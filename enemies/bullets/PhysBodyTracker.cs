using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class PhysBodyTrackerData
{
    public enum PhysBodyTrackerShape
    {
        CsgConvexHull,
        Sphere,
        Box,
        Capsule
    }

    public Transform3D GlobalTransform;
    public PhysBodyTrackerShape Type;
    public float[] Args;

    protected PhysBodyTrackerData(Transform3D global_transform, Shape3D shape)
    {
        GlobalTransform = global_transform;
        Type = shape switch
        {
            SphereShape3D => PhysBodyTrackerShape.Sphere,
            BoxShape3D => PhysBodyTrackerShape.Box,
            CapsuleShape3D => PhysBodyTrackerShape.Capsule,
            ConvexPolygonShape3D => PhysBodyTrackerShape.CsgConvexHull,
            _ => throw new ArgumentException($"Unsupported shape type: {shape.GetType()}"),
        };
        Args = Type switch
        {
            PhysBodyTrackerShape.Sphere => [((SphereShape3D)shape).Radius],
            PhysBodyTrackerShape.Box => [((BoxShape3D)shape).Size.X, ((BoxShape3D)shape).Size.Y, ((BoxShape3D)shape).Size.Z],
            PhysBodyTrackerShape.Capsule => [((CapsuleShape3D)shape).Radius, ((CapsuleShape3D)shape).Height],
            PhysBodyTrackerShape.CsgConvexHull => [],
            _ => throw new ArgumentException($"Unsupported shape type: {shape}"),
        };
    }

    public static PhysBodyTrackerData Create(Transform3D global_transform, Shape3D shape)
    {
        return shape switch
        {
            SphereShape3D => new PhysBodyTrackerData(global_transform, shape),
            BoxShape3D => new PhysBodyTrackerData(global_transform, shape),
            CapsuleShape3D => new PhysBodyTrackerData(global_transform, shape),
            ConvexPolygonShape3D convex => new CsgConvexHullData(global_transform, convex),
            _ => throw new ArgumentException($"Unsupported shape type: {shape.GetType()}"),
        };
    }
}

public class CsgConvexHullData : PhysBodyTrackerData
{
    public System.Numerics.Vector3[] TransformedPoints;

    public CsgConvexHullData(Transform3D global_transform, ConvexPolygonShape3D shape)
        : base(global_transform, shape)
    {
        TransformedPoints = new System.Numerics.Vector3[shape.Points.Length];
        for (int i = 0; i < shape.Points.Length; i++)
        {
            var pt = global_transform*shape.Points[i];
            TransformedPoints[i] = new System.Numerics.Vector3(pt.X, pt.Y, pt.Z);
        }
    }
}

/// <summary>
/// Attach to a physics body to track its position in a grid.
/// Bodies tracked like this will be used to collide with bullets spawned by the bulletmanager.
/// To enable thousands of bullets, the physics system is circumvented using this faster cheaper method.
/// </summary>
public partial class PhysBodyTracker : Node
{
    public struct TrackerKey { }

    private const float CellSize = 4f;

    // used to track bodies in a grid, for moving bodies which update their position
    private static readonly ConcurrentDictionary<PhysBodyTracker, HashSet<Vector3I>> _tracker_to_gridpos = [];
    // used to track occupied grid positions, to test collisions
    private static readonly ConcurrentDictionary<Vector3I, HashSet<PhysBodyTracker>> _gridpos_to_trackers = [];
    // used to associate bodies with a transform and shape data
    private static readonly ConcurrentDictionary<PhysBodyTracker, List<PhysBodyTrackerData>> _tracker_to_data = [];
    private static readonly ConcurrentDictionary<PhysicsBody3D, PhysBodyTracker> _tracked_bodies = [];

    // used to track cells that are empty, to clean them up later
    private static readonly Dictionary<Vector3I, float> _pendingCleanupCells = [];

    private Transform3D _body_prev_transform = Transform3D.Identity;
    public GodotObject ParentBody = null;
    //private readonly SemaphoreSlim _semaphore = new(1, 1);

    private static bool _already_clean_up_this_frame = false;
    
    private static readonly List<PhysBodyTracker> _trackers_to_clear_manually = [];

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        var body = GetParent();

        if (body is PhysicsBody3D phys_body)
        {
            phys_body.TreeExiting += () =>
            {
                //if (!_tracked_bodies.TryRemove(phys_body, out var removed_tracker)) { GD.Print("failed to remove body from tracker"); }
                _trackers_to_clear_manually.Add(this);
            };

            ParentBody = phys_body;
            _tracked_bodies.TryAdd(phys_body, this);
            //GD.Print($"Tracking {phys_body} ({phys_body.Name}) in grid");
            UpdateTrackerInGrid(this);
        }
        else
        {
            throw new Exception($"PhysBodyTracker must be child of a PhysicsBody3D, not {body.GetType()}");
        }

        // static body treasure chests are not tracked, but we need to update the grid when they are opened
        if (ParentBody is TreasureChest t)
        {
            t.Opened += () =>
            {
                //GD.Print("updating treasure chest in grid!");
                UpdateTrackerInGrid(this);
            };
        }
        // special case, big chest node structure is nested for no reason...
        else if (((Node3D)ParentBody).GetParent() is TreasureChest big_chest)
        {
            big_chest.Opened += () =>
            {
                //GD.Print("updating big chest in grid!");
                UpdateTrackerInGrid(this);
            };
        }

        CallDeferred(MethodName.Reparent, GetTree().Root);
    }

    /// <summary>
    /// This keeps the body's grid position updated.
    /// It's convenient to be able to do it here, but 
    /// can lead to concurrent modification exceptions.
    /// It's done in BulletManager's _PhysicsProcess instead
    /// </summary>
    /// <param name="delta"></param>
    public override void _PhysicsProcess(double delta)
    {
        // if (Engine.GetPhysicsFrames() % 2 == 0) return; // basic optimization to skip every other frame
        // ManualUpdateBodyInGrid
    }

    public void ManualUpdateTrackerInGrid()
    {
        // do not run in editor
        if (Engine.IsEditorHint()) return;

        // skip if body is static
        if (ParentBody is StaticBody3D) return;

        // only characterbody and rigid bodies need to be tracked for position changes
        var parent = (PhysicsBody3D)ParentBody;
        var new_transform = parent.GlobalTransform;
        if (new_transform != _body_prev_transform)
        {
            //GD.Print($"Updating parent Body");
            UpdateTrackerInGrid(this);
            _body_prev_transform = new_transform;
        }
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;
        if (!_already_clean_up_this_frame)
        {
            _already_clean_up_this_frame = true;
            CleanupEmptyCells((float)delta);
            CallDeferred(MethodName.ResetCleanupFlag);
        }
    }

    private static void ResetCleanupFlag()
    {
        _already_clean_up_this_frame = false;
    }

    public static void StopTrackingAndFree(PhysBodyTracker tracker)
    {
        RemoveTrackerFromGrid(tracker);
        if (!_tracker_to_data.TryRemove(tracker, out var _)) GD.Print($"Failed to remove tracker_to_data for {tracker}");
        _tracker_to_gridpos.TryRemove(tracker, out _);

        tracker.QueueFree();
    }

    private static void UpdateTrackerInGrid(PhysBodyTracker tracker)
    {
        RemoveTrackerFromGrid(tracker);
        if (tracker.ParentBody is not PhysicsBody3D body || !IsInstanceValid(tracker.ParentBody))
        {
            _trackers_to_clear_manually.Add(tracker);
            return;
        }

        if (body is StaticBody3D)
            {
                var children = body.GetChildren();
                var shape = children.OfType<CollisionShape3D>().FirstOrDefault();
                if (shape.Shape is ConvexPolygonShape3D conv)
                {
                    List<Vector3> pts = [];
                    foreach (var child in children)
                    {
                        if (child is CollisionShape3D cs && cs.Shape is ConvexPolygonShape3D d)
                        {
                            pts.AddRange(d.Points.Select(p => cs.GlobalTransform * p));
                        }
                    }
                    UpdateBodyPointCloudInGrid(tracker, body, pts);
                }
                else if (shape != null) UpdateBodyAABBInGrid(tracker, body);
                else throw new Exception($"Attempted to track StaticBody3D {body.Name} does not have a collision shape.");

                _tracker_to_data.TryRemove(tracker, out var _);
                var shape_data = new List<PhysBodyTrackerData>();
                foreach (var child in body.GetChildren())
                {
                    if (child is CollisionShape3D cs)
                    {
                        shape_data.Add(PhysBodyTrackerData.Create(cs.GlobalTransform, cs.Shape));
                    }
                }
                _tracker_to_data[tracker] = shape_data;
            }
            else // dynamic bodies
            {
                UpdateBodyAABBInGrid(tracker, body);

                if (body is Enemy en)
                {
                    var shape = en.CollisionShape;
                    if (_tracker_to_data.TryGetValue(tracker, out var enemy_data))
                    {
                        foreach (var d in enemy_data)
                            d.GlobalTransform = shape.GlobalTransform;
                    }
                    else _tracker_to_data[tracker] = [PhysBodyTrackerData.Create(shape.GlobalTransform, shape.Shape)];
                }
                else if (body is Player pc)
                {
                    var shape = pc.PlayerCollisionShape;
                    if (_tracker_to_data.TryGetValue(tracker, out var player_data))
                    {
                        foreach (var d in player_data)
                            d.GlobalTransform = shape.GlobalTransform;
                    }
                    else _tracker_to_data[tracker] = [PhysBodyTrackerData.Create(shape.GlobalTransform, shape.Shape)];
                }
                else
                {
                    _tracker_to_data.TryRemove(tracker, out var _);
                    var shape_data = new List<PhysBodyTrackerData>();
                    foreach (var child in body.GetChildren())
                    {
                        if (child is CollisionShape3D cs)
                        {
                            shape_data.Add(PhysBodyTrackerData.Create(cs.GlobalTransform, cs.Shape));
                        }
                    }
                    _tracker_to_data[tracker] = shape_data;
                }
            }
    }

    private static void UpdateBodyPointCloudInGrid(PhysBodyTracker tracker, PhysicsBody3D body, List<Vector3> point_cloud)
    {
        if (!_tracker_to_gridpos.TryGetValue(tracker, out var gridCells))
        {
            gridCells = [];
            _tracker_to_gridpos[tracker] = gridCells;
        }
        gridCells.Clear();

        for (int i = 0; i < point_cloud.Count; i++)
        {
            var cell = WorldToCell(point_cloud[i]);
            if (!_gridpos_to_trackers.TryGetValue(cell, out var set))
            {
                set = [];
                _gridpos_to_trackers[cell] = set;
            }
            set.Add(tracker);
            gridCells.Add(cell);
        }
    }

    private static void UpdateBodyAABBInGrid(PhysBodyTracker tracker, PhysicsBody3D body)
    {
        Aabb totalAabb = GetNodeAabb(body);

        if (totalAabb == default)
        {
            GD.PushWarning($"PhysicsBody3D '{body.Name}' did not have an aabb (aabb was default), returning.");
            return;
        }

        var min = WorldToCell(totalAabb.Position);
        var max = WorldToCell(totalAabb.Position + totalAabb.Size);


        if (!_tracker_to_gridpos.TryGetValue(tracker, out var gridCells))
        {
            gridCells = [];
            _tracker_to_gridpos[tracker] = gridCells;
        }
        gridCells.Clear();

        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    Vector3I cell = new(x, y, z);
                    gridCells.Add(cell);

                    if (!_gridpos_to_trackers.TryGetValue(cell, out var set))
                    {
                        set = [];
                        _gridpos_to_trackers[cell] = set;
                    }
                    set.Add(tracker);
                }
            }
        }
    }

    private static void RemoveTrackerFromGrid(PhysBodyTracker tracker)
    {
        if (_tracker_to_gridpos.TryGetValue(tracker, out var cells))
        {
            foreach (var cell in cells)
            {
                if (_gridpos_to_trackers.TryGetValue(cell, out var set))
                {
                    set.Remove(tracker);
                }
                if (set.Count == 0 && !_pendingCleanupCells.ContainsKey(cell))
                {
                    _pendingCleanupCells.Add(cell, 0f);
                }
            }
        }
    }

    private static void CleanupEmptyCells(float delta)
    {
        if (_pendingCleanupCells.Count == 0) return;

        var remove_from_cleanup_list = new List<Vector3I>();

        foreach (var (cell, t) in _pendingCleanupCells)
        {
            if (_gridpos_to_trackers.TryGetValue(cell, out var checkSet))
            {
                if (checkSet.Count > 0)
                {
                    remove_from_cleanup_list.Add(cell);
                }
                else
                {
                    if (t > 1f)
                    {
                        _gridpos_to_trackers.TryRemove(cell, out var _);
                        remove_from_cleanup_list.Add(cell);
                    }
                    else
                    {
                        _pendingCleanupCells[cell] = t + delta;
                    }
                }
            }
        }

        foreach (var cell in remove_from_cleanup_list)
            _pendingCleanupCells.Remove(cell);
    }

    private static Aabb GetNodeAabb(Node3D node)
    {
        Aabb totalAabb = default;
        bool foundShape = false;

        foreach (var pc in node.GetChildren(true))
        {
            if (pc is CollisionShape3D shape)
            {
                var aabb = ShapeToAabb(shape.Shape);
                aabb = shape.GlobalTransform * aabb;
                if (!foundShape)
                {
                    totalAabb = aabb;
                    foundShape = true;
                }
                else
                {
                    totalAabb = totalAabb.Merge(aabb);
                }
            }
        }

        return totalAabb;
    }

    private static Aabb ShapeToAabb(Shape3D shape)
    {
        if (shape is BoxShape3D box)
        {
            return new Aabb(-box.Size / 2f, box.Size);
        }
        else if (shape is SphereShape3D sphere)
        {
            return new Aabb(-Vector3.One * sphere.Radius, Vector3.One * sphere.Radius * 2f);
        }
        else if (shape is CapsuleShape3D capsule)
        {
            var size = new Vector3(capsule.Radius * 2, capsule.Height, capsule.Radius * 2);
            return new Aabb(-size / 2f, size);
        }
        else if (shape is ConvexPolygonShape3D convex)
        {
            Vector3 min = Vector3.One * float.MaxValue, max = Vector3.One * float.MinValue;

            foreach (var pt in convex.Points)
            {
                if (pt.X < min.X) min.X = pt.X;
                if (pt.X > max.X) max.X = pt.X;
                if (pt.Y < min.Y) min.Y = pt.Y;
                if (pt.Y > max.Y) max.Y = pt.Y;
                if (pt.Z < min.Z) min.Z = pt.Z;
                if (pt.Z > max.Z) max.Z = pt.Z;
            }
            var size = max - min;

            return new Aabb(-size / 2f, size);
        }
        return default;
    }

    public static Vector3I WorldToCell(Vector3 worldPos)
    {
        return new Vector3I(
            Mathf.FloorToInt(worldPos.X / CellSize),
            Mathf.FloorToInt(worldPos.Y / CellSize),
            Mathf.FloorToInt(worldPos.Z / CellSize)
        );
    }

    public static bool IsCellOccupied(Vector3I cell)
    {
        return _gridpos_to_trackers.ContainsKey(cell);
    }

    public static bool TryGetTrackersInCell(Vector3I cell, out HashSet<PhysBodyTracker> trackers)
    {
        return _gridpos_to_trackers.TryGetValue(cell, out trackers);
    }

    public static bool TryGetTrackerData(PhysBodyTracker tracker, out List<PhysBodyTrackerData> data)
    {
        return _tracker_to_data.TryGetValue(tracker, out data);
    }

    public static List<PhysBodyTracker> AllTrackers()
    {
        return [.. _tracker_to_data.Keys];
    }

    public static bool TrackedBodiesContainsPlayer()
    {
        return _tracked_bodies.ContainsKey(Player.Instance);
    }

    public static bool TryGetTrackerFromBody(PhysicsBody3D body, out PhysBodyTracker tracker)
    {
        if (_tracked_bodies.TryGetValue(body, out PhysBodyTracker value))
        {
            tracker = value;
            return true;
        }
        else
        {
            tracker = null;
            return false;
        }
    }

    public static void ManuallyFlushTrackers()
    {
        foreach (var tracker in _trackers_to_clear_manually)
        {
            StopTrackingAndFree(tracker);
        }
        _trackers_to_clear_manually.Clear();

        // GD.Print($"UNTRACKING. REMAINING BODIES COUNT: {_tracked_bodies.Count}");
        // GD.Print("Gridpos to trackers count: " + _gridpos_to_trackers.Count);
        // GD.Print("Tracker to gridpos count: " + _tracker_to_gridpos.Count);
        // GD.Print("Tracker to data count: " + _tracker_to_data.Count);
        // GD.Print("Pending cleanup cells count: " + _pendingCleanupCells.Count);
    }
    
    public static int NumTrackedBodies()
    {
        return _tracked_bodies.Count;
    }
}
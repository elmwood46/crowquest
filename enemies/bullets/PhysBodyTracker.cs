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
    private const float CellSize = 4f;

    // used to track bodies in a grid, for moving bodies which update their position
    private static readonly ConcurrentDictionary<object, HashSet<Vector3I>> _body_to_gridpos = [];
    // used to track occupied grid positions, to test collisions
    private static readonly ConcurrentDictionary<Vector3I, HashSet<object>> _gridpos_to_bodies = [];
    // used to associate bodies with a transform and shape data
    private static readonly ConcurrentDictionary<object, List<PhysBodyTrackerData>> _body_to_data = [];

    // used to track cells that are empty, to clean them up later
    private static readonly Dictionary<Vector3I, float> _pendingCleanupCells = [];

    private Transform3D _body_prev_transform = Transform3D.Identity;
    private GodotObject _parent_body = null;
    //private readonly SemaphoreSlim _semaphore = new(1, 1);

    private static bool _already_clean_up_this_frame = false;
    private static int _num_instances = 0;

    public override void _Ready()
    {
        _num_instances++;
        if (Engine.IsEditorHint()) return;
        var body = GetParent();

        if (body is PhysicsBody3D phys_body)
        {
            _parent_body = phys_body;
            UpdateBodyInGrid(phys_body);
        }
        else
        {
            throw new Exception($"PhysBodyTracker must be child of a PhysicsBody3D, not {body.GetType()}");
        }

        // static body treasure chests are not tracked, but we need to update the grid when they are opened
        if  (_parent_body is TreasureChest t)
        {
            t.Opened += () => {
                //GD.Print("updating treasure chest in grid!");
                UpdateBodyInGrid(t);
            };
        }
        // special case, big chest node structure is nested for no reason...
        else if (((Node3D)_parent_body).GetParent() is TreasureChest big_chest) 
        {
            big_chest.Opened += () => {
                //GD.Print("updating big chest in grid!");
                UpdateBodyInGrid(big_chest);
            };
        }
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

    public void ManualUpdateBodyInGrid()
    {
        // do not run in editor
        if (Engine.IsEditorHint()) return;

        // skip if body is static
        if (_parent_body is StaticBody3D) return;

        // only characterbody and rigid bodies need to be tracked for position changes
        var parent = (PhysicsBody3D)_parent_body;
        var new_transform = parent.GlobalTransform;
        if (new_transform != _body_prev_transform)
        {
            //GD.Print($"Updating parent Body");
            UpdateBodyInGrid(parent);
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

    public override void _ExitTree()
    {
        Callable.From(()=>
        { 
            GD.Print($"Exiting tree, PhysBodyTracker {this} removed from {GetParent()}.");
            _num_instances--;
            if (_parent_body == null) 
            {
                GD.PushWarning($"Trying to exit tree, PhysBodyTracker {this} has no parent body.");
                return;
            }

            // remove the body from the grid if it exists
            RemoveBodyFromGrid((PhysicsBody3D)_parent_body);
            _body_to_data.TryRemove((PhysicsBody3D)_parent_body, out var _);
            _body_to_gridpos.TryRemove(_parent_body, out _);
            if (_num_instances==0)
            {
                _gridpos_to_bodies.Clear();
                _body_to_gridpos.Clear();
                _pendingCleanupCells.Clear();
            }
        }).CallDeferred();

        base._ExitTree();
    }

    private static void UpdateBodyInGrid(PhysicsBody3D body)
    {
        if (body == null || !IsInstanceValid(body)) return;

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
                        pts.AddRange(d.Points.Select(p => cs.GlobalTransform*p));
                    }
                }
                UpdateBodyPointCloudInGrid(body, pts);
            }
            else if (shape != null) UpdateBodyAABBInGrid(body);
            else throw new Exception($"Attempted to track StaticBody3D {body.Name} does not have a collision shape.");

            _body_to_data.TryRemove(body, out var _);
            var shape_data = new List<PhysBodyTrackerData>();
            foreach (var child in body.GetChildren())
            {
                if (child is CollisionShape3D cs)
                {
                    shape_data.Add(PhysBodyTrackerData.Create(cs.GlobalTransform, cs.Shape));
                }
            }
            _body_to_data[body] = shape_data;
        }
        else // dynamic bodies
        {
            UpdateBodyAABBInGrid(body);

            if (body is Enemy en)
            {
                var shape = en.CollisionShape;
                if (_body_to_data.TryGetValue(en, out var enemy_data))
                {
                    foreach (var d in enemy_data)
                        d.GlobalTransform = shape.GlobalTransform;
                }
                else _body_to_data[en] = [PhysBodyTrackerData.Create(shape.GlobalTransform, shape.Shape)];
            }
            else if (body is Player pc)
            {
                var shape = pc.PlayerCollisionShape;
                if (_body_to_data.TryGetValue(pc, out var player_data))
                {
                    foreach (var d in player_data)
                        d.GlobalTransform = shape.GlobalTransform;
                }
                else _body_to_data[pc] = [PhysBodyTrackerData.Create(shape.GlobalTransform, shape.Shape)];
            }
            else 
            {
                _body_to_data.TryRemove(body, out var _);
                var shape_data = new List<PhysBodyTrackerData>();
                foreach (var child in body.GetChildren())
                {
                    if (child is CollisionShape3D cs)
                    {
                        shape_data.Add(PhysBodyTrackerData.Create(cs.GlobalTransform, cs.Shape));
                    }
                }
                _body_to_data[body] = shape_data;
            }
        }
    }

    private static void UpdateBodyPointCloudInGrid(PhysicsBody3D body, List<Vector3> point_cloud)
    {
        RemoveBodyFromGrid(body);

        if (!_body_to_gridpos.TryGetValue(body, out var gridCells))
        {
            gridCells = [];
            _body_to_gridpos[body] = gridCells;
        }
        gridCells.Clear();

        for (int i=0; i< point_cloud.Count; i++)
        {
            var cell = WorldToCell(point_cloud[i]);
            if (!_gridpos_to_bodies.TryGetValue(cell, out var set))
            {
                set = [];
                _gridpos_to_bodies[cell] = set;
            }
            set.Add(body);
            gridCells.Add(cell);
        }
    }

    private static void UpdateBodyAABBInGrid(PhysicsBody3D body)
    {
        RemoveBodyFromGrid(body);

        Aabb totalAabb = GetNodeAabb(body);

        if (totalAabb == default)
        {
            GD.PushWarning($"PhysicsBody3D '{body.Name}' did not have an aabb (aabb was default), returning.");
            return;
        }

        var min = WorldToCell(totalAabb.Position);
        var max = WorldToCell(totalAabb.Position + totalAabb.Size);


        if (!_body_to_gridpos.TryGetValue(body, out var gridCells))
        {
            gridCells = [];
            _body_to_gridpos[body] = gridCells;
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

                    if (!_gridpos_to_bodies.TryGetValue(cell, out var set))
                    {
                        set = [];
                        _gridpos_to_bodies[cell] = set;
                    }
                    set.Add(body);
                }
            }
        }
    }

    private static void RemoveBodyFromGrid(PhysicsBody3D body)
    {
        if (_body_to_gridpos.TryGetValue(body, out var cells))
        {
            foreach (var cell in cells)
            {
                if (_gridpos_to_bodies.TryGetValue(cell, out var set))
                {
                    set.Remove(body);
                }
                if (set.Count == 0 && !_pendingCleanupCells.ContainsKey(cell))
                {
                    _pendingCleanupCells.Add(cell,0f);
                }
            }
        }
    }

    private static void CleanupEmptyCells(float delta)
    {
        if (_pendingCleanupCells.Count == 0) return;

        var toRemove = new List<Vector3I>();

        foreach (var (cell,t) in _pendingCleanupCells)
        {
            if (_gridpos_to_bodies.TryGetValue(cell, out var checkSet))
            {
                if (checkSet.Count > 0)
                {
                    toRemove.Add(cell);
                }
                else
                {
                    if (t > 1f)
                    {
                        _gridpos_to_bodies.TryRemove(cell, out var _);
                        toRemove.Add(cell);
                    }
                    else
                    {
                        _pendingCleanupCells[cell] = t + delta;
                    }
                }
            }
        }

        foreach (var cell in toRemove)
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
                aabb = shape.GlobalTransform*aabb;
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
            var size = new Vector3(capsule.Radius*2, capsule.Height, capsule.Radius*2);
            return new Aabb(-size/2f, size);
        }
        else if (shape is ConvexPolygonShape3D convex)
        {
            Vector3 min = Vector3.One*float.MaxValue, max = Vector3.One*float.MinValue;

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

            return new Aabb(-size/2f, size);
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
        return _gridpos_to_bodies.ContainsKey(cell);
    }

    public static bool TryGetBodiesInCell(Vector3I cell, out HashSet<object> bodies)
    {
        return _gridpos_to_bodies.TryGetValue(cell, out bodies);
    }

    public static bool TryGetBodyData(object body, out List<PhysBodyTrackerData> data)
    {
        return _body_to_data.TryGetValue(body, out data);
    }

    public static List<PhysicsBody3D> AllTrackedBodies()
    {
        return [.. _body_to_data.Keys.OfType<PhysicsBody3D>()];
    }
}
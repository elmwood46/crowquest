using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Manages the spawning of bullets, as well as the collision detection and removal of bullets.
/// This class is a singleton and should be accessed through the Instance property.
/// </summary>
public partial class BulletManager : Node
{
    [Export] public Node3D FollowInstance {get;set;}
    [Export] public MultiMeshInstance3D BulletMultimesh {get; set;}
    [Export] public int MaxBullets {get; private set;} = 500;
    [Export] public float BulletRadius {get;set;} = 0.25f;// must match multimesh's mesh sphere radius (default 0.25)
    private static readonly List<Dictionary<string,Variant>> _basic_bullets = [];
    private static readonly ConcurrentQueue<Dictionary<string,Variant>> _bullets_to_add = new();
    private static readonly ConcurrentStack<int> _bullet_idx_to_remove = new();
    private static BulletManager Instance;
    //private static readonly Transform3D HIDE_DOWN = new(Basis.Identity,new Vector3(0.0f, -10000.0f, 0.0f));
    private static readonly PackedScene _bullet_death_particles = ResourceLoader.Load("res://enemies/bullets/bullet_death.tscn") as PackedScene;
    private static readonly ConvexPolygonShape3D _sphere_convex_shape = GD.Load<ConvexPolygonShape3D>("res://enemies/bullets/test/bulletConvexShape.tres");
    private static readonly Vector3[] _sphere_convex_shape_verts = _sphere_convex_shape.Points;
    
    private const float BULLET_MAX_DISTANCE = 160f;
    // limit particle effect for destroying bullets to prevent stuttering
    private const int MAX_PARTICLE_NODES_CREATED_PER_FRAME = 10;

    private static readonly Vector3 XZ_ONE = new(1,0,1);

    public override void _Ready()
    {
        Instance = this;
        BulletMultimesh.Multimesh.InstanceCount = MaxBullets;
    }

    public static void AddBullet(
        PhysicsBody3D shooter,
        int damage,
        DamageTypeFlagEnum damage_type,
        Vector3 shot_direction,
        float speed,
        bool harms_enemies = true,
        Vector3 start_position = default,
        float homing_rate = 0.0f,
        Color color = default)
    {
        if (color == default) color = Colors.DarkOrange;
        var start_pos = start_position == default ? shooter.GlobalPosition+Vector3.Up*0.5f : start_position;
        shot_direction = shot_direction.Normalized();
        var start_transform =  new Transform3D(Basis.Identity,start_pos).LookingAt(start_pos+shot_direction, Vector3.Up);

        _bullets_to_add.Enqueue(new Dictionary<string, Variant>()
        {
            {"damage", damage},
            {"damage_type", (int)damage_type},
            {"speed", speed},
            {"shot_direction", shot_direction},
            {"shooter_id", shooter},
            {"distance_travelled", 0f},
            {"transform", start_transform},
            {"harms_enemies", harms_enemies},
            {"homing_rate", homing_rate},
            {"color", color},
            {"is_enemy_bullet", shooter is Enemy}
        });
    }

    async public override void _PhysicsProcess(double delta)
    {
        // bullet multimesh follows player
        BulletMultimesh.GlobalPosition = BulletMultimesh.GlobalPosition.Lerp(FollowInstance.GlobalPosition, 0.1f);

        // add bullets to the multimesh
        while (!_bullets_to_add.IsEmpty)
        {
            if (!_bullets_to_add.TryDequeue(out var bullet_data)) continue;
            if (_basic_bullets.Count == Instance.MaxBullets) _basic_bullets.RemoveAt(0);
            _basic_bullets.Add(bullet_data);
        }

        // set visible instances to number bullets
        BulletMultimesh.Multimesh.VisibleInstanceCount = _basic_bullets.Count;

        // early exit if no bullets -- still update the physics bodies in the grid
        var trackers_list = PhysBodyTracker.AllTrackers();
        if (_basic_bullets.Count == 0)
        {
            if (Engine.GetPhysicsFrames() % 2ul == 0) foreach (var tracker in trackers_list)
                {
                    tracker.ManualUpdateTrackerInGrid();
                }
            PhysBodyTracker.ManuallyFlushTrackers();

            return;
        }

        // get the global position of the multimesh and player
        var neg_multimesh_glob_pos = -BulletMultimesh.GlobalPosition;
        var player_xz_glob_pos = XZ_ONE * FollowInstance.GlobalPosition;

        // fetch data lists
        var enemies_trackers_list = trackers_list.Where(b => b.ParentBody is Enemy).ToList();
        List<Vector3> tracked_enemies_targetable = Player.Instance == null ? [] :
            [.. Player.Instance.GetActiveNonBlockedEnemiesInArea().Select(e => e.GlobalPosition * XZ_ONE)];
        PhysBodyTracker.TryGetTrackerFromBody(Player.Instance, out var player_tracker);

        // create body collision exclusion map, and update multimesh
        Dictionary<int, HashSet<PhysBodyTracker>> exclusion_map = [];
        var mm = BulletMultimesh.Multimesh;
        for (var i = 0; i < _basic_bullets.Count; i++)
        {
            mm.SetInstanceTransform(i, ((Transform3D)_basic_bullets[i]["transform"]).Translated(neg_multimesh_glob_pos));
            mm.SetInstanceColor(i, (Color)_basic_bullets[i]["color"]);

            // exclusion map
            exclusion_map[i] = [];
            var shooter_id = (PhysicsBody3D)_basic_bullets[i]["shooter_id"];
            if (PhysBodyTracker.TryGetTrackerFromBody(shooter_id, out var shooter_tracker))
            {
                exclusion_map[i].Add(shooter_tracker);
            }

            if (Player.Instance.HasRollIFrames()) exclusion_map[i].Add(player_tracker);

            if (!(bool)_basic_bullets[i]["harms_enemies"]) foreach (var e in enemies_trackers_list) exclusion_map[i].Add(e);
        }

        // do collisions and position updates in background thread
        await Task.Run(() =>
        {
            for (var i = 0; i < _basic_bullets.Count; i++)
            {
                // get bullet data
                if (!exclusion_map.TryGetValue(i, out var exclude_bodies)) exclude_bodies = [];
                var bullet = _basic_bullets[i];
                if (bullet == null) continue;
                var bullet_transform = (Transform3D)bullet["transform"];
                var speed = (float)bullet["speed"];
                var shot_direction = (Vector3)bullet["shot_direction"];
                var harms_enemies = (bool)bullet["harms_enemies"];
                var homing_rate = (float)bullet["homing_rate"];

                // do homing
                if (homing_rate > Mathf.Epsilon)
                {
                    bool is_enemy_bullet = (bool)bullet["is_enemy_bullet"];
                    if (is_enemy_bullet)
                    {
                        var homing_dir = (player_xz_glob_pos - bullet_transform.Origin * XZ_ONE).Normalized();
                        shot_direction = shot_direction.Lerp(homing_dir, homing_rate);
                    }
                    else if (tracked_enemies_targetable.Count > 0)
                    {
                        Vector3 bullet_pos = bullet_transform.Origin * XZ_ONE;
                        float min_dist_sq = float.MaxValue;
                        Vector3 closest_enemy_pos = bullet_pos;

                        foreach (var enemy_pos in tracked_enemies_targetable)
                        {
                            float dist_sq = bullet_pos.DistanceSquaredTo(enemy_pos);
                            if (dist_sq < min_dist_sq)
                            {
                                min_dist_sq = dist_sq;
                                closest_enemy_pos = enemy_pos;
                            }
                        }

                        Vector3 homing_dir = (closest_enemy_pos - bullet_pos).Normalized();
                        shot_direction = shot_direction.Lerp(homing_dir, homing_rate);
                    }
                    shot_direction = shot_direction.Normalized();
                }
                bullet["shot_direction"] = shot_direction;

                // calc motion vector
                var len = speed * (float)delta;
                var motion_vector = shot_direction * len;

                // update distance and check for collision
                bullet["distance_travelled"] = (float)bullet["distance_travelled"] + len;
                if ((float)bullet["distance_travelled"] >= BULLET_MAX_DISTANCE)
                {
                    DestroyBulletWithParticles(i, bullet_transform);
                }
                else
                {
                    if (!CheckForCollision(i, bullet_transform, motion_vector, exclude_bodies))
                    {
                        bullet["transform"] = bullet_transform.Translated(motion_vector);
                    }
                }
            }
        });


        // remove bullets flagged for removal
        while (!_bullet_idx_to_remove.IsEmpty)
        {
            if (!_bullet_idx_to_remove.TryPop(out var bullet_index)) continue;
            if (bullet_index < 0 || bullet_index >= _basic_bullets.Count)
            {
                continue; // this stops duplicate values in stack from causing errors
            }
            _basic_bullets.RemoveAt(bullet_index);
        }
        _bullet_idx_to_remove.Clear();

        // update the physics bodies in the grid
        // including removing enemies
        // doing this manually here avoids concurrent modification exceptions

        if (Engine.GetPhysicsFrames() % 2ul == 0) foreach (var tracker in trackers_list)
        {
            tracker.ManualUpdateTrackerInGrid();
        }
        PhysBodyTracker.ManuallyFlushTrackers();
    }

    private static bool CheckForCollision(int bullet_idx,
        Transform3D bullet_global_transform,
        Vector3 motion_vec,
        HashSet<PhysBodyTracker> exclude_trackers = null)
    {
        var bullet_cell = PhysBodyTracker.WorldToCell(bullet_global_transform.Origin);
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    var cell = bullet_cell + new Vector3I(x, y, z);
                    if (PhysBodyTracker.IsCellOccupied(cell) && PhysBodyTracker.TryGetTrackersInCell(cell, out var trackers))
                    {
                        try
                        {
                            foreach (PhysBodyTracker tracker in trackers)
                            {
                                if (exclude_trackers.Contains(tracker)) continue;
                                

                                if (PhysBodyTracker.TryGetTrackerData(tracker, out var shape_data))
                                {
                                    foreach (var tracked_shape in shape_data)
                                    {
                                        if (tracked_shape is null) continue;
                                        if (IsBulletIntersectShape(
                                            bullet_global_transform.Translated(motion_vec),
                                            Instance.BulletRadius,
                                            tracked_shape))
                                        {
                                            DoBulletCollision(
                                                tracker,
                                                bullet_idx,
                                                bullet_global_transform.Translated(motion_vec));
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            // can get concurrent modification exception if multiple threads are trying to access the same cell
                            GD.PushError("Error in BulletManager.CheckForCollision: ", e);
                            continue;
                        }
                    }
                }
            }
        }
        return false;
    }

    private static bool IsBulletIntersectShape(Transform3D bullet_glob_transform, float bullet_radius, PhysBodyTrackerData tracked_shape)
    {
        if (tracked_shape.Type == PhysBodyTrackerData.PhysBodyTrackerShape.Sphere)
        {
            var static_sphere_radius = tracked_shape.Args[0]*tracked_shape.GlobalTransform.Basis.Scale.X;
            return SphereToSphere(bullet_glob_transform.Origin, bullet_radius, tracked_shape.GlobalTransform.Origin, static_sphere_radius);
        }
        else if (tracked_shape.Type == PhysBodyTrackerData.PhysBodyTrackerShape.Capsule)
        {
            return SphereToCapsule(
                bullet_glob_transform.Origin,
                bullet_radius,
                tracked_shape.GlobalTransform,
                tracked_shape.Args[0], //radius
                tracked_shape.Args[1]); //height
        }
        else if (tracked_shape.Type == PhysBodyTrackerData.PhysBodyTrackerShape.Box)
        {
            var size = new Vector3(tracked_shape.Args[0],tracked_shape.Args[1],tracked_shape.Args[2]);
            return SphereToOBB(bullet_glob_transform.Origin, bullet_radius, tracked_shape.GlobalTransform, size);
        }
        else if (tracked_shape.Type == PhysBodyTrackerData.PhysBodyTrackerShape.CsgConvexHull)
        {
            return SphereToConvexHull(bullet_glob_transform.Origin, bullet_radius, ((CsgConvexHullData)tracked_shape).TransformedPoints);
        }
        return false;
    }

    private static void DoBulletCollision(PhysBodyTracker tracker, int bullet_idx, Transform3D bullet_transform)
    {
        if (_bullet_idx_to_remove.Contains(bullet_idx)) return;

        Dictionary<string, Variant> bullet_data;
        if (bullet_idx >= 0 && bullet_idx < _basic_bullets.Count) bullet_data = _basic_bullets[bullet_idx];
        else return;

        var damage = (int)bullet_data["damage"];
        var damtype = (DamageTypeFlagEnum)(int)bullet_data["damage_type"];

        Callable.From(() =>
        {
            if (tracker.ParentBody is IHurtable hurtable)
            {
                hurtable.TakeDamage(damage, damtype);
            }
        }).CallDeferred();

        DestroyBulletWithParticles(bullet_idx, bullet_transform);
    }

    private static void DestroyBulletWithParticles(int bullet_idx, Transform3D bullet_transform)
    {
        _bullet_idx_to_remove.Push(bullet_idx);

        if (_bullet_idx_to_remove.Count <= MAX_PARTICLE_NODES_CREATED_PER_FRAME)
        {
            Callable.From(() =>
            {
                var death_particles = _bullet_death_particles.Instantiate<GpuParticles3D>();
                Instance.GetTree().Root.AddChild(death_particles);
                death_particles.GlobalTransform = bullet_transform;
                //death_particles.Emitting = true;
            }).CallDeferred();
        }
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Helper Functions
    /// Used for debugging etc
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetBulletCountString()
    {
        return _basic_bullets.Count.ToString();
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Collision Functions
    /// These functions are used to handle the collision of bullets with other objects in the game.
    /// 
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static bool SphereToSphere(Vector3 sphere1, float radius1, Vector3 sphere2, float radius2)
    {
        return sphere1.DistanceSquaredTo(sphere2) <= (radius1 + radius2) * (radius1 + radius2);
    }

    public static bool SphereToCapsule( 
        Vector3 sphereCenter,
        float sphereRadius,
        Transform3D capsuleTransform,
        float capsuleRadius,
        float capsuleHeight)
    {
        var half_cyl_height = (capsuleHeight - 2f*capsuleRadius)/2f;
        var worldTop = capsuleTransform * new Vector3(0, half_cyl_height, 0);
        var worldBottom = capsuleTransform * new Vector3(0, -half_cyl_height, 0);
        float totalRadius = sphereRadius + capsuleRadius*capsuleTransform.Basis.Scale.X; // assume uniform scaling
        return sphereCenter.DistanceSquaredTo(ClosestPointOnLineSegment(worldBottom, worldTop, sphereCenter)) <= totalRadius * totalRadius;
    }

    public static bool SphereToOBB(Vector3 sphereCenter,float sphereRadius,Transform3D boxTransform, Vector3 boxSize)
    {
        // Transform sphere into the box’s local space
        var inv = boxTransform.AffineInverse();
        var localSphere = inv*sphereCenter;
        var local_radius = inv.Basis.Scale.X * sphereRadius; // assume uniform scaling
        var halfExtents = boxSize * 0.5f;

        // Clamp to box bounds in local space
        float x = Math.Clamp(localSphere.X, -halfExtents.X, halfExtents.X);
        float y = Math.Clamp(localSphere.Y, -halfExtents.Y, halfExtents.Y);
        float z = Math.Clamp(localSphere.Z, -halfExtents.Z, halfExtents.Z);
        return localSphere.DistanceSquaredTo(new Vector3(x, y, z)) <= local_radius * local_radius;
    }

    public static bool SphereToAABB(Vector3 sphereCenter, float sphereRadius, Vector3 boxCenter, Vector3 boxSize)
    {
        var halfExtents = boxSize * 0.5f;
        var min = boxCenter - halfExtents;
        var max = boxCenter + halfExtents;

        // Clamp sphere center to box
        float x = Math.Clamp(sphereCenter.X, min.X, max.X);
        float y = Math.Clamp(sphereCenter.Y, min.Y, max.Y);
        float z = Math.Clamp(sphereCenter.Z, min.Z, max.Z);

        return sphereCenter.DistanceSquaredTo(new Vector3(x, y, z)) <= sphereRadius * sphereRadius;
    }


    public static bool SphereToConvexHull(Vector3 sphere_centre, float sphere_radius, System.Numerics.Vector3[] convex_hull)
    {
        var transformed_bullet_collision_box = new System.Numerics.Vector3[_sphere_convex_shape_verts.Length];
        for (int i=0;i<_sphere_convex_shape_verts.Length;i++)
        {
            var box_vert = sphere_centre+(_sphere_convex_shape_verts[i]*sphere_radius);
            transformed_bullet_collision_box[i] = new System.Numerics.Vector3(box_vert.X,box_vert.Y,box_vert.Z);
        }
        return OpenGJKSharp.OpenGJKSharp.HasCollision(transformed_bullet_collision_box, convex_hull);
    }

    private static Vector3 ClosestPointOnLineSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float abLengthSquared = ab.LengthSquared();

        if (abLengthSquared == 0f)
            return a;

        float t = (point - a).Dot(ab) / abLengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        return a + ab * t;
    }
}
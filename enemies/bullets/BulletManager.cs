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
    [Export] public MultiMeshInstance3D BulletMultimesh {get; set;}
    [Export] public int MaxBullets {get; private set;} = 500;
    [Export] public float BulletRadius // must match multimesh's mesh sphere radius (default 0.25)
        {
            get => _bullet_radius;
            private set
            {
                _bullet_radius = value;

                _bullet_collision_box = new Vector3[CUBE_VERTS.Length];
                for (int i=0;i<CUBE_VERTS.Length;i++)
                {
                    _bullet_collision_box[i] = value*2f*CUBE_VERTS[i];
                }
            }
        }
    private static readonly List<Dictionary<string,Variant>> _basic_bullets = [];
    private static readonly ConcurrentQueue<Dictionary<string,Variant>> _bullets_to_add = new();
    private static readonly ConcurrentStack<int> _bullet_idx_to_remove = new();
    private static BulletManager Instance;
    private static readonly Transform3D HIDE_DOWN = new(Basis.Identity,new Vector3(0.0f, -10000.0f, 0.0f));
    private static readonly PackedScene BulletDeathScene = ResourceLoader.Load("res://enemies/bullets/bullet_death.tscn") as PackedScene;
    private static readonly ConvexPolygonShape3D _sphere_convex_shape = GD.Load<ConvexPolygonShape3D>("res://enemies/bullets/test/bulletConvexShape.tres");
    private static readonly Vector3[] _sphere_convex_shape_verts = _sphere_convex_shape.Points;
    private const float BULLET_MAX_DISTANCE = 160f;
    private const int MAX_BULLETS_DESTROYED_AT_ONCE = 10;
    private static readonly Vector3[] CUBE_VERTS = // used to collide bullet with convex hulls (bullet uses a box collision for these bc its small)
        {
            new(-0.5f, -0.5f, -0.5f),
            new(0.5f, -0.5f, -0.5f),
            new(-0.5f, 0.5f, -0.5f),
            new(0.5f, 0.5f, -0.5f),
            new(-0.5f, -0.5f, 0.5f),
            new(0.5f, -0.5f, 0.5f),
            new(-0.5f, 0.5f, 0.5f),
            new(0.5f, 0.5f, 0.5f)
        };

    private Vector3[] _bullet_collision_box;
    private float _bullet_radius = 0.25f;

    public override void _Ready()
    {
        Instance = this;
        BulletMultimesh.Multimesh.InstanceCount = MaxBullets;
    }

    public override void _Process(double delta)
    {
        // add new bullets
        while (!_bullets_to_add.IsEmpty)
        {
            if (!_bullets_to_add.TryDequeue(out var bullet_data)) continue;
            if (_basic_bullets.Count == Instance.MaxBullets) _basic_bullets.RemoveAt(0);
            _basic_bullets.Add(bullet_data);
        }
    }

    async public override void _PhysicsProcess(double delta)
    {
        BulletMultimesh.GlobalPosition = BulletMultimesh.GlobalPosition.Lerp(SimpleController.Instance.GlobalPosition,0.1f);

        // performance optimization
        BulletMultimesh.Multimesh.VisibleInstanceCount = _basic_bullets.Count;
        if (_basic_bullets.Count==0) return;

        var neg_multimesh_glob_pos = -BulletMultimesh.GlobalPosition;
        
        for (var i=0;i<_basic_bullets.Count;i++)
        {
            BulletMultimesh.Multimesh.SetInstanceTransform(i, ((Transform3D)_basic_bullets[i]["transform"]).Translated(neg_multimesh_glob_pos));
        }

        await Task.Run(()=>{
            for (var i=0;i<_basic_bullets.Count;i++)
            {
                var bullet = _basic_bullets[i];
                var bullet_transform = (Transform3D)bullet["transform"];
                var speed = (float)bullet["speed"];
                var shot_direction = (Vector3)bullet["shot_direction"];
                var shooter_rid = (Rid)bullet["shooter_rid"];
                var exclude_bodies = new Godot.Collections.Array<Rid>();
                if (shooter_rid.IsValid) exclude_bodies.Add(shooter_rid);
            
                var len = speed * (float)delta;
                var motion_vector = shot_direction * len;

                bullet["distance_travelled"] = (float)bullet["distance_travelled"] + len;
                if ((float)bullet["distance_travelled"] >= BULLET_MAX_DISTANCE)
                {
                    DestroyBullet(i, bullet_transform);
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
        // use stack to remove bullets in reverse order
        // so that the indices of the remaining bullets are not changed
        while (!_bullet_idx_to_remove.IsEmpty)
        {
            if (!_bullet_idx_to_remove.TryPop(out var bullet_index)) continue;
            if (bullet_index < 0 || bullet_index >= _basic_bullets.Count) 
            {
                // duplicate values in stack sometimes has this result
                continue;
            }
            _basic_bullets.RemoveAt(bullet_index);
        }
        _bullet_idx_to_remove.Clear();
    }

    public static string GetBulletCountString()
    {
        return _basic_bullets.Count.ToString();
    }

    public static void AddBullet(PhysicsBody3D shooter, Dictionary<string,Variant> bullet_data, Vector3 start_position = new Vector3())
    {
        var damage = (int)bullet_data["damage"];
        var damage_type = (DamageType)(int)bullet_data["damage_type"];
        var shot_direction = (Vector3)bullet_data["shot_direction"];
        var speed = (float)bullet_data["speed"];
        AddBullet(shooter, damage, damage_type, shot_direction, speed, start_position);
    }

    public static void AddBullet(PhysicsBody3D shooter, int damage, DamageType damage_type, Vector3 shot_direction, float speed, Vector3 start_position = default)
    {
        var start_pos = start_position == default ? shooter.GlobalPosition+Vector3.Up*0.5f : start_position;
        shot_direction = shot_direction.Normalized();
        var start_transform =  new Transform3D(Basis.Identity,start_pos).LookingAt(start_pos+shot_direction, Vector3.Up);

        _bullets_to_add.Enqueue(new Dictionary<string, Variant>()
        {
            {"damage", damage},
            {"damage_type", (int)damage_type},
            {"speed", speed},
            {"shot_direction", shot_direction},
            {"shooter_rid", shooter.GetRid()},
            {"distance_travelled", 0f},
            {"transform", start_transform}
        });
    }

    private static bool CheckForCollision(int bullet_idx, Transform3D bullet_global_transform, Vector3 motion_vector, Godot.Collections.Array<Rid> exclude_bodies = null)
    {
        var bullet_cell = PhysBodyTracker.WorldToCell(bullet_global_transform.Origin);
        for (int x=-1;x<=1;x++)
        {
            for (int y=-1;y<=1;y++)
            {
                for (int z=-1;z<=1;z++)
                {
                    var cell = bullet_cell + new Vector3I(x,y,z);
                    if (PhysBodyTracker.IsCellOccupied(cell)&&PhysBodyTracker.TryGetBodiesInCell(cell, out var bodies))
                    {
                        foreach (var body in bodies.ToList())
                        {
                            if (body is not PhysicsBody3D phys_body || !IsInstanceValid(phys_body) || exclude_bodies.Contains(phys_body.GetRid())) continue;
                            if (PhysBodyTracker.TryGetBodyData(body, out var shape_data))
                            {
                                foreach (var tracked_shape in shape_data.ToList())
                                {
                                    if (CollideBulletWithShape(
                                        bullet_global_transform.Translated(motion_vector),
                                        Instance.BulletRadius,
                                        tracked_shape))
                                    {
                                        BulletCollide(
                                            (GodotObject)body,
                                            bullet_idx,
                                            bullet_global_transform.Translated(motion_vector));
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    private static bool CollideBulletWithShape(Transform3D bullet_glob_transform, float bullet_radius, PhysBodyTrackerData tracked_shape)
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

    private static void BulletCollide(GodotObject body, int bullet_idx, Transform3D bullet_transform)
    {
        if (_bullet_idx_to_remove.Contains(bullet_idx)) return;

        var damage = (int)_basic_bullets[bullet_idx]["damage"];
        var damtype = (DamageType)(int)_basic_bullets[bullet_idx]["damage_type"];

        Callable.From(() =>
        {
            // DEBUG print body name
            // if (body is Node3D d) GD.Print(d.Name);
            // else if (body is null) GD.Print("null");
            // else GD.Print(body.GetType());

            if (body is IHurtable hurtable)
            {
                hurtable.TakeDamage(damage, damtype);
            }
        }).CallDeferred();

        DestroyBullet(bullet_idx, bullet_transform);
    }

    private static void DestroyBullet(int bullet_idx, Transform3D bullet_transform)
    {
        _bullet_idx_to_remove.Push(bullet_idx);

        if (_bullet_idx_to_remove.Count < MAX_BULLETS_DESTROYED_AT_ONCE)
        {
            Callable.From(() =>
            {
                
                var death_particles = BulletDeathScene.Instantiate<GpuParticles3D>();
                Instance.GetTree().Root.AddChild(death_particles);
                death_particles.GlobalTransform = bullet_transform;
                //death_particles.Emitting = true;
            }).CallDeferred();
        }
    }

    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// Collision Functions
    /// These functions are used to handle the collision of bullets with other objects in the game.
    /// 
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public static bool SphereToSphere(Vector3 sphere1, float radius1, Vector3 sphere2, float radius2)
    {
        return sphere1.DistanceSquaredTo(sphere2) <= (radius1 + radius2)*(radius1 + radius2);
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
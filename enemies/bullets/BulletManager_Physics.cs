using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class BulletManager_Physics : Node
{
    [Export] public MultiMeshInstance3D BulletMultimesh {get; set;}
    [Export] public int MaxBullets {get; private set;} = 500;
    private static readonly List<Dictionary<string,Variant>> _basic_bullets = [];
    private static readonly List<int> _bullets_to_remove = [];
    private static BulletManager_Physics Instance;
    private static readonly Transform3D HIDE_DOWN = new(Basis.Identity,new Vector3(0.0f, -10000.0f, 0.0f));
    private static readonly PackedScene BulletDeathScene = ResourceLoader.Load("res://enemies/bullets/bullet_death.tscn") as PackedScene;
    private const float BULLET_MAX_DISTANCE = 160f;
    public override void _Ready()
    {
        Instance = this;
        BulletMultimesh.Multimesh.InstanceCount = MaxBullets;
    }

    public override void _PhysicsProcess(double delta)
    {
        BulletMultimesh.GlobalPosition = BulletMultimesh.GlobalPosition.Lerp(Player.Instance.GlobalPosition,0.1f);
        // performance optimization
        if (_basic_bullets.Count == 0 || Engine.GetPhysicsFrames() % 3 == 0) return;

        BulletMultimesh.Multimesh.VisibleInstanceCount = _basic_bullets.Count;
        
        for (var i=0;i<_basic_bullets.Count;i++)
        {
            var bullet = _basic_bullets[i];
            var bullet_rid = (Rid)bullet["bullet_rid"];
            var speed = (float)bullet["speed"];
            var shot_direction = (Vector3)bullet["shot_direction"];
            var shooter_rid = (Rid)bullet["shooter_rid"];
            var exclude_bodies = new Godot.Collections.Array<Rid>() {bullet_rid};
            if (shooter_rid.IsValid) exclude_bodies.Add(shooter_rid);

            var transform = (Transform3D)PhysicsServer3D.BodyGetState(bullet_rid, PhysicsServer3D.BodyState.Transform);
            BulletMultimesh.Multimesh.SetInstanceTransform(i, transform.Translated(-Instance.BulletMultimesh.GlobalPosition));
        
            var motion_vector = shot_direction * speed * (float)delta;

            bullet["distance_travelled"] = (int)bullet["distance_travelled"] + speed * (float)delta;
            if ((int)bullet["distance_travelled"] >= BULLET_MAX_DISTANCE)
            {
                DestroyBullet(i, transform);
            }
            else
            {
                if (!TestMotion(i, motion_vector, exclude_bodies))
                {
                    if (!TestMotion(i, -motion_vector, exclude_bodies))
                    {
                        PhysicsServer3D.BodySetState(bullet_rid, PhysicsServer3D.BodyState.Transform, transform.Translated(motion_vector));
                    }
                }
            }
        }

        // clear bullets from list

        _bullets_to_remove.Sort();
        for (int i=_bullets_to_remove.Count-1;i>=0;i--)
        {
            var bullet_idx = _bullets_to_remove[i];
            var bullet_data = _basic_bullets[bullet_idx];
            PhysicsServer3D.FreeRid((Rid)bullet_data["collision_shape_rid"]);
            PhysicsServer3D.FreeRid((Rid)bullet_data["bullet_rid"]);
            _basic_bullets.RemoveAt(bullet_idx);
        }
        _bullets_to_remove.Clear();
    }

    private static bool TestMotion(int bullet_index, Vector3 motion_vector, Godot.Collections.Array<Rid> exclude_bodies)
    {
        var bullet = _basic_bullets[bullet_index];
        var bullet_rid = (Rid)bullet["bullet_rid"];
        var transform = (Transform3D)PhysicsServer3D.BodyGetState(bullet_rid, PhysicsServer3D.BodyState.Transform);
    
        var motion_params = new PhysicsTestMotionParameters3D()
        {
            CollideSeparationRay = true,
            ExcludeBodies = exclude_bodies,
            From = transform,
            Motion = motion_vector,
        };
        var motion_result = new PhysicsTestMotionResult3D();

        var collided = PhysicsServer3D.BodyTestMotion(bullet_rid, motion_params, motion_result);
        if (collided)
        {
            BulletCollide(motion_result.GetCollider(), bullet_index, transform);
        }
        return collided;
    }

    public override void _ExitTree()
    {
        foreach (var bullet in _basic_bullets)
        {
            PhysicsServer3D.FreeRid((Rid)bullet["collision_shape_rid"]);
            PhysicsServer3D.FreeRid((Rid)bullet["bullet_rid"]);
        }
        _basic_bullets.Clear();
    }

    public static void AddBullet(PhysicsBody3D shooter, Dictionary<string,Variant> bullet_data, Vector3 start_position = new Vector3())
    {
        var damage = (int)bullet_data["damage"];
        var damage_type = (DamageType)(int)bullet_data["damage_type"];
        var shot_direction = (Vector3)bullet_data["shot_direction"];
        var speed = (float)bullet_data["speed"];
        AddBullet(shooter, damage, damage_type, shot_direction, speed, start_position);
    }

    public static void AddBullet(PhysicsBody3D shooter, int damage, DamageType damage_type, Vector3 shot_direction, float speed, Vector3 start_position = new Vector3())
    {
        var bullet = PhysicsServer3D.BodyCreate();
        var start_pos = start_position == Vector3.Zero ? shooter.GlobalPosition+Vector3.Up*0.5f : start_position;
        //start_pos -= Instance.BulletMultimesh.GlobalPosition;
        shot_direction = shot_direction.Normalized();
        PhysicsServer3D.BodySetMode(bullet, PhysicsServer3D.BodyMode.Kinematic);
        PhysicsServer3D.BodySetState(bullet, PhysicsServer3D.BodyState.Transform, 
            new Transform3D(
                Basis.Identity,
                start_pos
            ).LookingAt(start_pos+shot_direction, Vector3.Up)
        );
        PhysicsServer3D.BodySetSpace(bullet, shooter.GetWorld3D().Space);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.GravityScale, 0.0f);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.LinearDampMode, (int)PhysicsServer3D.BodyDampMode.Replace);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.AngularDampMode, (int)PhysicsServer3D.BodyDampMode.Replace);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.LinearDamp, 0.0f);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.AngularDamp, 0.0f);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.Friction, 0.0f);
        // PhysicsServer3D.BodySetParam(bullet, PhysicsServer3D.BodyParameter.Mass, 1.0f);

        var collision_shape = PhysicsServer3D.SphereShapeCreate();
        PhysicsServer3D.ShapeSetData(collision_shape, 0.25f); // sets the radius of sphereshape
        PhysicsServer3D.BodyAddShape(bullet, collision_shape);
        PhysicsServer3D.BodySetCollisionLayer(bullet, 1u << 20);
        PhysicsServer3D.BodySetCollisionMask(bullet, ~(1u << 20));
        // PhysicsServer3D.BodyApplyCentralImpulse(bullet, shot_direction * speed);
        
        if (_basic_bullets.Count == Instance.MaxBullets)
        {
            var old_data = _basic_bullets[0];
            PhysicsServer3D.FreeRid((Rid)old_data["collision_shape_rid"]);
            PhysicsServer3D.FreeRid((Rid)old_data["bullet_rid"]);
            _basic_bullets.RemoveAt(0);
        }

        _basic_bullets.Add(new Dictionary<string, Variant>()
        {
            {"bullet_rid", bullet},
            {"collision_shape_rid", collision_shape},
            {"damage", damage},
            {"damage_type", (int)damage_type},
            {"speed", speed},
            {"shot_direction", shot_direction},
            {"shooter_rid", shooter.GetRid()},
            {"distance_travelled", 0f}
        });
    }

    private static void BulletCollide(GodotObject body, int bullet_idx, Transform3D bullet_transform)
    {
        if (_bullets_to_remove.Contains(bullet_idx)) return;
        if (body is IHurtable hurtable)
        {
            var bullet_data = _basic_bullets[bullet_idx];
            var damage = (int)bullet_data["damage"];
            var damtype = (DamageType)(int)bullet_data["damage_type"];
            hurtable.TakeDamage(damage, damtype);
            DestroyBullet(bullet_idx, bullet_transform);
        }
        else if (body is StaticBody3D || body is CsgShape3D)
        {
            DestroyBullet(bullet_idx, bullet_transform);
        }
    }

    private static void DestroyBullet(int bullet_idx, Transform3D bullet_transform)
    {
        Instance.BulletMultimesh.Multimesh.SetInstanceTransform(bullet_idx, HIDE_DOWN);
        var death_particles= BulletDeathScene.Instantiate<GpuParticles3D>();
        Instance.GetTree().Root.AddChild(death_particles);
        death_particles.GlobalTransform = bullet_transform;
        death_particles.Emitting = true;
        _bullets_to_remove.Add(bullet_idx);
    }
}

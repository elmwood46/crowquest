using Godot;
using System;
using System.Collections.Generic;
using System.Linq;



public partial class Pickup : RigidBody3D, IPickup
{
    private const double PICKUP_LIFETIME = 60;
    protected Timer _deathtimer = new() {WaitTime = 1d, Autostart = false, OneShot = true};
    protected Timer _lifetime = new() {WaitTime = PICKUP_LIFETIME,Autostart = false,OneShot = true};
    public bool ActivatePickup = false;
    [Export] public Godot.Collections.Array<AudioStream> PickupSounds {get;set;}
    [Export] public Godot.Collections.Array<AudioStream> ImpactSounds {get;set;}
    [Export] public AudioBus Bus {get;set;} = AudioBus.Misc;
    [Export] public RayCast3D GroundCheckRaycast {get;set;}
    protected const float _lerpfactor = 0.2f;
    protected Vector3 _base_mesh_scale;
    protected Vector3 _base_collision_scale;
    protected MeshInstance3D _mesh;
    protected CollisionShape3D _collision;

    protected InteractableComponent _interactable;
    protected bool _spawn_position_set = false;
    protected bool _floated_to_ground = false;

    protected string _base_name;
    protected Vector3 _spawn_position;
    public override void _Ready()
    {
        if (PhysicsMaterialOverride == null)
        {
            PhysicsMaterialOverride = new PhysicsMaterial()
            {
                Friction = 1.0f,
                Bounce = 0.2f
            };
        }
        else PhysicsMaterialOverride.Friction = 1.0f;
        LinearDamp = 1.0f;
        AngularDamp = 1.0f;

        // play impact sound
        BodyEntered += (body) =>
        {
            if (!(body is StaticBody3D || body is RigidBody3D)) return;

            if (ImpactSounds.Count > 0 && !Freeze && LinearVelocity.LengthSquared() > 0.2f)
            {
                var stream = ImpactSounds[Random.Shared.Next(0, ImpactSounds.Count)];
                AudioManager.TryPlay(stream, Bus, GlobalPosition, pitch_scale:0.9f+0.2f*Random.Shared.NextSingle());
            }
        };

        SetCollisionLayerValue(1, false);
        SetCollisionLayerValue(2, false);
        SetCollisionLayerValue(3, true);
        SetCollisionMaskValue(1, true);
        SetCollisionMaskValue(2, false);
        SetCollisionMaskValue(3, true);
        SetCollisionMaskValue(9, false);
        _mesh = GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
        _collision = GetChildren().OfType<CollisionShape3D>().FirstOrDefault();
        _base_mesh_scale = _mesh.Scale;
        _base_collision_scale = _collision.Scale;
        _lifetime.Timeout += () =>
        {
            _deathtimer.Start();
        };
        _deathtimer.Timeout += Deactivate;
        AddChild(_deathtimer);
        AddChild(_lifetime);
        _lifetime.Start();

        if (this is not Coin)
        {
            _interactable = new();
            AddChild(_interactable);
            _interactable.Connect(nameof(InteractableComponent.Interacted), Callable.From(() => { ActivatePickup = true; }));
        }

        Visible = true;
        Freeze = false;
    }

    public void SetSpawnPosition(Vector3 spawn_position)
    {
        _spawn_position = spawn_position;
        _spawn_position_set = true;
    }

    public void ForcePhysicsStateUpdate(Vector3 translate, Vector3 linear_velocity, Vector3 angular_velocity)
    {
        var rid = GetRid();
        PhysicsServer3D.BodySetState(
            rid,
            PhysicsServer3D.BodyState.Transform,
            Transform3D.Identity.Translated(translate)
        );
        PhysicsServer3D.BodySetState(
            rid,
            PhysicsServer3D.BodyState.LinearVelocity,
            linear_velocity
        );
        PhysicsServer3D.BodySetState(
            rid,
            PhysicsServer3D.BodyState.AngularVelocity,
            angular_velocity
        );
        GlobalPosition = translate;
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        AngularVelocity = new Vector3(0, 1, 0);
        if (_spawn_position_set)
        {
            if (!_floated_to_ground)
            {
                if (IsOnFloor()) _floated_to_ground = true;
                else
                {
                    _spawn_position = new Vector3(_spawn_position.X, _spawn_position.Y - 0.1f, _spawn_position.Z);
                    GlobalPosition = _spawn_position;
                }
            }
            else GlobalPosition = new Vector3(GlobalPosition.X, _spawn_position.Y + 0.1f * Mathf.Sin(GlobalPosition.X + GlobalPosition.Z + Mathf.Tau * Engine.GetPhysicsFrames() / 60f), GlobalPosition.Z);
        }
        base._IntegrateForces(state);
    }

    public override void _PhysicsProcess(double delta)
    {
        // shrink effect
        if (!_deathtimer.IsStopped())
        {
            _mesh.Scale = _base_mesh_scale * (float)Math.Max(_deathtimer.TimeLeft / _deathtimer.WaitTime, 0.1f);
            _collision.Scale = _base_collision_scale * (float)Math.Max(_deathtimer.TimeLeft / _deathtimer.WaitTime, 0.1f);
        }
        else
        {
            _mesh.Scale = _base_mesh_scale;
            _collision.Scale = _base_collision_scale;
        }

        // pickup effect
        if (ActivatePickup && Player.Instance != null)
        {
            Freeze = true;
            FreezeMode = FreezeModeEnum.Static;
            LerpTowardsPlayer();
        }

        if (GlobalPosition.Y < -100f)
        {
            Deactivate();
            QueueFree();
        }
    }
    
    public bool IsOnFloor()
    {
        GroundCheckRaycast.ForceRaycastUpdate();
        var col = GroundCheckRaycast.GetCollider();
        return col != null && (col is StaticBody3D || col is RigidBody3D);
    }

    public void LerpTowardsPlayer()
    {
        SetCollisionMaskValue(1, false);
        SetCollisionMaskValue(3, false);
        SetCollisionMaskValue(9, false);
        SetCollisionLayerValue(1, false);
        SetCollisionLayerValue(3, false);
        SetCollisionLayerValue(9, false);
        var targ_pos = Player.Instance.GlobalPosition + Vector3.Up * 0.5f;
        GlobalPosition = GlobalPosition.Lerp(targ_pos, _lerpfactor);
        if (GlobalPosition.DistanceSquaredTo(targ_pos) <= 0.5f)
        {
            OnPickup();
            if (PickupSounds.Count > 0)
            {
                var pickup_sound = PickupSounds[Random.Shared.Next(0, PickupSounds.Count)];
                float pitch_scale;
                if (this is Coin) 
                {
                    pitch_scale = 0.8f + 0.4f * Player.Instance.GetXpRatio();
                    AudioManager.TryPlayTrackedSound("player_gem"+GetHashCode(),pickup_sound, Bus, Player.Instance.GlobalPosition,pitch_scale:pitch_scale);
                }
                else 
                {
                    pitch_scale = 0.9f + 0.2f * Random.Shared.NextSingle();
                    AudioManager.TryPlayTrackedSound("player_pickup"+GetHashCode(),pickup_sound, Bus, Player.Instance.GlobalPosition,pitch_scale:pitch_scale);
                }
            }
            Deactivate();
        }
    }

    virtual public void Deactivate()
    {
        GD.Print("Deactivating pickup "+this);
        QueueFree();
    }

    virtual public void OnPickup()
    {
        // override this in inherited classes
        GD.Print("OVERRIDE ME: picked up item "+this);
    }
}

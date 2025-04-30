using Godot;
using System;
using System.Collections.Generic;



public partial class Pickup : RigidBody3D, IPickup
{
    private const double PICKUP_LIFETIME = 60;
    protected Timer _deathtimer = new() {WaitTime = 1, Autostart = false, OneShot = true};
    protected Timer _lifetime = new() {WaitTime = PICKUP_LIFETIME,Autostart = false,OneShot = true};
    public bool ActivatePickup = false;
    [Export] public Godot.Collections.Array<AudioStream> PickupSounds {get;set;}
    [Export] public Godot.Collections.Array<AudioStream> ImpactSounds {get;set;}
    [Export] public AudioBus Bus {get;set;} = AudioBus.Misc;
    protected const float _lerpfactor = 0.5f;
    protected Vector3 _base_scale; 

    protected InteractableComponent _interactable;

    protected string _base_name;

    public override void _Ready()
    {
        // play impact sound
        BodyEntered += (body) => {
            if (!(body is StaticBody3D || body is RigidBody3D)) return;
            
            if (ImpactSounds.Count > 0 && !Freeze && LinearVelocity.LengthSquared() > 0.2f)
            {
                var stream = ImpactSounds[Random.Shared.Next(0,ImpactSounds.Count)];
                AudioManager.TryPlay(stream, Bus, GlobalPosition);
            }
        };

        SetCollisionLayerValue(1,false);
        SetCollisionLayerValue(2,false);
        SetCollisionLayerValue(3,true);
        SetCollisionMaskValue(1,true);
        SetCollisionMaskValue(2,false);
        SetCollisionMaskValue(3,true);
        SetCollisionMaskValue(9,true);

        _base_scale = ((MeshInstance3D)GetChild(0)).Scale;
        _lifetime.Timeout += () => {
            _deathtimer.Start();
        };
        _deathtimer.Timeout += Deactivate;
        AddChild(_deathtimer);
        AddChild(_lifetime);

        if (this is not Coin)
        {
            _interactable = new();
            AddChild(_interactable);
            _interactable.Connect(nameof(InteractableComponent.Interacted),Callable.From(()=>{ActivatePickup = true;}));
        }
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

    public override void _PhysicsProcess(double delta)
    {
        // disable
        if (Freeze) return;

        // shrink effect
        if (!_deathtimer.IsStopped())
        {
            ((MeshInstance3D)GetChild(0)).Scale = _base_scale*(float)Math.Max(_deathtimer.TimeLeft/_deathtimer.WaitTime,0.1f);
            ((CollisionShape3D)GetChild(1)).Scale = _base_scale*(float)Math.Max(_deathtimer.TimeLeft/_deathtimer.WaitTime,0.1f);
        }
        else 
        {
            ((MeshInstance3D)GetChild(0)).Scale = _base_scale;
            ((CollisionShape3D)GetChild(1)).Scale = _base_scale;        
        }

        // pickup effect
        if (ActivatePickup && Player.Instance != null)
        {
            LerpTowardsPlayer();
        }
    }

    public void LerpTowardsPlayer()
    {
        SetCollisionMaskValue(1,false);
        SetCollisionMaskValue(3,false);
        float dx,dy,dz;
        var targ_pos = Player.Instance.GlobalPosition+Vector3.Up*0.5f;
        dx = Mathf.Lerp(GlobalPosition.X,targ_pos.X,_lerpfactor);
        dy = Mathf.Lerp(GlobalPosition.Y,targ_pos.Y,_lerpfactor);
        dz = Mathf.Lerp(GlobalPosition.Z,targ_pos.Z,_lerpfactor);
        GlobalPosition = new Vector3(dx,dy,dz);
        if (GlobalPosition.DistanceSquaredTo(targ_pos) <= 0.5f)
        {
            OnPickup();
            if (PickupSounds.Count > 0)
            {
                var pickup_sound = PickupSounds[Random.Shared.Next(0,PickupSounds.Count)];
                AudioManager.TryPlay(pickup_sound,Bus,Player.Instance.GlobalPosition);
            }
            Deactivate();
        }
    }

    virtual public void Deactivate()
    {
        QueueFree();
    }

    virtual public void OnPickup()
    {
        // override this in inherited classes
        GD.Print("OVERRIDE ME: picked up item "+this);
    }
}

using System.Collections.Generic;
using Godot;

public partial class Explosion : Node3D
{
	public const float DEFAULT_CHAR_BODY_MASS = 10.0f;
	[Export] public Area3D ExplosionCollisionArea {get;set;}
	[Export] public float Damage {get;set;} = 10.0f;
	[Export] public float ExplosionForce {get;set;} = 500.0f;
	[Export] public Node3D ExplosionVfxScene;
	private AnimationPlayer _explosionAnimation;
	private float _explosion_radius = 1.0f;
	private bool _do_explosion = false;

	// Called when the node enters the scene tree for the first time.
	public override async void _Ready()
	{
		if (ExplosionCollisionArea.GetChild(0) is CollisionShape3D shape && shape.Shape is SphereShape3D sphere)
		{
			_explosion_radius = sphere.Radius;
		}
		ExplosionCollisionArea.SetCollisionLayerValue(1,true);
		ExplosionCollisionArea.SetCollisionLayerValue(2,true);
		ExplosionCollisionArea.SetCollisionLayerValue(3,true);
		ExplosionCollisionArea.SetCollisionLayerValue(4,true);
		ExplosionCollisionArea.SetCollisionLayerValue(9,true);
		ExplosionCollisionArea.SetCollisionMaskValue(1,true);
		ExplosionCollisionArea.SetCollisionMaskValue(2,true);
		ExplosionCollisionArea.SetCollisionMaskValue(3,true);
		ExplosionCollisionArea.SetCollisionMaskValue(4,true);
		ExplosionCollisionArea.SetCollisionMaskValue(9,true);
		ExplosionVfxScene.Visible = false;
		foreach (var child in ExplosionVfxScene.GetChildren())
		{
			if (child is AnimationPlayer a)
			{
				_explosionAnimation = a;
				break;
			}
		}

		await ToSignal(GetTree(), "physics_frame");
		await ToSignal(GetTree(), "physics_frame");

		_do_explosion = true;
	}

	private void PushAwayObjects() {
		Godot.Collections.Array<Node3D> _colliding_nodes = ExplosionCollisionArea.GetOverlappingBodies();

		foreach (Node3D node in _colliding_nodes) {
			if (!IsInstanceValid(node)) continue;
			GD.Print("body found: " + node.Name);
			var body_position = node.GlobalPosition;
			
			// calculate mass
			var mass = 1.0f;
			if (node is CharacterBody3D) {
				body_position.Y += 1.0f;
				mass = DEFAULT_CHAR_BODY_MASS; // characterBody3D has no mass so we set it here
			}
			else if (node is RigidBody3D rb) {
				mass = Mathf.Max(0.01f,rb.Mass);
			}

			var force_dir = GlobalPosition.DirectionTo(body_position);
			var bodyDist = body_position.DistanceTo(GlobalPosition);
			var knockbackFromRadius = ExplosionForce
				* (1f - Mathf.Min(bodyDist/_explosion_radius,1f))
				/ mass
				* force_dir;

			if (node is CharacterBody3D c) {
				c.Velocity += knockbackFromRadius;
			}
			else if (node is RigidBody3D rb) {
				rb.ApplyImpulse(knockbackFromRadius);
			}

			// damaging destructible mesh
			// if (node is PhysicsBody3D pb && IsInstanceValid(pb.GetParent().GetParent()) && pb.GetParent().GetParent() is DestructibleMesh mesh) {
			// 	if (!mesh.IsBroken() && IsInstanceValid((Node3D)mesh.IntactScene.GetChild(0)) && ((Node3D)mesh.IntactScene.GetChild(0)).IsInsideTree())
			// 	{
			// 		var damage = EnemyAttackPatterns.SphereDamageDropoff(GlobalPosition,((Node3D) mesh.IntactScene.GetChild(0)).GlobalPosition, Damage, _explosion_radius);
			// 		GD.Print("damaging destructo mesh ", mesh, " with ", damage, " damage");
			// 		mesh.TakeDamage(damage, DamageType.Fire);
			// 	}
			// }

			if (node is IHurtable hurtable) {
				var damage = EnemyAttackPatterns.SphereDamageDropoff(GlobalPosition, body_position, Damage, _explosion_radius);
				GD.Print("Damaging hurtable ", hurtable, " with ", damage, " damage");
				hurtable.TakeDamage(damage, DamageType.Physical);
			}
		}
	}

    public override void _PhysicsProcess(double delta)
    {
        if (_do_explosion) {
			PushAwayObjects();
			PlayExplosionAnimation();
			_do_explosion = false;
		}
    }

    private void PlayExplosionAnimation() {
		ExplosionVfxScene.Visible = true;
		_explosionAnimation.Play("init");
		GD.Print("exploded with radius ", _explosion_radius);

		// timer to destroy explosion after animation
        var _animation_timer = new Timer()
        {
            WaitTime = _explosionAnimation.CurrentAnimationLength
        };
        _animation_timer.Timeout += () => {
			ExplosionVfxScene.Visible = false;
			QueueFree();
		};
		AddChild(_animation_timer);
		_animation_timer.Start();
	}
}
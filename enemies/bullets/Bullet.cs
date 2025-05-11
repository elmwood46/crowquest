using Godot;
using System;

public partial class Bullet : Node3D
{
    [Export] public ShapeCast3D BulletShapeCast;
    [Export] public float BulletSpeed = 20.0f;
    public Vector3 BulletDirection = Vector3.Forward;
    [Export] public float BulletLifeTime = 100.0f;
    [Export] public int BulletDamage = 10;
    [Export] public DamageType BulletDamageType = DamageType.Physical;
    [Export] public bool IsPlayerBullet = false;
    [Export] public Node3D BulletVisual;
    [Export] public GpuParticles3D BulletDeathScene;
    private bool _bullet_is_destroyed = false;

    public override void _Ready()
    {
        var t = new Timer()
        {
            WaitTime = BulletLifeTime,
            OneShot = true
        };
        t.Timeout += DestroyBullet;
        AddChild(t);
        t.Start();
    }

    private void BulletCollide(GodotObject body)
    {
        if (body is IHurtable hurtable)
        {
            if (hurtable is Player p && !IsPlayerBullet)
            {
                p.TakeDamage(BulletDamage, BulletDamageType);
                DestroyBullet();
            }
            else if (hurtable is Enemy e && IsPlayerBullet)
            {
                e.TakeDamage(BulletDamage, BulletDamageType);
                DestroyBullet();
            } 
            else if (hurtable is not Player && hurtable is not Enemy)
            {
                hurtable.TakeDamage(BulletDamage, BulletDamageType);
                DestroyBullet();
            }
        }
        else if (body is StaticBody3D || body is CsgShape3D)
        {
            DestroyBullet();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_bullet_is_destroyed) return;
        if (!BulletVisual.Visible) return;

        for (var i=0;i<1;i++)
        {
            BulletShapeCast.TargetPosition = BulletDirection * (i+1) * 0.1f;
            BulletShapeCast.ForceShapecastUpdate();
            if (BulletShapeCast.IsColliding())
            {
                var hitObject = BulletShapeCast.GetCollider(0);
                BulletCollide(hitObject);
            }
        }

        if (_bullet_is_destroyed) return;

        var step = BulletDirection * BulletSpeed * (float)delta;
        GlobalPosition += step;
    }

    private void DestroyBullet()
    {
        if (!_bullet_is_destroyed)
        {
            _bullet_is_destroyed = true;
            BulletVisual.GetNode<GpuParticles3D>("GPUParticles3D").AmountRatio = 0f;
            BulletVisual.GetNode<MeshInstance3D>("MeshInstance3D").Visible = false;
            var delete_timer = new Timer()
            {
                WaitTime = 2.0f,
                OneShot = true
            };
            delete_timer.Timeout += QueueFree;
            AddChild(delete_timer);
            delete_timer.Start();
            BulletDeathScene.Emitting = true;
        }
    }
}

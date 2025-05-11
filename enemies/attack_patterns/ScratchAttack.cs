using Godot;
using System;

public partial class ScratchAttack : Node3D, IAttack
{
    public static readonly string AttackName = "EliScratchAttack";
    private static readonly PackedScene ScratchAttackHitbox = ResourceLoader.Load("res://enemies/attack_hitboxes/scratch_hitbox.tscn") as PackedScene; 
    public int AttackState {get;private set;} = 0;
    private Node3D _hitbox;
    public bool CanBeInterrupted => true;
    public bool CanMoveDuring => true;
    public bool IsFinished {get;set;} = false;
    private Timer _attackTimer;
    private Timer _cooldown = new() {WaitTime = 3.0, OneShot = true};
    private Vector2 _cooldown_range = new(0.5f,1.0f); // seconds
    public bool CanTrigger(Enemy enemy)
    {
        if (!_cooldown.IsInsideTree())
        {
            enemy.AddChild(_cooldown);
        }
        return enemy.IsPlayerInRange(16.0f) && _cooldown.IsStopped();
    }
    private static readonly AudioStream ScratchSound = ResourceLoader.Load("res://enemies/enemy_instances/elijah/audio/cat_attack.wav") as AudioStream;

    // this state machine is so unnecessarily messy lmao but it works
    public void Execute(Enemy enemy)
    {
        if (IsFinished) return;

        if (AttackState == 0)
        {
            _attackTimer = new Timer()
            {
                WaitTime = Random.Shared.Next(1,5)/10.0f,
                OneShot = true
            };
            enemy.AddChild(_attackTimer);
            _attackTimer.Start();

            enemy.AnimStateMachine.Travel("windup_scratch");

            AttackState = 1;

            enemy.ForceZeroVelocity();
        }
        else if (AttackState == 1 && _attackTimer.IsStopped())
        {
            enemy.ImpulseTowardsPlayer(15.0f);
            enemy.AnimStateMachine.Travel("scratch");
            _hitbox = ScratchAttackHitbox.Instantiate() as Node3D;
            enemy.AddChild(_hitbox);
            _hitbox.GlobalPosition = enemy.GlobalPosition;
            _hitbox.LookAt(_hitbox.GlobalPosition+enemy.GetYDirectionToPlayer(), Vector3.Up);
            _attackTimer.WaitTime = 1.0f;
            _attackTimer.Start();
            enemy.StopIdleSoundTimer();
            AudioManager.TryPlay(ScratchSound, AudioBus.Enemies, enemy.GlobalPosition);

            var hitbox_duration_timer = new Timer()
            {
                WaitTime = 0.2,
                OneShot = true
            };
            hitbox_duration_timer.Timeout += () =>
            {
                if (_hitbox != null && IsInstanceValid(_hitbox) && _hitbox.IsInsideTree()) _hitbox.QueueFree();
                else _hitbox = null;
                hitbox_duration_timer.QueueFree();
            };
            enemy.AddChild(hitbox_duration_timer);
            hitbox_duration_timer.Start();

            AttackState = 2;
        }
        else if (AttackState == 2 && !_attackTimer.IsStopped())
        {
            enemy.ForceTowardsPlayer(20.0f);
            if (IsInstanceValid(_hitbox))
            {
                foreach (var body in ((Area3D)_hitbox.GetChild(0)).GetOverlappingBodies())
                {
                    if (body is Player p)
                    {
                        p.TakeDamage(5, DamageType.Physical);
                    }
                }
            }
        }
        else if (AttackState == 2 &&_attackTimer.IsStopped())
        {
            Finish(enemy);
        }
    }

    public void Finish(Enemy enemy)
    {
        _cooldown.Stop();
        _cooldown.WaitTime = _cooldown_range.X + Random.Shared.NextSingle()*(_cooldown_range.Y-_cooldown_range.X);
        _cooldown.Start();
        IsFinished = true;
        if (IsInstanceValid(_hitbox)) _hitbox.QueueFree();
        if (IsInstanceValid(_attackTimer)) _attackTimer.QueueFree();
        enemy.AnimStateMachine.Travel("base_idle", true);
        AttackState = 0;
    }

    public void ResetParams()
    {
        IsFinished = false;
        if (IsInstanceValid(_attackTimer))_attackTimer.QueueFree();
        if (IsInstanceValid(_hitbox)) _hitbox.QueueFree();
        AttackState = 0;
    }
}
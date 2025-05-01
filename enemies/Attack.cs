using Godot;
using System;

public partial class ScratchAttack : Node3D, IAttack
{
    private static readonly PackedScene ScratchAttackHitbox = ResourceLoader.Load("res://enemies/attack_hitboxes/scratch_hitbox.tscn") as PackedScene; 
    private int _state = 0;
    private Node3D _hitbox;
    public bool CanBeInterrupted => true;
    public bool CanMoveDuring => true;
    public bool IsFinished {get;set;} = false;
    public bool CanTrigger(Enemy enemy)
    {
        return enemy.IsPlayerInRange(2.0f);
    }
    private static readonly AudioStream ScratchSound = ResourceLoader.Load("res://enemies/elijah/audio/cat_attack.wav") as AudioStream;

    public void Execute(Enemy enemy)
    {
        if (IsFinished) return;

        if (_state == 0)
        {
            enemy.ResetAttackTimer();
            enemy.AttackTimer.WaitTime = 0.5f;
            enemy.AttackTimer.Start();
            enemy.AnimStateMachine.Travel("windup_scratch");
            _state = 1;
        }
        else if (_state == 1 && enemy.AttackTimer.IsStopped())
        {
            enemy.AttackTimer.WaitTime = 1.0f;
            enemy.AnimStateMachine.Travel("scratch");
            _hitbox = ScratchAttackHitbox.Instantiate() as Node3D;
            enemy.AddChild(_hitbox);
            _hitbox.GlobalPosition = enemy.GlobalPosition;
            _hitbox.LookAt(_hitbox.GlobalPosition+enemy.GetYDirectionToPlayer(), Vector3.Up);
            enemy.AttackTimer.Start();
            enemy.StopIdleSoundTimer();
            AudioManager.TryPlay(ScratchSound, AudioBus.Enemies, enemy.GlobalPosition);
            _state = 2;
        }
        else if (_state == 2 && !enemy.AttackTimer.IsStopped())
        {
            if (IsInstanceValid(_hitbox) && enemy.AttackTimer.TimeLeft > 0.95f)
            {
                foreach (var body in ((Area3D)_hitbox.GetChild(0)).GetOverlappingBodies())
                {
                    if (body is Player p)
                    {
                        p.TakeDamage(5, DamageType.Physical);
                    }
                }
            }
            else
            {
                if (_hitbox != null && IsInstanceValid(_hitbox) && _hitbox.IsInsideTree()) _hitbox.QueueFree();
                else _hitbox = null;
            }
        }
        else if (_state == 2 && enemy.AttackTimer.IsStopped())
        {
            Finish(enemy);
        }
    }

    public void Finish(Enemy enemy)
    {
        IsFinished = true;
        if (IsInstanceValid(_hitbox)) _hitbox.QueueFree();
        enemy.AnimStateMachine.Travel("base_idle", true);
        _state = 0;
    }

    public void ResetParams()
    {
        IsFinished = false;
        _state = 0;
    }
}
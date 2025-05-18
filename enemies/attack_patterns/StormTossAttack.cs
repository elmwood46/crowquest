using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class StormTossAttack : Node3D, IAttack
{
    public int AttackState {get;private set;} = 0;
    public bool CanBeInterrupted => false;
    public bool CanMoveDuring => false;
    public bool IsFinished {get;set;} = false;
    private Timer _cooldown = new() {WaitTime = 3.0, OneShot = true};
    private Vector2 _cooldown_range = new(2f,3f); // seconds
    private Enemy _enemy_to_toss;
    private Node3D _target_node_when_holding;

    public bool CanTrigger(Enemy enemy)
    {
        if (!_cooldown.IsInsideTree())
        {
            enemy.AddChild(_cooldown);
        }
        _target_node_when_holding ??= (enemy.GetNodeOrNull<Node3D>("PositionToHoldEnemy") ?? throw new Exception("PositionToHoldEnemy node not found"));

        if (!(_cooldown.IsStopped() && enemy.IsPlayerInRange(32.0f) && enemy.IsPlayerVisible())) return false;

        var detect_throwable_enemies = enemy.GetNodeOrNull<Area3D>("DetectThrowableEnemies") ?? throw new Exception("DetectThrowableEnemies node not found");
        var enemies = detect_throwable_enemies.GetOverlappingBodies().Where(node => node is Enemy en && IsPickuppable(en));
        return enemies.Any();
    }
    private static readonly AudioStream PickupSound = ResourceLoader.Load("res://enemies/enemy_instances/storm/lift_grunt.ogg") as AudioStream;
    private static readonly AudioStream StormSqueal = ResourceLoader.Load("res://enemies/enemy_instances/storm/storm_squeal.ogg") as AudioStream;
    private static readonly AudioStream TossSound = ResourceLoader.Load("res://enemies/enemy_instances/storm/storm_toss.ogg") as AudioStream;

    private static bool IsPickuppable(Enemy enemy)
    {
        return enemy.Visible && enemy.Tags.Contains(Enemy.TagEnum.CanBePickedUp) && enemy.State != Enemy.StateEnum.IsPickedUp && !enemy.IsStunned() && !enemy.IsDead();
    }

    // this state machine is so unnecessarily messy lmao but it works
    public void Execute(Enemy enemy)
    {
        if (IsFinished) return;

        if (enemy == null || !IsInstanceValid(enemy))
        {
            GD.Print("Enemy is null or invalid. Cancelling storm toss attack.");
            ResetParams();
            IsFinished = true;
            return;
        }

        if (AttackState == 0)
        {
            enemy.ForceZeroVelocity();
            var detect_throwable_enemies = enemy.GetNodeOrNull<Area3D>("DetectThrowableEnemies") ?? throw new Exception("DetectThrowableEnemies node not found");
            var enemies = detect_throwable_enemies.GetOverlappingBodies()
                .Where(node => node is Enemy en && IsPickuppable(en))
                .OfType<Enemy>();
            if (!enemies.Any())
            {
                GD.Print("no enemies to toss");
                Finish(enemy);
                return;
            }
            //GD.Print("enemies: " + enemies.Count());
            var min_dist = float.MaxValue;
            foreach (var en in enemies)
            {
                var dist = enemy.GlobalPosition.DistanceSquaredTo(en.GlobalPosition);
                if (dist < min_dist)
                {
                    _enemy_to_toss = en;
                    min_dist = dist;
                }
            }
            _enemy_to_toss.StopAttacking();
            _enemy_to_toss.State = Enemy.StateEnum.IsPickedUp;
            _enemy_to_toss.AnimStateMachine.Travel("picked_up", true);
            _enemy_to_toss.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
            _enemy_to_toss.Freeze = true;
            _enemy_to_toss.SetCollisionLayerValue(9, false); // ignore enemies
            _enemy_to_toss.SetCollisionLayerValue(1, false); // ignore player
            _enemy_to_toss.SetCollisionMaskValue(9, false); // ignore enemies
            _enemy_to_toss.SetCollisionMaskValue(1, false); // ignore player
            _enemy_to_toss.SetCollisionMaskValue(2, false); // ignore ???
            _enemy_to_toss.SetCollisionMaskValue(3, false); // ignore ???
            _enemy_to_toss.SetCollisionMaskValue(4, false); // ignore ???

            AudioManager.TryPlay(PickupSound, AudioBus.Enemies, enemy.GlobalPosition);
            AttackState = 1;

            // backup timer in case the enemy is not picked up properly, this forces the state switch
            var t = new Timer()
            {
                OneShot = true,
                WaitTime = 3.0f
            };
            t.Timeout += () =>
            {
                t.QueueFree();
                // if state has already changed, don't do anything
                if (AttackState != 1) return;

                AttackState = 2;
                AudioManager.TryPlay(StormSqueal, position: _enemy_to_toss.GlobalPosition);
                var t2 = new Timer()
                {
                    OneShot = true,
                    WaitTime = 1f
                };
                t2.Timeout += () =>
                {
                    if (enemy == null || !IsInstanceValid(enemy))
                    {
                        GD.Print("Enemy is null or invalid. Cancelling storm toss attack.");
                        ResetParams();
                        IsFinished = true;
                        return;
                    }
                    if (_enemy_to_toss == null || !IsInstanceValid(_enemy_to_toss))
                    {
                        GD.Print("Enemy to toss is null or invalid. Cancelling storm toss attack.");
                        Finish(enemy);
                        return;
                    }
                    _enemy_to_toss.AnimStateMachine.Travel("thrown", true);
                    AudioManager.TryPlay(TossSound, AudioBus.Enemies, _enemy_to_toss.GlobalPosition);
                    AttackState = 3;
                    t2.QueueFree();
                };
                enemy.AddChild(t2);
                t2.Start();
            };
            enemy.AddChild(t);
            t.Start();
        }
        else if (_enemy_to_toss == null || !IsInstanceValid(_enemy_to_toss))
        {
            GD.Print("Enemy to toss is null or invalid. Cancelling storm toss attack.");
            Finish(enemy);
            return;
        }

        if (AttackState == 1) // hold above head and wait 
        {
            // TODO have enemy go into a "hold" animation
            _enemy_to_toss.GlobalPosition = _enemy_to_toss.GlobalPosition.Lerp(_target_node_when_holding.GlobalPosition, 0.1f);
            if (_enemy_to_toss.GlobalPosition.DistanceSquaredTo(_target_node_when_holding.GlobalPosition) <= 0.1f)
            {
                AttackState = 2;
                AudioManager.TryPlay(StormSqueal, position: _enemy_to_toss.GlobalPosition);
                var t = new Timer()
                {
                    OneShot = true,
                    WaitTime = 1f
                };
                t.Timeout += () =>
                {
                    if (enemy == null || !IsInstanceValid(enemy))
                    {
                        GD.Print("Enemy is null or invalid. Cancelling storm toss attack.");
                        ResetParams();
                        IsFinished = true;
                        return;
                    }
                    if (_enemy_to_toss == null || !IsInstanceValid(_enemy_to_toss))
                    {
                        GD.Print("Enemy to toss is null or invalid. Cancelling storm toss attack.");
                        Finish(enemy);
                        return;
                    }
                    _enemy_to_toss.AnimStateMachine.Travel("thrown", true);
                    AudioManager.TryPlay(TossSound, AudioBus.Enemies, _enemy_to_toss.GlobalPosition);
                    AttackState = 3;
                    t.QueueFree();
                };
                enemy.AddChild(t);
                t.Start();
            }
        }

        if (AttackState == 2) // hold over head
        {
            _enemy_to_toss.GlobalPosition = _enemy_to_toss.GlobalPosition.Lerp(_target_node_when_holding.GlobalPosition, 0.1f);
        }

        if (AttackState == 3) // toss
        {
            _enemy_to_toss.SetCollisionLayerValue(9, true); // ignore enemies
            _enemy_to_toss.SetCollisionMaskValue(9, true); // ignore enemies
            _enemy_to_toss.SetCollisionMaskValue(1, true); // ignore player
            _enemy_to_toss.SetCollisionMaskValue(2, true); // ignore ???
            _enemy_to_toss.SetCollisionMaskValue(3, true); // ignore ???
            _enemy_to_toss.SetCollisionMaskValue(4, true); // ignore ???

            _enemy_to_toss.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            _enemy_to_toss.Freeze = false;
            // TODO
            // have enemy go into a "toss" animation
            var direction = _enemy_to_toss.GlobalPosition.DirectionTo(Player.Instance.GlobalPosition + Player.Instance.Velocity * 0.1f);
            _enemy_to_toss.GravityScale = 0f;
            var phys_mat = _enemy_to_toss.PhysicsMaterialOverride;
            _enemy_to_toss.PhysicsMaterialOverride = new PhysicsMaterial()
            {
                Bounce = 0.5f,
                Friction = 0.0f,
                Rough = false,
            };


            _enemy_to_toss.ApplyCentralImpulse(direction * 50f);
            var t = new Timer()
            {
                OneShot = true,
                WaitTime = 1f
            };
            var en = _enemy_to_toss;
            t.Timeout += () =>
            {
                Finish(enemy);
                t.QueueFree();
            };
            enemy.AddChild(t);
            t.Start();
            AttackState = -1;
        }
    }

    public void Finish(Enemy enemy)
    {
        ResetEnemyToToss();
    
        if (enemy == null || !IsInstanceValid(enemy)) return;
        _cooldown.Stop();
        _cooldown.WaitTime = _cooldown_range.X + Random.Shared.NextSingle()*(_cooldown_range.Y-_cooldown_range.X);
        _cooldown.Start();
        IsFinished = true;
        enemy.AnimStateMachine.Travel("base_idle", true);
    }

    private void ResetEnemyToToss()
    {
        if (_enemy_to_toss != null && IsInstanceValid(_enemy_to_toss))
        {
            _enemy_to_toss.PhysicsMaterialOverride = Enemy.DefaultPhysicsMaterial;
            _enemy_to_toss.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            _enemy_to_toss.Freeze = false;
            _enemy_to_toss.GravityScale = 1f;
            _enemy_to_toss.State = Enemy.StateEnum.Idle;
            _enemy_to_toss.AnimStateMachine.Travel("base_idle", true);
        }
        _enemy_to_toss = null;
    }

    public void ResetParams()
    {
        ResetEnemyToToss();
        IsFinished = false;
        AttackState = 0;
    }
}
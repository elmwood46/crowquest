using Godot;
using System;
using System.Linq.Expressions;

public partial class SpiralBulletAttack : Node3D, IAttack
{
    public static readonly string AttackName = "SpiralBulletAttack";
    public bool CanBeInterrupted => true;
    public bool CanMoveDuring => true;
    public bool IsFinished {get;set;} = false;
    private static readonly PackedScene DefaultBullet = ResourceLoader.Load("res://enemies/attack_patterns/default_bullet.tscn") as PackedScene; 
    private static readonly AudioStream BulletFireSound = ResourceLoader.Load("res://audio/attacks/bubble-pop-283674.mp3") as AudioStream;
    private Timer _cooldown = new() {WaitTime = 5.0, OneShot = true};
    private Vector2 _cooldown_range = new(4.0f,5.0f); // seconds
    private int _attack_state = 0;
    private int _bullet_count = 50;
    private float _attack_time = 1.0f;
    private Timer _bullet_spawn_timer;
    private int _bullets_spawned = 0;
    private float _spiral_rotations = 3.0f;

    private void AddSwitchStateTimer(Enemy enemy, int next_state, float wait_time, Callable callback = new Callable())
    {
        var t = new Timer()
        {
            WaitTime = wait_time,
            OneShot = true
        };
        t.Timeout += () =>
        {
            _attack_state = next_state;
            callback.Call();
            t.QueueFree();
        };
        enemy.AddChild(t);
        t.Start();
    }

    public bool CanTrigger(Enemy enemy)
    {
        if (!_cooldown.IsInsideTree())
        {
            enemy.AddChild(_cooldown);
        }
        return enemy.IsPlayerInRange(16.0f) && _cooldown.IsStopped();
    }

    // this state machine is so unnecessarily messy lmao but it works
    public void Execute(Enemy enemy)
    {
        if (IsFinished) return;
        if (_attack_state == 0)
        {
            enemy.ForceZeroVelocity();
            
            // called when state switches
            var state_switch_callback = Callable.From(() =>
            {
                _bullet_spawn_timer = new Timer()
                {
                    WaitTime = _attack_time / _bullet_count
                };
                _bullet_spawn_timer.Timeout += () =>
                {
                    if (_bullets_spawned >= _bullet_count) return;
                    //var bullet = DefaultBullet.Instantiate<Bullet>();
                    //enemy.GetTree().Root.AddChild(bullet);
                    //bullet.GlobalPosition = enemy.GlobalPosition;
                    // bullet.LookAt(bullet.GlobalPosition+enemy.GetYDirectionToPlayer(), Vector3.Up);
                    // bullet.RotateY(MathF.PI*2 *_spiral_rotations * _bullets_spawned/_bullet_count);
                    //bullet.BulletDirection = enemy.GetYDirectionToPlayer().Rotated(Vector3.Up,MathF.PI*_bullets_spawned/16);
                    var bullet_dir = enemy.GetYDirectionToPlayer().Rotated(Vector3.Up,MathF.PI*_bullets_spawned/16);
                    BulletManager.AddBullet(enemy, 10, DamageType.Physical, bullet_dir, 10.0f);
                    if (Mathf.FloorToInt(_bullets_spawned/(float)_bullet_count)*100%10 == 0)
                    {
                        AudioManager.TryPlay(BulletFireSound, AudioBus.Misc, enemy.GlobalPosition);
                    } 
                    _bullets_spawned++;
                };
                enemy.AddChild(_bullet_spawn_timer);
                _bullet_spawn_timer.Start();
            });
            AddSwitchStateTimer(enemy, 1, 0.5f, state_switch_callback);
            _attack_state = -1;
        }
        else if (_attack_state == 1)
        {
            if (_bullets_spawned >= _bullet_count) 
            {
                Finish(enemy);
            }
        }
    }

    public void Finish(Enemy enemy)
    {
        _cooldown.Stop();
        _cooldown.WaitTime = _cooldown_range.X + Random.Shared.NextSingle()*(_cooldown_range.Y-_cooldown_range.X);
        _cooldown.Start();
        IsFinished = true;
        enemy.AnimStateMachine.Travel("base_idle", true);
    }

    public void ResetParams()
    {
        if (IsInstanceValid(_bullet_spawn_timer)) _bullet_spawn_timer.QueueFree();
        IsFinished = false;
        _attack_state = 0;
        _bullets_spawned = 0;
    }
}
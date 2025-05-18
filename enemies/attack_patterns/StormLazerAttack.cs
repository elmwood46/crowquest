using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class StormLazerAttack : Node3D, IAttack
{
    public int BaseDamage { get; set; } = 10;
    public int AttackState { get; private set; } = 0;
    public bool CanBeInterrupted => true;
    public bool CanMoveDuring => false;
    public bool IsFinished {get;set;} = false;
    private Timer _cooldown = new() {WaitTime = 3.0, OneShot = true};
    private Vector2 _cooldown_range = new(5f,5f); // seconds
    private Node3D _targeting_line;
    private Node3D _targeting_box;
    private static double _target_timer_duration = 5.0d;
    private Timer _target_timer = new() { WaitTime = _target_timer_duration, OneShot = true };
    private double _aim_sound_duration;
    private double _aim_sound_stretch;
    private string _tracked_sound_id = "storm_lazer_attack_";
    private bool _stopped_early = false;
    private static readonly List<string> _tracked_sound_ids = [];

    private static readonly AudioStream AimSound = ResourceLoader.Load("res://enemies/enemy_instances/storm/target-beep.ogg") as AudioStream;
    private static readonly AudioStream ShootSound = ResourceLoader.Load("res://enemies/enemy_instances/storm/scifi-laser-gun-shot-2-341617.mp3") as AudioStream;

    public bool CanTrigger(Enemy enemy)
    {
        if (!_cooldown.IsInsideTree())
        {
            enemy.AddChild(_cooldown);
            enemy.AddChild(_target_timer);
            _tracked_sound_id += enemy.GetHashCode();
            _aim_sound_duration = AimSound.GetLength();
            _aim_sound_stretch = _aim_sound_duration / _target_timer_duration;

            _target_timer.Timeout += () =>
            {
                if (IsFinished) return;
                Player.Instance.AddCameraShake(0.5f);
                _targeting_line.Visible = false;
                _targeting_box.Visible = false;
                AudioManager.TryPlay(ShootSound, AudioBus.Enemies, enemy.GlobalPosition);
                Player.Instance?.TakeDamage(BaseDamage, DamageTypeFlagEnum.Electric);
                Finish(enemy);
            };
        }
        _targeting_line ??= (enemy.GetNodeOrNull<Node3D>("TargetingLine") ?? throw new Exception("Running StormLazerAttack, TargetingLine node not found"));
        _targeting_box ??= (enemy.GetNodeOrNull<Node3D>("TargetingBox") ?? throw new Exception("Running StormLazerAttack, TargetingBox node not found"));
        _targeting_box.GlobalPosition = Player.Instance.GlobalPosition;

        return _cooldown.IsStopped() && enemy.IsPlayerInRange(64.0f) && IsPlayerInHitscan(enemy);
    }

    private static bool IsPlayerInHitscan(Enemy enemy)
    {
        var spaceState = enemy.GetWorld3D().DirectSpaceState;

        Vector3 origin = enemy.GlobalPosition + Vector3.Up * 0.728f, end = Player.Instance.GlobalPosition + Vector3.Up * 0.5f;
        var dirToPlayer = (end - origin).Normalized();
        var query = PhysicsRayQueryParameters3D.Create(origin, end + dirToPlayer * 2.0f);
        query.Exclude = [enemy.GetRid()];
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        query.CollisionMask = 1;

        var result = spaceState.IntersectRay(query);

        if (result.Count > 0)
        {
            var res_obj = result["collider"].AsGodotObject();
            return res_obj != null && res_obj is not StaticBody3D && res_obj is Player;
        }
        return false;
    }

    // this state machine is so unnecessarily messy lmao but it works
    public void Execute(Enemy enemy)
    {
        if (IsFinished) return;

        if (AttackState == 0)
        {
            _targeting_line.Visible = true;
            _targeting_box.Visible = true;
            _target_timer.Start(_target_timer_duration);
            _tracked_sound_id = "storm_lazer_attack_" + enemy.GetHashCode();

            AudioManager.TryPlayTrackedSound(_tracked_sound_id, AimSound, AudioBus.Enemies, enemy.GlobalPosition, volumedb:-100f, pitch_scale: (float)_aim_sound_stretch);

            if (_tracked_sound_ids.Contains(_tracked_sound_id))
            {
                var idx = _tracked_sound_ids.IndexOf(_tracked_sound_id);
                _tracked_sound_ids.RemoveAt(idx);
            }
            _tracked_sound_ids.Add(_tracked_sound_id);
            AudioManager.TryUpdateTrackedSoundVolume(_tracked_sound_ids[0], 10.0f);
            
            AttackState = 1;
        }

        if (AttackState == 1)
        {
            AudioManager.TryUpdateTrackedSoundPosition(_tracked_sound_id, enemy.GlobalPosition);
            var scalez = _targeting_line.GlobalPosition.DistanceTo(Player.Instance.GlobalPosition);
            _targeting_line.Scale = new Vector3(1f, 1f, scalez);
            _targeting_line.LookAt(Player.Instance.GlobalPosition+Vector3.Up*0.5f);
            _targeting_box.GlobalPosition = _targeting_box.GlobalPosition.Lerp(Player.Instance.GlobalPosition, 0.25f);

            // check for blocking LOS
            if (Engine.GetPhysicsFrames() % 2ul == 0) return; // basic optimization to not run every frame

            if (!IsPlayerInHitscan(enemy))
            {
                _stopped_early = true;
                GD.Print("LOS broken; exiting early");
                Finish(enemy);
            }
        }
    }

    public void Finish(Enemy enemy)
    {
        IsFinished = true;
        AudioManager.TryStopTrackedSound(_tracked_sound_id);
        if (_tracked_sound_ids.Contains(_tracked_sound_id))
        {
            var idx = _tracked_sound_ids.IndexOf(_tracked_sound_id);
            _tracked_sound_ids.RemoveAt(idx);
        }
        if (_tracked_sound_ids.Count > 0) AudioManager.TryUpdateTrackedSoundVolume(_tracked_sound_ids[0], 10.0f);

        GD.Print(AudioManager.TrackedPlayers.Count);
        _target_timer.Stop();
        _targeting_line.Visible = false;
        _targeting_box.Visible = false;
        _cooldown.Stop();
        _cooldown.WaitTime = _cooldown_range.X + Random.Shared.NextSingle() * (_cooldown_range.Y - _cooldown_range.X);
        if (_stopped_early) _cooldown.WaitTime *= 0.5d;
        _stopped_early = false;
        _cooldown.Start();
        enemy.AnimStateMachine.Travel("base_idle", true);
    }

    public void ResetParams()
    {
        _stopped_early = false;
        IsFinished = false;
        AttackState = 0;
    }
}
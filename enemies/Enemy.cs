using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum EnemyTag
{
    BasicMelee,
    Ranged,
    Flying,
}

public enum EnemyState
{
    Idle,
    Attacking,
    TakingDamage,
    Dead,
    Moving,
}

public enum DieFace{
    d4 = 4,
    d6 = 6,
    d8 = 8,
    d10 = 10,
    d12 = 12,
    d20 = 20,
}

public partial class Enemy : RigidBody3D, IHurtable
{
    [Export] public int MaxHealth { get; set; } = 30;
    [Export] public string TagsString { get; set; } = "";
    [Export] public Godot.Collections.Array<AudioStream> PainSounds { get; set; } = new();
    [Export] public Godot.Collections.Array<AudioStream> DeathSounds { get; set; } = new();
    [Export] public Godot.Collections.Array<AudioStream> IdleSounds { get; set; } = new();
    [Export] public Godot.Collections.Array<AudioStream> SeeSound { get; set; } = new();
    [Export] public AnimationTree AnimTree { get; set; }
    [Export] public AnimatedSprite3D Sprite { get; set; }
    [Export] public float Speed { get; set; } = 5.0f;
    [Export] public RayCast3D GroundRay {get;set;}
    [Export] public RayCast3D SlopeCheckRay {get;set;}
    [Export] public RayCast3D WallCheckRay {get;set;}
    [Export] public Node3D RayGimbal {get;set;}
    [Export] public CollisionShape3D CollisionShape {get;set;}
    [Export(PropertyHint.Range,"0.0,1.0,0.01")] public float PainChance {get;set;} = 0.5f;
    [Export] public float StunDuration {get;set;} = 1.0f;
    [Export] public DieFace CoinDropDice {get;set;} = DieFace.d6;
    [Export] public int CoinDropDiceAmount {get;set;} = 3;
    [Export] public Godot.Collections.Array<string> AttacksList {get;set;} = new Godot.Collections.Array<string>();
    private List<IAttack> _attackList = new();
    private IAttack _currentAttack = null;

    // can be used by Attack class objects to time various events during attacks
    public Timer AttackTimer = new(){WaitTime = 1.0f, Autostart = false, OneShot = true};

    private Timer _deathTimer = new(){WaitTime = 1.0f, Autostart = false, OneShot = true}; 
    private Timer _footstep_timer = new(){WaitTime = 0.5f, Autostart = false, OneShot = true};

    private Timer _idle_sound_timer = new(){WaitTime = Random.Shared.Next(_idle_sound_wait_range.X,_idle_sound_wait_range.Y), Autostart = false, OneShot = true};
    private static readonly Vector2I _idle_sound_wait_range = new(30, 180);

    private float _death_shake_factor = 0.05f; 
    private Vector3 _base_sprite_position;
    private Vector3 _base_sprite_scale;

    private static readonly Color RED = new(1.0f,0.0f,0.0f);
    private static readonly ShaderMaterial EnemyHitFlash = ResourceLoader.Load("res://enemies/enemy_hit_flash.tres") as ShaderMaterial;
    private static readonly PackedScene _death_blood_fountain = ResourceLoader.Load<PackedScene>("res://effects/enemy_die_fx/enemy_die.tscn"); 
    private static readonly PackedScene _death_smoke = ResourceLoader.Load<PackedScene>("res://effects/enemy_die_fx/enemy_death_smoke.tscn"); 
    private ShaderMaterial _sprite_shader;

    public AnimationNodeStateMachinePlayback AnimStateMachine;

   // [Export] public NavigationAgent3D NavAgent {get;set;}
    //[Export] public float Mass { get; set; } = 80.0f;
    public HashSet<EnemyTag> Tags = new();

    // index of the spawned enemy
    public int Index;
    public int Health;
    public EnemyState State = EnemyState.Idle;
    private Timer _random_walk_dir_timer = new(){WaitTime = 1, Autostart = false, OneShot = true};

    private Vector3 _prev_jump_pos = Vector3.Zero;

    private Vector2 _random_walk_dir = Vector2.Zero;

    private float _jump_velocity = 2.5f;

    private Timer _path_req_timer = new(){WaitTime = 1.0f, Autostart = false, OneShot = true};

    private Timer _stun_timer = new(){WaitTime = 1.0f, Autostart = false, OneShot = true};

    // used for random walk
    private double _phys_secs = 0;
    private Vector3 _prev_pos = Vector3.Zero;

    // monster is inactive
    public bool Deactivated {get;set;} = false;

    // monster in ambush mode
    public bool Ambush {get;set;} = false;

    public void TakeDamage(int damage, DamageType damageType)
    {
        Health -= damage;

        var _stun = damage > Health/2 ? 1.0 : PainChance;

        if (Health > 0)
        {
            if (_stun_timer.IsStopped() && Random.Shared.NextSingle() < _stun)
            {
                if (_currentAttack == null || (_currentAttack != null && _currentAttack.CanBeInterrupted))
                {
                    if (_currentAttack != null)
                    {
                        _currentAttack.Finish(this);
                        _currentAttack.ResetParams();
                        _currentAttack = null;
                    }
                    AnimStateMachine.Travel("base_idle");
                    StopIdleSoundTimer();
                    var stream = PainSounds[Random.Shared.Next(0,PainSounds.Count)];
                    AudioManager.TryPlay(stream, AudioBus.Enemies, GlobalPosition);
                    _stun_timer.WaitTime = StunDuration;
                    _stun_timer.Start();
                }
            }
        }
        else _stun_timer.Stop();
    }

    public void StopIdleSoundTimer()
    {
        _idle_sound_timer.Stop();
    }

    public override void _Ready()
    {
        if (CollisionShape == null) throw new Exception($"Enemy {this} must have a CollisionShape3D set in editor.");
        CollisionShape.CallDeferred(MethodName.Reparent, this);

        if (Sprite == null) throw new Exception($"Enemy {this} must have a Sprite3D set in editor.");
        _base_sprite_position = Sprite.Position;
        _base_sprite_scale = Sprite.Scale;

        AddChild(AttackTimer);
        AddChild(_deathTimer);
        AddChild(_idle_sound_timer);
        AddChild(_footstep_timer);

        _footstep_timer.Timeout += () =>
        {
            if (!Tags.Contains(EnemyTag.Flying) && LinearVelocity.LengthSquared() > 0.1f && IsOnFloor())
            {
                PlayFootstepSound();
            }
        };

        _idle_sound_timer.Timeout += () => {
            if (IdleSounds.Count > 0)
            {
                var sound = IdleSounds[Random.Shared.Next(0,IdleSounds.Count)];
                AudioManager.TryPlay(sound, AudioBus.Enemies, GlobalPosition);
            }
            _idle_sound_timer.WaitTime = Random.Shared.Next(_idle_sound_wait_range.X,_idle_sound_wait_range.Y);
            _idle_sound_timer.Start();
        };

        if (EnemyHitFlash == null) throw new Exception($"Enemy {this} must have a EnemyHitFlash.tres in res://enemies/enemy_hit_flash.tres");
        _sprite_shader = EnemyHitFlash.Duplicate() as ShaderMaterial;
        _sprite_shader.SetShaderParameter("intensity", 1.0f);
        _sprite_shader.SetShaderParameter("flash_enabled", false);
        Sprite.MaterialOverride = _sprite_shader;

        if (Tags.Contains(EnemyTag.Flying))
        {
            //MotionMode = MotionModeEnum.Floating;
            //GravityScale = 0.0f;
        }
        AddChild(_random_walk_dir_timer);
        AddChild(_stun_timer);
        Health = MaxHealth;
        ProcessTagString(TagsString);
        ProcessAttackList();

        if (AnimTree != null)
        {
            var list = AnimTree.GetAnimationList();
            if (!list.Contains("base_idle")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_idle' animation");
            if (!list.Contains("base_pain")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_pain' animation");
            if (!list.Contains("base_die")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_die' animation");
            if (!list.Contains("base_move")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_move' animation");
        }

        AnimStateMachine = (AnimationNodeStateMachinePlayback)AnimTree.Get("parameters/playback");
    }

    public void DeathAnimation()
    {
        if (AnimStateMachine.GetCurrentNode() == "base_die")
        {
            // shake sprite
            float shakex, shakey, shakez;
            shakex = _death_shake_factor*(Random.Shared.NextSingle()*2.0f-1.0f);
            shakey = _death_shake_factor*(Random.Shared.NextSingle()*2.0f-1.0f);
            shakez = _death_shake_factor*(Random.Shared.NextSingle()*2.0f-1.0f);
            Sprite.Position = _base_sprite_position + new Vector3(shakex,shakey,shakez);

            if ((bool)_sprite_shader.GetShaderParameter("pulse_mode") != false) _sprite_shader.SetShaderParameter("pulse_mode", false);
            if ((bool)_sprite_shader.GetShaderParameter("flash_enabled") != true) _sprite_shader.SetShaderParameter("flash_enabled", true);
            if ((float)_sprite_shader.GetShaderParameter("intensity") != 0.5f) _sprite_shader.SetShaderParameter("intensity", 0.5f);
            if ((Color)_sprite_shader.GetShaderParameter("flash_color") != RED) _sprite_shader.SetShaderParameter("flash_color", RED);
            
            if (_deathTimer.IsStopped())
            {
                var blood_fountain = _death_blood_fountain.Instantiate() as GpuParticles3D;
                blood_fountain.Emitting = true;
                blood_fountain.Finished += () => blood_fountain.QueueFree();
                var deathsmoke = _death_smoke.Instantiate() as GpuParticles3D;
                deathsmoke.Emitting = true;
                deathsmoke.Finished += () => deathsmoke.QueueFree();
                AddSibling(blood_fountain);
                AddSibling(deathsmoke);
                blood_fountain.SetGlobalPosition(GlobalPosition);
                deathsmoke.SetGlobalPosition(GlobalPosition+Vector3.Up*0.5f);

                var coinAmount = 0;
                //GD.Print("die faces: ", (int)CoinDropDice);
                for (int i = 0; i < CoinDropDiceAmount; i++)
                {
                    coinAmount += Mathf.FloorToInt(Random.Shared.NextSingle()* (int)CoinDropDice) + 1;
                }
                AddSibling(TreasureSpawner.Create(GlobalPosition+Vector3.Up*0.5f,coinAmount,0.5f));

                QueueFree();
            }
            else
            {
                Freeze = true;
                //Sprite.Scale = new Vector3(_base_sprite_scale.X,(1.0f-0.2f*AnimStateMachine.GetCurrentPlayPosition())*_base_sprite_scale.Y,_base_sprite_scale.Z);
            }
        }
        else
        {
            _idle_sound_timer.Stop();
            var stream = DeathSounds[Random.Shared.Next(0,DeathSounds.Count)];
            AudioManager.TryPlay(stream, AudioBus.Enemies, GlobalPosition);
            AnimStateMachine.Travel("base_die", true);
            _deathTimer.WaitTime = AnimTree.GetAnimation("base_die").Length;
            _deathTimer.Start();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Health <= 0) {
            DeathAnimation();
            return;
        }

        if (!IsInstanceValid(this)) return;
        if (!IsInsideTree()) return;
        if (Freeze) return;

        //EnemyComputeShaderManager.SetEnemyPosition(Index, GlobalTransform.Origin);
        if (IsOnFloor() || SlopeDetected() || Tags.Contains(EnemyTag.Flying)) GravityScale = 0.0f;
        else if (!Tags.Contains(EnemyTag.Flying)) GravityScale = 1.0f;

        // update hit flash shader
        // (it also renders the base sprite even when not flashing)
        if ((Texture2D)_sprite_shader.GetShaderParameter("tex") != Sprite.SpriteFrames.GetFrameTexture(Sprite.Animation, Sprite.Frame))
        {
            _sprite_shader.SetShaderParameter("tex", Sprite.SpriteFrames.GetFrameTexture(Sprite.Animation, Sprite.Frame));
        }

        // damage flash
        if (!_stun_timer.IsStopped())
        {
            if ((bool)_sprite_shader.GetShaderParameter("flash_enabled") != true) _sprite_shader.SetShaderParameter("flash_enabled", true);
            if ((bool)_sprite_shader.GetShaderParameter("pulse_mode") != true) _sprite_shader.SetShaderParameter("pulse_mode", true);
            return;
        }
        else
        {
            if ((bool)_sprite_shader.GetShaderParameter("flash_enabled") != false) _sprite_shader.SetShaderParameter("flash_enabled", false);
            if ((bool)_sprite_shader.GetShaderParameter("pulse_mode") != false) _sprite_shader.SetShaderParameter("pulse_mode", false);
        }

        // timer to play idle sounds
        if (_currentAttack == null && _idle_sound_timer.IsStopped())
        {
            _idle_sound_timer.WaitTime = Random.Shared.Next(_idle_sound_wait_range.X,_idle_sound_wait_range.Y);
            _idle_sound_timer.Start();
        }

        // do attack logic and move if player is not dead
        if (!Player.Instance.IsDead && !Deactivated && !Ambush) {
            if (_currentAttack == null)
            {
                foreach (var attack in _attackList)
                {
                    if (attack.CanTrigger(this))
                    {
                        _currentAttack = attack;
                        _currentAttack.Execute(this);
                        //GD.Print("Enemy is attacking ", _currentAttack);
                        break;
                    }
                }
            }
            else 
            {
                if (_currentAttack.IsFinished)
                {
                    _currentAttack.ResetParams(); //reset attack
                    _currentAttack = null;
                    //GD.Print("Enemy finished attack ", _currentAttack);
                }
                else
                {
                    _currentAttack.Execute(this);
                    //GD.Print("Enemy is executing attack ", _currentAttack);
                }
            }
        

            // skip movement if attacking and can't move during attack
            if (_currentAttack != null && !_currentAttack.CanMoveDuring && !_currentAttack.IsFinished) return;

            // check for triggering random walk 
            _phys_secs += delta;
            if (_phys_secs > 1)
            {
                _phys_secs -= 1;
                if (_prev_pos.DistanceSquaredTo(GlobalPosition) < 1.0f)
                {
                    _random_walk_dir_timer.Stop();
                    _random_walk_dir = new Vector2(Random.Shared.NextSingle() * 2 - 1, Random.Shared.NextSingle() * 2 - 1).Normalized();
                    _random_walk_dir_timer.WaitTime = Random.Shared.NextDouble() + 0.001; // 1 seconds random walk
                    _random_walk_dir_timer.Start();
                }
                _prev_pos = GlobalPosition;
                //GD.Print($"Enemy {this} is on floor: {IsOnFloor()}");
            }
            /*
            CallDeferred(MethodName.SetNavAgentTarget, new Vector3(60,60,5)); //Player.Instance.GlobalPosition);
            
            //var vel = GetHorzDir((float)delta);
            GD.Print("targ postition: ", NavAgent.TargetPosition);
            GD.Print("distance to target:, ", NavAgent.DistanceToTarget());
            GD.Print("next path postition: ", NavAgent.GetNextPathPosition());
            var vel = NavAgent.GetNextPathPosition() - GlobalPosition;
            }*/

            var vel = GetHorzDir((float)delta);
            if (vel.LengthSquared() > 0.01f) RayGimbal.LookAt(RayGimbal.GlobalTransform.Origin + vel, Vector3.Up);
            if ((this != null)  && IsInstanceValid(this) && IsInsideTree()) ApplyCentralForce(vel.Normalized()*Speed*Mass);

            // Check for collision
            var col = WallCheckRay.GetCollider();
            if (IsOnFloor() && col != null && (col is StaticBody3D || (col is RigidBody3D r && r.Freeze)))
            {
                // Apply a slight upward force to help climb slopes
                //ApplyCentralImpulse(Vector3.Up * 5.0f);
                Vector3 normal = WallCheckRay.GetCollisionNormal();
                
                // Only jump or change dir if the collision is with a wall (not a floor/slope)
                if (normal.Dot(Vector3.Up) < 0.001f) // Mostly vertical surface
                {
                    if (!Tags.Contains(EnemyTag.Flying))// && IsOnFloor())
                    {
                        //GD.Print("Collided! Jumping...");
                        if ((this != null) && IsInstanceValid(this) && IsInsideTree()) ApplyCentralImpulse(Vector3.Up * _jump_velocity * Mass);
                        //LinearVelocity = new Vector3(LinearVelocity.X,_jump_velocity,LinearVelocity.Z); // Apply jump force
                    }
                }
            }
            //PushAwayRigidBodies();
            //MoveAndSlide();

            // play foostep sounds
            // reset footstep timer, which plays footstep sound when it times out
			if (!Tags.Contains(EnemyTag.Flying) && _footstep_timer.IsStopped())
			{
				if (LinearVelocity.LengthSquared() < 16)
				{
					_footstep_timer.WaitTime = AudioManager.FootstepWaitTimes.Z;
				}
				else if (LinearVelocity.LengthSquared() >= 64)
				{
					_footstep_timer.WaitTime = AudioManager.FootstepWaitTimes.Y;
				}
				else
				{
					_footstep_timer.WaitTime = AudioManager.FootstepWaitTimes.X;
				}
				
				_footstep_timer.Start();
			}
        }
    }

    /*
    bool first_set = true;
    private void SetNavAgentTarget(Vector3 target)
    {
        if (first_set)
        {
            NavAgent.TargetPosition = target;
            first_set = false;
        }
    }*/

    public void SetStun(float duration)
    {
        _stun_timer.Stop();
        _stun_timer.WaitTime = duration;
        _stun_timer.Start();
    }

    public bool IsOnFloor()
    {
        var col = GroundRay.GetCollider();
        return col != null && col is ChunkPlane;
    }

    public bool SlopeDetected()
    {
        var col = SlopeCheckRay.GetCollider();
        return col != null && col is ChunkPlane;
    }

    public bool WallDetected()
    {
        var col = WallCheckRay.GetCollider();
        return col != null && col is StaticBody3D || (col is RigidBody3D r && r.Freeze);
    }

    private Vector3 GetHorzDir(float delta) {
        var player_pos = Player.Instance.GlobalPosition;
        var _velocity = LinearVelocity;

        if (Tags.Contains(EnemyTag.Flying)) {
            // move straight towards player
            _velocity = (player_pos - GlobalPosition).Normalized() * Speed;
            if (!_random_walk_dir_timer.IsStopped())
            {
                _velocity.X = _random_walk_dir.X * Speed;
                _velocity.Z = _random_walk_dir.Y * Speed;
            }
        }
        else
        {
            var dirxy = new Vector2(GlobalPosition.X, GlobalPosition.Z);
            if (_random_walk_dir_timer.IsStopped())
            {
                // move towards player unless walking randomly
                var playerxz = new Vector2(player_pos.X, player_pos.Z);
                dirxy = (playerxz - dirxy).Normalized();
            }
            else dirxy = _random_walk_dir;
            
            // Basic forward movement
            _velocity.X = dirxy.X * Speed;
            _velocity.Z = dirxy.Y * Speed;
        }

        return new Vector3(_velocity.X,0,_velocity.Z);
    }

    private void ProcessTagString(string tag_string)
    {
        Tags = new HashSet<EnemyTag>();
        foreach (var tag in tag_string.Split(","))
        {
            if (Enum.TryParse(tag, out EnemyTag tagEnum))
            {
                Tags.Add(tagEnum);
            }
        }
    }

    private void ProcessAttackList()
    {
        foreach (var str in AttacksList)
        {
            if (str == "Scratch")
            {
                //GD.Print("added scratch attack to attack list");
                _attackList.Add(new ScratchAttack());
            }
        }
    }

    public bool IsPlayerInRange(float range)
    {
        return Player.Instance.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= range*range;
    }

    public Vector3 GetYDirectionToPlayer()
    {
        return new Vector3(GlobalPosition.X, 0.0f, GlobalPosition.Z).DirectionTo(new Vector3(Player.Instance.GlobalPosition.X, 0.0f, Player.Instance.GlobalPosition.Z));
    }

    public void ResetAttackTimer()
    {
        AttackTimer.Stop();
        AttackTimer.WaitTime = 1.0f;
        AttackTimer.OneShot = true;
        AttackTimer.Autostart = false;
    }

    private void PlayFootstepSound()
    {
        GroundRay.ForceRaycastUpdate();
        var floorBelow = GroundRay.IsColliding();
        var volume_db = 0.0f;
        if (floorBelow && LinearVelocity.LengthSquared() > 0.2f)
        {
            var collider = GroundRay.GetCollider();
            var _footstep_sound = AudioManager.FootstepSounds["default"][Random.Shared.Next(0, AudioManager.FootstepSounds["default"].Count)];

            // chunkplane means grass sound
            if (collider is ChunkPlane)
            {
                volume_db = -10.0f; // set to different values for different sounds (grass sounds are too loud)
                if (Random.Shared.NextSingle() < 0.5f)
                    _footstep_sound = AudioManager.FootstepSounds["grass"][Random.Shared.Next(0, AudioManager.FootstepSounds["grass"].Count)];
                else _footstep_sound = AudioManager.FootstepSounds["grass_2"][Random.Shared.Next(0, AudioManager.FootstepSounds["grass_2"].Count)];
            }
            AudioManager.TryPlay(_footstep_sound, AudioBus.Footsteps, GlobalPosition, volume_db);
        }
    }
}
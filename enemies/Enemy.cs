using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Enemy : RigidBody3D, IHurtable
{
    // ==================================================================
    // ====== Enemy Stats ========
    // ==================================================================
    [ExportCategory("Enemy Stats")] 
    public StateEnum State { get; set; } = StateEnum.Idle;
    [Export] public int MaxHealth { get; set; } = 30;
    public int Health;
    public bool IsDead() => Health <= 0 || State == StateEnum.Dead;
    [Export] public float Speed { get; set; } = 5.0f;
    [Export(PropertyHint.Range,"0.0,1.0,0.01")] public float PainChance {get;set;} = 0.5f;
    [Export] public Godot.Collections.Array<TagEnum> Tags { get; set; } = [];
    [Export] public Godot.Collections.Array<AttackManager.AttackName> Attacks {get;set;} = [];
    private readonly List<IAttack> _attackList = [];
    private IAttack _currentAttack = null;
    private bool CurrentAttackBlocksMovement() => _currentAttack != null && !_currentAttack.CanMoveDuring && !_currentAttack.IsFinished;
    [Export] public DamageTypeFlagEnum TouchDamageType {get;set;} = DamageTypeFlagEnum.Physical;
    [Export] public int TouchDamageAmount {get;set;} = 1;
    [Export] public float TouchDamageRadius = 1.0f;
    private Timer _touch_damage_cooldown = new(){Autostart = true, WaitTime = 0.05};
    [Export] public float StunDuration {get;set;} = 1.0f;
    [Export] public AttackManager.DieFaceEnum CoinDropDice {get;set;} = AttackManager.DieFaceEnum.d6;
    [Export (PropertyHint.Range, "1,100,1,or_greater")] public int CoinDropDiceAmount {get;set;} = 3;
    [Export] public float MaxViewRange {get;set;} = 48.0f;
    private const int _num_rays = 3; // number of rays to check for player visibility

    // ==================================================================
    // ====== Audio ========
    // ==================================================================
    [ExportCategory("Audio")] 
    [Export] public Godot.Collections.Array<AudioStream> PainSounds { get; set; } = [];
    [Export] public Godot.Collections.Array<AudioStream> DeathSounds { get; set; } = [];
    [Export] public Godot.Collections.Array<AudioStream> IdleSounds { get; set; } = [];
    [Export] public Godot.Collections.Array<AudioStream> SeeSound { get; set; } = [];
    private Timer _footstep_timer = new(){WaitTime = 0.5f, Autostart = false, OneShot = true};
    private Timer _idle_sound_timer = new(){WaitTime = Random.Shared.Next(_idle_sound_wait_range.X,_idle_sound_wait_range.Y), Autostart = false, OneShot = true};
    public void StopIdleSoundTimer() =>  _idle_sound_timer.Stop();   
    private static readonly Vector2I _idle_sound_wait_range = new(30, 180);

    // ==================================================================
    // ====== Exported Nodes ========
    // ==================================================================
    [ExportCategory("Editor Nodes")]
    [Export] public EnemyMoveAgent MovementAgent { get; set; }
    [Export] public AnimationTree AnimTree { get; set; }
    [Export] public AnimatedSprite3D Sprite { get; set; }
    [Export] public RayCast3D GroundRay {get;set;}
    [Export] public CollisionShape3D CollisionShape {get;set;}

    // ==================================================================
    // ====== Navigation ========
    // ==================================================================
    private bool _flag_force_zero_velocity = false; // when set to true, forces the enemy velocity to 0
    public void ForceZeroVelocity() => _flag_force_zero_velocity = true;
    public static readonly PhysicsMaterial DefaultPhysicsMaterial = GD.Load("res://enemies/enemy_physics_material.tres") as PhysicsMaterial;

    // ==================================================================
    // ====== Animation and Effects ========
    // ==================================================================
    public AnimationNodeStateMachinePlayback AnimStateMachine;
    private float _death_shake_factor = 0.05f; 
    private Timer _deathTimer = new(){WaitTime = 1.0f, Autostart = false, OneShot = true}; 
    private Vector3 _base_sprite_position;
    private Vector3 _base_sprite_scale;
    private static readonly Color RED = new(1.0f,0.0f,0.0f);
    private static readonly ShaderMaterial EnemyHitFlash = ResourceLoader.Load("res://enemies/enemy_hit_flash.tres") as ShaderMaterial;
    private static readonly PackedScene _death_blood_fountain = ResourceLoader.Load<PackedScene>("res://effects/enemy_die_fx/enemy_die.tscn"); 
    private static readonly PackedScene _death_smoke = ResourceLoader.Load<PackedScene>("res://effects/enemy_die_fx/enemy_death_smoke.tscn"); 
    private ShaderMaterial _sprite_shader;
    private Timer _stun_timer = new(){WaitTime = 1.0f, OneShot = true};
    public bool IsStunned() { return !_stun_timer.IsStopped(); }

    // ==================================================================
    // ====== State ========
    // ==================================================================
    public enum TagEnum
    {
        Flying,
        CanBePickedUp
    }

    public enum StateEnum
    {
        Idle,
        Attacking,
        TakingDamage,
        Dead,
        Moving,
        IsPickedUp
    }

    // ==================================================================

    public override void _Ready()
    {
        if (CollisionShape == null) throw new Exception($"Enemy {this} must have a CollisionShape3D set in editor.");
        CollisionShape.CallDeferred(MethodName.Reparent, this);

        if (Sprite == null) throw new Exception($"Enemy {this} must have a Sprite3D set in editor.");
        _base_sprite_position = Sprite.Position;
        _base_sprite_scale = Sprite.Scale;

        // all enemies damage player upon touch
        ContactMonitor = true;
        MaxContactsReported = 1;
        BodyEntered += (body) =>
        {
            // TODO thrown enemies should get stars over head while stunned
            if (Tags.Contains(TagEnum.CanBePickedUp) && State == StateEnum.IsPickedUp && body is not Enemy)
            {
                //GD.Print("Enemy touched player while picked up");
                if (body is Player p)
                {
                    var atk = _attackList.Where(x => x is StormTossAttack).FirstOrDefault() ?? throw new Exception($"Enemy {this} must have a StormTossAttack in its attack list to be picked up?? This error should never happen");
                    if (p.HasRollIFrames()) p.ForceStopRolling(); // player roll does not block toss attack
                    p.TakeDamage(atk.BaseDamage, TouchDamageType);
                    p.AddCameraShake(0.3f);
                    GD.Print($"DID HIGH DAMAGE TO PLAYER {p} while picked up");
                    // TODO  do some player stunning thing here when thrown enemy hits them
                    // p.Velocity 
                }
                GD.Print($"setting enemy {Name}{this} to idle");
                State = StateEnum.Idle;
                SetStun(2.0f);
                AnimStateMachine.Travel("base_idle", true);
                AudioManager.TryPlay(AttackManager.TossEnemyImpactSound, AudioBus.Enemies, GlobalPosition);
            }
            else
            {
                if (body is Player player)
                {
                    player.TakeDamage(TouchDamageAmount, TouchDamageType);
                }
            }
        };

        AddChild(_deathTimer);
        AddChild(_idle_sound_timer);
        AddChild(_footstep_timer);
        AddChild(_touch_damage_cooldown);

        _footstep_timer.Timeout += () =>
        {
            if (!Tags.Contains(TagEnum.Flying) && LinearVelocity.LengthSquared() > 0.1f && IsOnFloor())
            {
                PlayFootstepSound();
            }
        };

        _idle_sound_timer.Timeout += () => {
            if (IdleSounds.Count > 0 && !IsDead() && !Freeze && Visible)
            {
                var sound = IdleSounds[Random.Shared.Next(0,IdleSounds.Count)];
                AudioManager.TryPlay(sound, AudioBus.Enemies, GlobalPosition);
            }
            _idle_sound_timer.WaitTime = Random.Shared.Next(_idle_sound_wait_range.X,_idle_sound_wait_range.Y);
            _idle_sound_timer.Start();
        };
        _touch_damage_cooldown.Timeout += () =>
        {
            if (!IsDead() && !Freeze && Visible && IsPlayerInRange(TouchDamageRadius))
            {
                Player.Instance.TakeDamage(TouchDamageAmount, TouchDamageType);
            }
        };

        if (EnemyHitFlash == null) throw new Exception($"Enemy {this} must have a EnemyHitFlash.tres in res://enemies/enemy_hit_flash.tres");
        _sprite_shader = EnemyHitFlash.Duplicate() as ShaderMaterial;
        _sprite_shader.SetShaderParameter("intensity", 1.0f);
        _sprite_shader.SetShaderParameter("flash_enabled", false);
        Sprite.MaterialOverride = _sprite_shader;

        // if (Tags.Contains(TagEnum.Flying))
        // {
        //     MotionMode = MotionModeEnum.Floating;
        //     GravityScale = 0.0f;
        // }

        AddChild(_stun_timer);
        Health = MaxHealth;

        // add attack instances to attack list
        foreach (var atk in Attacks)
        {
            if (AttackManager.AllAttackNamesAndTypes.TryGetValue(atk, out var type))
            {
                _attackList.Add((IAttack)Activator.CreateInstance(type));
            }
        }

        if (AnimTree != null)
        {
            var list = AnimTree.GetAnimationList();
            if (!list.Contains("base_idle")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_idle' animation");
            if (!list.Contains("base_pain")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_pain' animation");
            if (!list.Contains("base_die")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_die' animation");
            if (!list.Contains("base_move")) throw new Exception($"Enemy {this} AnimationPlayer must have a 'base_move' animation");

            if (Tags.Contains(TagEnum.CanBePickedUp))
            {
                if (!list.Contains("picked_up")) throw new Exception($"Enemy {this} is throwable; AnimationPlayer must have a 'picked_up' animation");
                if (!list.Contains("thrown")) throw new Exception($"Enemy {this} is throwable; AnimationPlayer must have a 'thrown' animation");
            }
        }
        else throw new Exception($"Enemy {this} must have an AnimationTree set in editor.");

        AnimStateMachine = (AnimationNodeStateMachinePlayback)AnimTree.Get("parameters/playback");
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        if (GlobalPosition.Y < -100f)
        {
            var min_dist = float.MaxValue;
            var chunks = ChunkManager.Instance.GetChunks();
            ChunkPlane chunk_to_teleport = null;
            foreach (var chunk in chunks)
            {
                var dist = chunk.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                if (dist < min_dist)
                {
                    min_dist = dist;
                    chunk_to_teleport = chunk;
                }
            }

            if (chunk_to_teleport != null)
            {
                LinearVelocity = Vector3.Zero;
                GlobalPosition = chunk_to_teleport.GlobalPosition + Vector3.Up * 5f;
            } 
        }

        if (IsDead())
        {
            state.LinearVelocity = Vector3.Zero;
            return;
        }

        if (_flag_force_zero_velocity)
        {
            state.LinearVelocity = Vector3.Zero;
            state.AngularVelocity = Vector3.Zero;
            _flag_force_zero_velocity = false;
        }
        else if (!IsDead()
            && !IsStunned()
            && !CurrentAttackBlocksMovement()
            && IsOnFloor())
        {
            // keep velocity below max speed when moving on floor   
            if (MovementAgent.State == EnemyMoveAgent.MoveState.PATH_TO_PLAYER)
            {
                state.LinearVelocity = state.LinearVelocity.Lerp(state.LinearVelocity.Normalized() * Speed * 2f, 0.45f);
            }
            else if (state.LinearVelocity.LengthSquared() > Speed * Speed)
            {
                state.LinearVelocity = state.LinearVelocity.Lerp(state.LinearVelocity.Normalized() * Speed, 0.2f);
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDead())
            {
                if (!_stun_timer.IsStopped()) _stun_timer.Stop();
                if (State == StateEnum.IsPickedUp)
                {
                    State = StateEnum.Dead;
                    AudioManager.TryPlay(AttackManager.TossEnemyImpactSound, AudioBus.Enemies, GlobalPosition);
                }
                StopAttacking();
                DeathAnimation();
                return;
            }

        if (!IsInstanceValid(this) || !IsInsideTree() || Freeze) return;

        if (Tags.Contains(TagEnum.Flying)) GravityScale = 0.0f;
        // if (IsOnFloor() || Tags.Contains(TagEnum.Flying)) GravityScale = 0.0f;
        // else if (!Tags.Contains(TagEnum.Flying)) GravityScale = 1.0f;

        UpdateHitFlashShader();

        if (State == StateEnum.IsPickedUp)
        {
            if (IsOnFloor())
            {
                State = StateEnum.Idle;
                SetStun(2f);
                AnimStateMachine.Travel("base_idle", true);
                AudioManager.TryPlay(AttackManager.TossEnemyImpactSound, AudioBus.Enemies, GlobalPosition);
            }
            return;
        }

        // timer to play idle sounds
        if (_currentAttack == null && _idle_sound_timer.IsStopped())
        {
            _idle_sound_timer.WaitTime = Random.Shared.Next(_idle_sound_wait_range.X, _idle_sound_wait_range.Y);
            _idle_sound_timer.Start();
        }

        // do attack logic and move if player is not dead and we are not picked up
        if (!Player.Instance.IsDead)
        {
            DoAttackLogic();

            // skip movement if attacking and can't move during attack
            if (CurrentAttackBlocksMovement() || IsStunned()) return;

            MovementAgent.GetMovementVector(delta, out var vel_dir, out var speed_mult);

            if ((this != null)  && IsInstanceValid(this) && IsInsideTree()) 
            {
                ApplyCentralForce(vel_dir*speed_mult*Mass);
            }

            SetFootstepTimer();
        }
    }

    private void UpdateHitFlashShader()
    {
        // update hit flash shader
        // (it also renders the base sprite even when not flashing)
        if ((Texture2D)_sprite_shader.GetShaderParameter("tex") != Sprite.SpriteFrames.GetFrameTexture(Sprite.Animation, Sprite.Frame))
        {
            _sprite_shader.SetShaderParameter("tex", Sprite.SpriteFrames.GetFrameTexture(Sprite.Animation, Sprite.Frame));
        }

        // damage flash
        if (IsStunned())
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
    }

    public IAttack GetCurrentAttack()
    {
        return _currentAttack;
    }

    public void StopAttacking()
    {
        if (_currentAttack != null)
        {
            _currentAttack.Finish(this);
            _currentAttack.ResetParams();
            _currentAttack = null;
        }
    }

    private void DoAttackLogic()
    {
        if (State == StateEnum.IsPickedUp)
        {
                StopAttacking();
                return;
        }

        if (_currentAttack == null)
        {
            // randomly choose an attack from the list
            var viable = new List<IAttack>();
            foreach (var attack in _attackList)
            {
                if (attack.CanTrigger(this)) viable.Add(attack);
            }
            if (viable.Count == 0) return;
            _currentAttack = viable[Random.Shared.Next(0, viable.Count)];
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
    }

    public void TakeDamage(int damage, DamageTypeFlagEnum damageType)
    {
        Health -= damage;

        var _stun = damage > Health / 2 ? 1.0 : PainChance;

        if (Health > 0)
        {
            if (!IsStunned() && Random.Shared.NextSingle() < _stun)
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
                    if (PainSounds.Count > 0)
                    {
                        var stream = PainSounds[Random.Shared.Next(0, PainSounds.Count)];
                        AudioManager.TryPlay(stream, AudioBus.Enemies, GlobalPosition);
                    }
                    _stun_timer.WaitTime = StunDuration;
                    _stun_timer.Start();
                }
            }
        }
    }

    public void DeathAnimation()
    {
        if (!Visible) return;
        if (AnimStateMachine.GetCurrentNode() == "base_die")
        {
            // shake sprite
            float shakex, shakey, shakez;
            shakex = _death_shake_factor * (Random.Shared.NextSingle() * 2.0f - 1.0f);
            shakey = _death_shake_factor * (Random.Shared.NextSingle() * 2.0f - 1.0f);
            shakez = _death_shake_factor * (Random.Shared.NextSingle() * 2.0f - 1.0f);
            Sprite.Position = _base_sprite_position + new Vector3(shakex, shakey, shakez);

            if ((bool)_sprite_shader.GetShaderParameter("pulse_mode") != false) _sprite_shader.SetShaderParameter("pulse_mode", false);
            if ((bool)_sprite_shader.GetShaderParameter("flash_enabled") != true) _sprite_shader.SetShaderParameter("flash_enabled", true);
            if ((float)_sprite_shader.GetShaderParameter("intensity") != 0.5f) _sprite_shader.SetShaderParameter("intensity", 0.5f);
            if ((Color)_sprite_shader.GetShaderParameter("flash_color") != RED) _sprite_shader.SetShaderParameter("flash_color", RED);

            if (_deathTimer.IsStopped() && Visible)
            {
                var blood_fountain = _death_blood_fountain.Instantiate() as GpuParticles3D;
                blood_fountain.Emitting = true;
                blood_fountain.Finished += blood_fountain.QueueFree;
                var deathsmoke = _death_smoke.Instantiate() as GpuParticles3D;
                deathsmoke.Emitting = true;
                deathsmoke.Finished += deathsmoke.QueueFree;
                GetTree().CurrentScene.AddChild(blood_fountain);
                GetTree().CurrentScene.AddChild(deathsmoke);
                blood_fountain.SetGlobalPosition(GlobalPosition);
                deathsmoke.SetGlobalPosition(GlobalPosition + Vector3.Up * 0.5f);

                var coinAmount = 0;
                //GD.Print("die faces: ", (int)CoinDropDice);
                for (int i = 0; i < CoinDropDiceAmount; i++)
                {
                    coinAmount += Mathf.FloorToInt(Random.Shared.NextSingle() * (int)CoinDropDice) + 1;
                }
                //GetTree().CurrentScene.AddChild(TreasureSpawner.Create(GlobalPosition + Vector3.Up * 0.5f, coinAmount, 0.5f));

                // spawn coins and XP gems
                TreasureSpawner.PickupCoinsAtNode(this, Mathf.Max(coinAmount,1));
                var spawner = TreasureSpawner.Create(GlobalPosition+Vector3.Up*0.5f, Random.Shared.Next(1,4), 0.5f, true);
                GetTree().CurrentScene.AddChild(spawner);
                spawner.GlobalPosition = GlobalPosition + Vector3.Up * 0.5f;

                Visible = false;
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
            if (DeathSounds.Count > 0)
            {
                var stream = DeathSounds[Random.Shared.Next(0, DeathSounds.Count)];
                AudioManager.TryPlay(stream, AudioBus.Enemies, GlobalPosition);
            }
            AnimStateMachine.Travel("base_die", true);
            _deathTimer.WaitTime = AnimTree.GetAnimation("base_die").Length;
            _deathTimer.Start();
        }
    }

    // ==================================================================
    // ====== Helper Methods ========
    // ==================================================================

    private void SetFootstepTimer()
    {
        // play foostep sounds
        // reset footstep timer, which plays footstep sound when it times out
        if (!Tags.Contains(TagEnum.Flying) && _footstep_timer.IsStopped())
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

    public override void _ExitTree()
    {
        // ensure current attack resets params/frees any objects its controlling
        StopAttacking();
    }

    public void ImpulseTowardsPlayer(float impulseMagnitude)
    {
        if ((this != null) && IsInstanceValid(this) && IsInsideTree()
        && Player.Instance != null && IsInstanceValid(Player.Instance) && Player.Instance.IsInsideTree())
        {
            var dir = (Player.Instance.GlobalPosition - GlobalPosition).Normalized();
            ApplyCentralImpulse(dir * impulseMagnitude * Mass);
        }
    }
    public void ForceTowardsPlayer(float forceMagnitude)
    {
        if ((this != null) && IsInstanceValid(this) && IsInsideTree()
        && Player.Instance != null && IsInstanceValid(Player.Instance) && Player.Instance.IsInsideTree()) 
        {
            var dir = (Player.Instance.GlobalPosition - GlobalPosition).Normalized();
            ApplyCentralForce(dir * forceMagnitude * Mass);
        }
    }
    public void SetStun(float duration)
    {
        _stun_timer.Stop();
        _stun_timer.WaitTime = duration;
        _stun_timer.Start();
    }
    public void SetPlayerCurrentAttack(IAttack attack)
    {
        if (attack == null) return;
        _currentAttack = attack;
        if (_currentAttack.IsFinished) {
            _currentAttack.ResetParams();
        }
    }
    public bool IsOnFloor()
    {
        GroundRay.ForceRaycastUpdate();
        var col = GroundRay.GetCollider();
        if (Tags.Contains(TagEnum.CanBePickedUp) && State == StateEnum.IsPickedUp)
        {
            return col != null && col is StaticBody3D;
        }
        else return col != null && (col is StaticBody3D || col is RigidBody3D);
    }
    public bool IsPlayerInRange(float range)
    {
        return Player.Instance.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= range*range;
    }
    public bool IsPlayerVisible()
    {
        if (!IsPlayerInRange(MaxViewRange)) return false;
        for (int i=0; i<_num_rays; i++)
        {
            if (Player.Instance != null && IsInstanceValid(Player.Instance))
            {
                var spaceState = GetWorld3D().DirectSpaceState;

                Vector3 origin = GlobalPosition+Vector3.Up, end = Player.Instance.GlobalPosition+Vector3.Up*(i+0.5f)*Player.PLAYER_HEIGHT/_num_rays;
                var dirToPlayer = (end - origin).Normalized();
                var query = PhysicsRayQueryParameters3D.Create(origin, end+dirToPlayer*2.0f);
                query.Exclude = [GetRid()];
                query.CollideWithBodies = true;

                var result = spaceState.IntersectRay(query);

                if (result.Count > 0)
                {
                    var res_obj = result["collider"].AsGodotObject();
                    //GD.Print($"Ray {i}: {res_obj}");
                    if (res_obj != null && res_obj is not StaticBody3D && res_obj is Player)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    public Vector3 XZDirectionToPlayer()
    {
        return 
            new Vector3(GlobalPosition.X, 0.0f, GlobalPosition.Z)
            .DirectionTo(new Vector3(Player.Instance.GlobalPosition.X, 0.0f, Player.Instance.GlobalPosition.Z));
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
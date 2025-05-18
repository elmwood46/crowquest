using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

public partial class Player : CharacterBody3D, IHurtable
{
	[ExportCategory("Player Features")]
	[Export] public int MaxHealth { get; set; } = 100;
	[Export] public float MoveSpeed { get; set; } = 5.0f;
	[Export] public float JumpVelocity { get; set; } = 4.8f;
	[Export] public float SprintFactor { get; set; } = 1.5f;
	[Export] public float MoveLerpFactor { get; set; } = 10.0f;
	[Export] public float Gravity { get; set; } = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	[Export] public float MouseSensitivity = 0.3f;
	[Export(PropertyHint.Range, "0.01,1.5,0.01")] public float MaxCameraShake = 2.0f;
	[Export(PropertyHint.Range, "0.001,0.05,0.001")] public float CameraShakeDecay { get; private set; } = 0.05f;
	[Export] public float MinCameraSize { get; set; } = 20.0f;
	[Export] public float MaxCameraSize { get; set; } = 40.0f;
	public const float PLAYER_HEIGHT = 1.32f;

	[ExportCategory("Debug Set Instance Vars")]
	[Export] public int ForceAutoAttackNumber { get; set; } = 5;
	[Export] public long ForceIFramesAmount { get; set; } = 32L;

	// =======================================================================
	// instance vars
	private int _money = 0;
	private int _XP = 0;
	private bool _player_is_active = true;
	private Timer _autoattack_timer = new() { WaitTime = 0.2f, OneShot = true };
	private float _autoattack_cooldown = 1.0f;
	private int _autoattack_number = 5;
	private float _autoattack_homing = 0.1f;
	private int _autoattack_damage = 10;
	private float _autoattack_bullet_speed = 20.0f;
	public HashSet<Enemy> VisibleEnemiesInRange = [];
	// rolling
	private bool _can_roll = true;
	private float _roll_cooldown_time = 0.3f;
	private Timer _roll_cooldown_timer = new() { OneShot = true };
	private bool _is_rolling = false;
	private int _roll_iframes = 20;
	private int _roll_iframe_counter = 0;
	private float _roll_duration = 1.0f;
	private float _roll_animation_speedscale;
	private float _roll_move_speed = 12.0f;
	private Vector3 _saved_roll_dir = default;
	public bool HasRollIFrames() { return _roll_iframe_counter > 0; }
	public void ForceStopRolling()
	{
		_is_rolling = false;
		HandleAnimations();
		_roll_iframe_counter = 0;
		_can_roll = false;
		_roll_cooldown_timer.Start();
	}

	// =======================================================================
	// damage & death vars
	private readonly Stopwatch _iFramesStopwatch = new();
	private long _iFramesWaitTime = 32L;
	private float _timeSinceDeath = 0.0f;
	public bool IsDead => _current_health <= 0;
	private float _min_damage_vignette_ratio = 0.0f;
	private float _damage_vignette_ratio = 0.0f;
	private int _current_health;
	private Timer _pulse_damage_timer = new() { Autostart = true, WaitTime = 0.3 };
	private Vector2 _damage_pulse_time = new(0.3f, 2.0f);
	private Timer _blood_spray_timer = new() { OneShot=true, WaitTime = 0.1f };
	private double _blood_spray_duration = 0.25d;
	private ShaderMaterial _player_hurt_next_pass;

	// =======================================================================
	// camera vars
	const float _MAX_CAM_SIZE_CHANGE = 5.0f;
	private Node3D _camera_gimbal;
	private float _cameraXRotation = 0.0f;
	private float _camera_zoom;
	private float _default_camera_zoom => (MinCameraSize + MaxCameraSize)/2f;
	private Vector3 _default_camera_rotation;
	private Camera3D _camera_3d;
	private Label _angle_label;
	private bool _lock_camera_angle = false;
	private float _camera_shake_amount = 0.0f;

	// =======================================================================
	//animation vars
	private Node3D _player_model;
	private AnimationTree _anim_tree;
	private AnimationNodeStateMachinePlayback _anim_state_machine;
	private Timer _blink_timer;
	private Timer _reopen_eyes_timer;
	private CompressedTexture2D _close_eye_texture = ResourceLoader.Load<CompressedTexture2D>("res://character_models/gdquest_gobot/textures/closed_eyes.png");
	private CompressedTexture2D _open_eye_texture = ResourceLoader.Load<CompressedTexture2D>("res://character_models/gdquest_gobot/textures/open_eye.png");
	private CompressedTexture2D _hurt_eye_texture = ResourceLoader.Load<CompressedTexture2D>("res://character_models/gdquest_gobot/textures/hurt_eyes.png");
	private StandardMaterial3D _left_eye_material;
	private StandardMaterial3D _right_eye_material;
	private AudioStreamPlayer3D _footstep_sound_player = new();
	private Timer _footstep_timer = new() { WaitTime = 0.2f, OneShot = true, Autostart = false };
	private float _velocity_sq_last_frame = 0.0f;
	private ulong _lastFrameOnFloor = ulong.MaxValue;

	// =======================================================================
	public static Player Instance { get; private set; }

	[ExportCategory("Node References")]
	[Export] public MeltingEffect ScreenMeltEffect { get; set; }
	[Export] public MeshInstance3D DamageVignette { get; set; }
	[Export] public ShapeCast3D ShapeCast { get; set; } = null;
	[Export] public Label3D HoverText { get; set; } = null;
	[Export] public Area3D CoinPickupArea { get; set; } = null;
	[Export] public RayCast3D GroundCheckRay { get; set; } = null;
	[Export] public CollisionShape3D PlayerCollisionShape { get; set; } = null;
	[Export] public Area3D AutoAttackArea { get; set; } = null;
	[Export] public GpuParticles3D BloodSprayParticles { get; set; } = null;

	// =======================================================================

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;

		Instance = this;
		HoverText.Text = "";

		// health and damage
		_current_health = MaxHealth;
		_pulse_damage_timer.WaitTime = _damage_pulse_time.Y;
		_iFramesStopwatch.Start();
		_iFramesWaitTime = ForceIFramesAmount;
		AddChild(_pulse_damage_timer);

		// autoattack
		_autoattack_timer.WaitTime = _autoattack_cooldown;
		_autoattack_timer.Timeout += () =>
		{

			if (!IsDead && GetActiveNonBlockedEnemiesInArea().Count > 0)
			{
				for (var i = 0; i < _autoattack_number; i++) BulletManager.AddBullet(
						this,
						_autoattack_damage,
						DamageTypeFlagEnum.Physical,
						shot_direction: Vector3.Forward.Rotated(Vector3.Up, Random.Shared.NextSingle() * Mathf.Tau),
						speed: _autoattack_bullet_speed,
						homing_rate: _autoattack_homing,
						color: new Color(0, 1, 1, 1)
					);
				_autoattack_timer.WaitTime = _autoattack_cooldown;
				_autoattack_timer.Start();
			}
			else
			{
				_autoattack_timer.WaitTime = 0.1f;
				_autoattack_timer.Start();
			}
		};
		AddChild(_autoattack_timer);
		_autoattack_timer.Start();
		_autoattack_number = ForceAutoAttackNumber;
		

		// blood spray when damaged
		AddChild(_blood_spray_timer);
		_blood_spray_timer.WaitTime = _blood_spray_duration;
		BloodSprayParticles.AmountRatio = 0.0f;
		var player_model = GetNode<MeshInstance3D>("%Gobot");
		_player_hurt_next_pass = (ShaderMaterial)player_model.GetSurfaceOverrideMaterial(0).NextPass;

		// set mouse mode and capture
		Input.SetMouseMode(Input.MouseModeEnum.Captured);

		// set up area 3d
		CoinPickupArea.BodyEntered += (body) =>
		{
			if (body is Pickup pickup)
			{
				GD.Print("Pickup entered");
				pickup.ActivatePickup = true;
			}
		};

		// set up the camera
		_angle_label = GetNode<Label>("%AngleLabel");
		_camera_gimbal = GetNode<Node3D>("%CameraGimbal");
		_camera_zoom = _default_camera_zoom;
		_camera_3d = GetNode<Camera3D>("%CameraGimbal/Camera3D");
		_camera_3d.Size = _camera_zoom;
		_default_camera_rotation = _camera_gimbal.Rotation;
		_player_model = GetNode<Node3D>("gobot");

		// set up the player model
		_anim_tree = GetNode<AnimationTree>("AnimationTree");
		_anim_state_machine = (AnimationNodeStateMachinePlayback)_anim_tree.Get("parameters/playback");
		_left_eye_material = GetNode<MeshInstance3D>("gobot/Armature/Skeleton3D/Gobot").GetSurfaceOverrideMaterial(1) as StandardMaterial3D;
		_right_eye_material = GetNode<MeshInstance3D>("gobot/Armature/Skeleton3D/Gobot").GetSurfaceOverrideMaterial(2) as StandardMaterial3D;
		_blink_timer = new Timer() { WaitTime = 0.2f, OneShot = true, Autostart = true };
		_reopen_eyes_timer = new Timer() { WaitTime = 0.2f, OneShot = true, Autostart = true };
		_blink_timer.Timeout += () =>
		{
			_left_eye_material.AlbedoTexture = _close_eye_texture;
			_right_eye_material.AlbedoTexture = _close_eye_texture;
			_reopen_eyes_timer.Start(0.085f);
		};
		_reopen_eyes_timer.Timeout += () =>
		{
			_left_eye_material.AlbedoTexture = _open_eye_texture;
			_right_eye_material.AlbedoTexture = _open_eye_texture;
			_blink_timer.Start(Random.Shared.Next(4, 10));
		};
		AddChild(_blink_timer);
		AddChild(_reopen_eyes_timer);
		_blink_timer.Start();

		// rolling
		_roll_cooldown_timer.WaitTime = _roll_cooldown_time;
		_roll_cooldown_timer.Timeout += () =>
		{
			_can_roll = true;
		};
		AddChild(_roll_cooldown_timer);
		_roll_animation_speedscale = _anim_tree.GetAnimation("Flip").Length / _roll_duration;
		_anim_tree.GetAnimation("Flip").Set("speed_scale", _roll_animation_speedscale);
		_anim_tree.AnimationFinished += (anim_name) =>
		{
			if (anim_name == "Flip")
			{
				_is_rolling = false;
				HandleAnimations();
				_roll_cooldown_timer.Start();
			}
		};

		// footsteps
		_camera_3d.AddChild(_footstep_sound_player);
		_footstep_sound_player.Bus = "Footsteps";
		AddChild(_footstep_timer);
		_footstep_timer.Timeout += () =>
		{
			if (Velocity.LengthSquared() > 0.1f && IsOnFloor())
			{
				PlayFootstepSound();
			}
		};

		// add audio players to camera listener
		foreach (var kvp in AudioManager.keyValuePairs)
		{
			_camera_3d.AddChild(kvp.Value);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (Input.IsActionJustPressed("Interact") && IsDead && _timeSinceDeath > 0.5f)
		{
			LoadSavedState();
			return;
		}

		if (@event is InputEventKey)
		{
			var keyEvent = @event as InputEventKey;
			// if (keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
			// {
			// 	if (Input.GetMouseMode() == Input.MouseModeEnum.Visible)
			// 	{
			// 		Input.SetMouseMode(Input.MouseModeEnum.Captured);
			// 		_player_is_active = true;
			// 	}
			// 	else
			// 	{
			// 		Input.SetMouseMode(Input.MouseModeEnum.Visible);
			// 		_player_is_active = false;
			// 	}
			// }
			//else if (keyEvent.Pressed && keyEvent.Keycode == Key.F1)
			if (keyEvent.Pressed && keyEvent.Keycode == Key.F1)
			{
				if (GetViewport().DebugDraw == Viewport.DebugDrawEnum.Wireframe)
				{
					RenderingServer.SetDebugGenerateWireframes(false);
					GetViewport().DebugDraw = Viewport.DebugDrawEnum.Disabled;
				}
				else
				{
					RenderingServer.SetDebugGenerateWireframes(true);
					GetViewport().DebugDraw = Viewport.DebugDrawEnum.Wireframe;
				}
			}
			else if (keyEvent.Pressed && keyEvent.Keycode == Key.F2)
			{
				_lock_camera_angle = !_lock_camera_angle;
			}
			else if (keyEvent.Pressed && keyEvent.Keycode == Key.F3)
			{
				var debug_overlay = GetTree().GetNodesInGroup("debug_overlay");
				foreach (var node in debug_overlay)
				{
					if (node is Control debug_gui)
					{
						debug_gui.Visible = !debug_gui.Visible;
					}
				}
			}
			else if (keyEvent.Pressed && keyEvent.Keycode == Key.F4)
			{
				TakeDamage(5, DamageTypeFlagEnum.Physical);
			}
		}

		if (!_player_is_active) return;

		if (@event is InputEventMouseMotion)
		{
			var mouseMotion = @event as InputEventMouseMotion;
			var deltaX = mouseMotion.Relative.Y * MouseSensitivity;
			var deltaY = -mouseMotion.Relative.X * MouseSensitivity;

			if (!_lock_camera_angle) _camera_gimbal.RotateY(Mathf.DegToRad(deltaY));

			// if (!_lock_camera_angle && _cameraXRotation + deltaX > -90 && _cameraXRotation + deltaX < 90)
			// {
			// 	_camera_3d.RotateX(Mathf.DegToRad(-deltaX));
			// 	_cameraXRotation += deltaX;
			// }

			_angle_label.Text = $"Pitch: {_cameraXRotation} Yaw: {_camera_gimbal.Rotation.Y}";
		}

		if (@event is InputEventMouseButton)
		{
			var mouseButton = @event as InputEventMouseButton;
			if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
			{
				_camera_zoom++;
				if (_camera_zoom > MaxCameraSize) _camera_zoom = MaxCameraSize;
			}
			else if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
			{
				_camera_zoom--;
				if (_camera_zoom < MinCameraSize) _camera_zoom = MinCameraSize;
			}
		}
	}

	public InteractableComponent GetInteractableComponentAtShapecast()
	{
		// confirms the first collider is the player character body; if not, something is wrong 
		if (ShapeCast.GetCollisionCount() > 0 && ShapeCast.GetCollider(0) != this)
			return null;

		for (int i = 0; i < ShapeCast.GetCollisionCount(); i++)
		{
			if (ShapeCast.GetCollider(i) is not Node collider) continue;
			foreach (var child in collider.GetChildren(true))
			{
				if (child is InteractableComponent interactable)
				{
					return interactable;
				}
			}
		}
		return null;
	}

	private void DoBloodSpray()
	{
		if (IsDead)
		{
			BloodSprayParticles.AmountRatio = 1f;
			_player_hurt_next_pass.SetShaderParameter("amount", 1f);
			return;
		}
		
		// animate blood spray timer
		if (_blood_spray_timer.TimeLeft <= 0)
		{
			BloodSprayParticles.AmountRatio = 0.0f;
			_player_hurt_next_pass.SetShaderParameter("amount", 0.0f);
			_blood_spray_timer.Stop();
		}
		else
		{
			var amount = (float)(_blood_spray_timer.TimeLeft / _blood_spray_timer.WaitTime);
			BloodSprayParticles.AmountRatio = amount;
			_player_hurt_next_pass.SetShaderParameter("amount", amount);
		}
	}

	public override void _Process(double delta)
	{
		DoBloodSpray();

		if (IsDead)
		{
			_timeSinceDeath += (float)delta;
			LerpCameraSize(_camera_zoom);
			_lock_camera_angle = true;
			_camera_gimbal.RotateY(Mathf.DegToRad(0.5f));
		}

		InteractableComponent interactable = GetInteractableComponentAtShapecast();
		if (interactable != null)
		{
			HoverText.Text = InteractableComponent.InteractButtonName + " " + interactable.HoverText;
			interactable.HoverCursor(this);
			if (Input.IsActionJustPressed("Interact"))
			{
				interactable.Interact();
			}
		}
		else HoverText.Text = "";
	}

	public override void _PhysicsProcess(double delta)
	{
		var fps_label = GetNode<Label>("%FpsLabel");
		fps_label.Text = $"FPS: {Engine.GetFramesPerSecond()}";
		if (!_player_is_active) return;

		var velocity = Velocity;

		// get input direction		
		var inputDirection = Input.GetVector("MoveLeft", "MoveRight", "MoveBack", "MoveForward");
		//var y_input = ((Input.IsActionPressed("Jump") ? 1.0f : 0.0f) - (Input.IsActionPressed("Crouch") ? 1.0f : 0.0f)) * Vector3.Up;

		var camera_basis = Basis.FromEuler(_camera_3d.GlobalRotation * Vector3.Up);
		var direction = new Vector3(inputDirection.X, 0.0f, -inputDirection.Y);
		direction = camera_basis * direction.Normalized();

		if (!_is_rolling && GlobalTransform * -direction != _player_model.GlobalPosition)
		{
			_player_model.LookAt(GlobalTransform * -direction);
		}

		// check for jump, roll, footstep sounds, apply gravity

		if (IsOnFloor())
		{
			// jump
			if (Input.IsActionJustPressed("Jump"))
			{
				//velocity.Y = JumpVelocity;
				if (_can_roll)
				{
					if (direction != Vector3.Zero) _saved_roll_dir = direction;
					else _saved_roll_dir = _player_model.GlobalBasis * Vector3.Back;

					_is_rolling = true;
					_can_roll = false;
					_velocity_sq_last_frame = 0.0f;
					PlayFootstepSound();
					_anim_state_machine.Travel("Roll");
					_roll_iframe_counter = _roll_iframes+1;
				}
			}

			// play footstep sound if landing or if just starting to move
			if (!_is_rolling)
			{
				if (_lastFrameOnFloor < Engine.GetPhysicsFrames() - 1
					|| (velocity.LengthSquared() > 0.2f && _velocity_sq_last_frame <= 0.2f))
				{
					_footstep_sound_player.Stop();
					_footstep_timer.Stop();
					PlayFootstepSound(_lastFrameOnFloor < Engine.GetPhysicsFrames() - 1);
				}
			}

			_lastFrameOnFloor = Engine.GetPhysicsFrames();
		}
		else
		{
			velocity.Y -= Gravity * (float)delta;
		}
		_velocity_sq_last_frame = velocity.LengthSquared();

		//direction += y_input;
		var move_speed = MoveSpeed;
		var sprint_speed = MoveSpeed * SprintFactor;

		if (_is_rolling)
		{
			move_speed = _roll_move_speed;
			direction = _saved_roll_dir;
		}
		else
		{
			// SPRINTING
			//move_speed = Input.IsActionPressed("Sprint") ? sprint_speed : MoveSpeed;

			// reset footstep timer, based on moving speed, which plays footstep sound when it times out
			ResetFootstepTimer(move_speed);
		}

		velocity.X = Mathf.Lerp(velocity.X, direction.X * move_speed, (float)delta * MoveLerpFactor);
		//velocity.Y = Mathf.Lerp(velocity.Y, direction.Y * move_speed, (float)delta * MoveLerpFactor);
		velocity.Z = Mathf.Lerp(velocity.Z, direction.Z * move_speed, (float)delta * MoveLerpFactor);

		Velocity = velocity;

		MoveAndSlide();

		HandleAnimations();
		DoCameraShake();
		UpdateDamageVignette();

		// FOV
		var max_vel_clamped = _is_rolling ? _roll_move_speed : sprint_speed * 2.0f;
		var velocity_clamped = Mathf.Clamp(velocity.Length(), 0.5f, max_vel_clamped);
		LerpCameraSize(_camera_zoom + _MAX_CAM_SIZE_CHANGE * velocity_clamped / max_vel_clamped);

		if (Input.IsActionPressed("ResetCameraZoom"))
		{
			ResetCameraZoom();
		}

		if (_roll_iframe_counter > 0) _roll_iframe_counter--;
	}

	// =======================================================================

	public void ResetFootstepTimer(float move_speed)
	{
		if (_footstep_timer.IsStopped())
		{
			if (move_speed >= MoveSpeed && move_speed < move_speed * SprintFactor)
			{
				_footstep_timer.WaitTime = AudioManager.FootstepWaitTimes.X;

			}
			else if (move_speed >= MoveSpeed * SprintFactor)
			{
				_footstep_timer.WaitTime = AudioManager.FootstepWaitTimes.Y;
			}
			else
			{
				_footstep_timer.WaitTime = AudioManager.FootstepWaitTimes.Z;
			}

			_footstep_timer.Start();
		}
	}

	public string CameraFacedDirection()
	{
		var faced_vec = Vector3.Forward.Rotated(Vector3.Up, _camera_gimbal.Rotation.Y);
		return $"Camera Facing: {-faced_vec}";
	}

	public static void AddMoney(int amount)
	{
		Instance._money += amount;
	}
	public static int GetMoney()
	{
		return Instance._money;
	}

	public static void AddXP(int amount)
	{
		Instance._XP += amount;
	}
	public static int GetXP()
	{
		return Instance._XP;
	}

	public static int GetHealth()
	{
		return Instance._current_health;
	}

	public static Vector3 GetCameraPosition()
	{
		return Instance._camera_3d.GlobalPosition;
	}

	public void TakeDamage(int damage, DamageTypeFlagEnum type)
	{
		if (IsDead) return;//{ GD.Print("player is dead"); return; }
		if (HasRollIFrames()) return; //{ GD.Print("player has roll iframes"); return; }
		if (_iFramesStopwatch.ElapsedMilliseconds > _iFramesWaitTime)
		{
			_blood_spray_timer.Start(_blood_spray_duration);
			var damage_ratio = 1.0f * damage / MaxHealth;
			AddCameraShake(damage_ratio);//10*damage/MaxHealth);
			_iFramesStopwatch.Restart();
			_current_health -= damage;
			_damage_vignette_ratio += damage_ratio;
			if (_min_damage_vignette_ratio >= 1.0f) _damage_vignette_ratio = 1;
			else _damage_vignette_ratio = Mathf.Clamp(_damage_vignette_ratio, _min_damage_vignette_ratio, 1.0f);
			if (_current_health <= 0)
			{
				_autoattack_timer.Stop();
				_current_health = 0;
				_player_is_active = false;
				GD.Print("Player died");
				//ResetCameraZoom();
				_camera_zoom = MinCameraSize;
				UpdateDamageVignette();
			}
		} //else GD.Print("player is invincible");
	}

	public void AddCameraShake(float amount)
	{
		amount = Mathf.Clamp(amount, 0, 1);
		_camera_shake_amount = Mathf.Clamp(_camera_shake_amount + amount * MaxCameraShake, 0.0f, MaxCameraShake);
	}

	private void DoCameraShake()
	{
		if (_camera_shake_amount <= 0) return;
		var shake = new Vector3((float)GD.RandRange(-_camera_shake_amount, _camera_shake_amount), (float)GD.RandRange(-_camera_shake_amount, _camera_shake_amount), (float)GD.RandRange(-_camera_shake_amount, _camera_shake_amount));
		_camera_gimbal.Position = shake;
		_camera_shake_amount = Mathf.Max(_camera_shake_amount - CameraShakeDecay, 0.0f);
		if (_camera_shake_amount <= 0)
		{
			_camera_shake_amount = 0;
			_camera_gimbal.Position = Vector3.Zero;
		}
	}

	private void SetCameraSize(float size)
	{
		_camera_3d.Size = size;
	}
	private void LerpCameraSize(float size)
	{
		_camera_3d.Size = Mathf.Lerp(_camera_3d.Size, size, 0.2f);
	}

	private void LoadSavedState()
	{
		_blood_spray_timer.Stop();

		// reset camera
		ResetCameraZoom();
		SetCameraSize(_camera_zoom);
		_camera_gimbal.Rotation = _default_camera_rotation;
		_lock_camera_angle = false;

		// reset player model
		//WeaponManager.Instance.CurrentWeapon = GD.Load("res://fpscontroller/weaponmanager/weapons/p90/p90.tres") as WeaponResource;
		_timeSinceDeath = 0.0f;
		_current_health = MaxHealth;
		GlobalPosition = Vector3.Zero + Vector3.Up * 2.0f; //SaveManager.GetCachedPlayerPosition();
		_player_is_active = true;
		_autoattack_timer.Start();

		// do screen melt effect
		ScreenMeltEffect.Set("melting", true);
		ScreenMeltEffect.Melt();
	}

	private void UpdateDamageVignette()
	{
		// set min damage vignette
		_pulse_damage_timer.WaitTime = _damage_pulse_time.X + 1.0f * _current_health / MaxHealth * (_damage_pulse_time.Y - _damage_pulse_time.X);
		var pulse_ratio = (float)(_pulse_damage_timer.TimeLeft / _pulse_damage_timer.WaitTime);
		var quarter_health = MaxHealth / 4;
		var almost_dead = MaxHealth / 10;
		if (_current_health < almost_dead)
		{
			_min_damage_vignette_ratio = 0.1f + 0.4f * pulse_ratio;
		}
		else if (_current_health < quarter_health)
		{
			_min_damage_vignette_ratio = 0.1f * pulse_ratio;
		}
		else _min_damage_vignette_ratio = 0.0f;

		if (_current_health == 0) _min_damage_vignette_ratio = 1.0f;
		_damage_vignette_ratio = Mathf.Lerp(_damage_vignette_ratio, _min_damage_vignette_ratio, 0.1f);
		((ShaderMaterial)DamageVignette.GetActiveMaterial(0)).Set("shader_parameter/damage_ratio", _damage_vignette_ratio);
	}

	// =======================================================================

	public void HandleAnimations()
	{
		if (_is_rolling)
		{
			_anim_state_machine.Travel("Roll");
			return;
		}

		if (!IsOnFloor())
			{
				if (Velocity.Y >= 0) _anim_state_machine.Travel("Jump");
				else _anim_state_machine.Travel("Fall");
				return;
			}

		if (Velocity.LengthSquared() <= 0.1f)
		{
			_anim_state_machine.Travel("Idle");
		}
		else
		{
			var vel_length = Velocity.Length();
			if (vel_length > MoveSpeed * 1.1f)
			{
				_anim_state_machine.Travel("Run");
			}
			else
			{
				_anim_state_machine.Travel("Walk");
			}

			_anim_tree.Set("parameters/Walk/blend_position", new Vector2(Velocity.X, Velocity.Z) / MoveSpeed);
			_anim_tree.Set("parameters/Walk/blend_amount", vel_length / MoveSpeed);
			_anim_tree.Set("parameters/Run/blend_position", new Vector2(Velocity.X, Velocity.Z) / (MoveSpeed * SprintFactor));
			_anim_tree.Set("parameters/Run/blend_amount", vel_length / (MoveSpeed * SprintFactor));
		}
	}

	public void ResetCameraZoom()
	{
		_camera_zoom = _default_camera_zoom;
	}

	private void PlayFootstepSound(bool force_play = false)
	{
		GroundCheckRay.ForceRaycastUpdate();
		var floorBelow = GroundCheckRay.IsColliding() && !(GroundCheckRay.GetCollisionNormal().AngleTo(Vector3.Up) > FloorMaxAngle);
		if ((floorBelow && Velocity.LengthSquared() > 0.2f) || force_play)
		{
			var collider = GroundCheckRay.GetCollider();
			var _footstep_sound = AudioManager.FootstepSounds["default"][Random.Shared.Next(0, AudioManager.FootstepSounds["default"].Count)];
			_footstep_sound_player.VolumeDb = 0.0f;

			// chunkplane means grass sound
			if (collider is ChunkPlane)
			{
				_footstep_sound_player.VolumeDb = -20.0f; // set to different values for different sounds (grass sounds are too loud)
				if (Random.Shared.NextSingle() < 0.5f)
					_footstep_sound = AudioManager.FootstepSounds["grass"][Random.Shared.Next(0, AudioManager.FootstepSounds["grass"].Count)];
				else _footstep_sound = AudioManager.FootstepSounds["grass_2"][Random.Shared.Next(0, AudioManager.FootstepSounds["grass_2"].Count)];
			}
			_footstep_sound_player.Stream = _footstep_sound;
			_footstep_sound_player.Play();
		}
	}

	public void SetChunkMaxHeight(float val_changed)
	{
		ChunkManager.Instance.MaxHeight = val_changed;
		var height_node = GetNode<Label>("Control/PanelContainer/VBoxContainer/HBoxContainer/Label");
		height_node.Text = $"Max Height: {val_changed}";
		GlobalPosition = new Vector3(GlobalPosition.X, val_changed, GlobalPosition.Z);
	}

	public void SetChunkResolution(float val_changed)
	{
		var res_node = GetNode<Label>("Control/PanelContainer/VBoxContainer/HBoxContainer2/Label");
		var res_slider = GetNode<Slider>("Control/PanelContainer/VBoxContainer/HBoxContainer2/HSlider");
		var inv_chunk_size = 1.0f / ChunkManager.Instance.ChunkSize;
		var resolution_amt = val_changed / res_slider.MaxValue;
		ChunkManager.Instance.PlaneResolution = Math.Max(inv_chunk_size, Mathf.RoundToInt(resolution_amt / inv_chunk_size) * inv_chunk_size);
		res_node.Text = $"Resolution: {val_changed}%, {ChunkManager.Instance.PlaneResolution}";
	}

	public void ChangeNoiseFrequency(float new_val)
	{
		var res_node = GetNode<Label>("Control/PanelContainer/VBoxContainer/HBoxContainer3/Label");
		res_node.Text = $"Noise Frequency: {new_val}";
		((FastNoiseLite)ChunkManager.Instance.NoiseTexture).Frequency = new_val;
		ChunkManager.Instance.UpdateAllChunks();
	}

	public List<Enemy> GetActiveNonBlockedEnemiesInArea()
	{
		var enemies = new List<Enemy>();
		foreach (var body in AutoAttackArea.GetOverlappingBodies())
		{
			if (body is Enemy enemy && !enemy.IsDead() && enemy.Visible && !enemy.Freeze)
			{
				if (enemy.IsPlayerVisible()) enemies.Add(enemy);
			}
		}
		return enemies;
	}
}
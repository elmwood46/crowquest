using Godot;
using System;

public partial class Player : CharacterBody3D
{
	[Export] public float MoveSpeed {get;set;} = 5.0f;
	[Export] public float JumpVelocity {get;set;} = 4.8f;
	[Export] public float SprintFactor {get;set;} = 1.5f;
	[Export] public float MoveLerpFactor {get;set;} = 10.0f;
	[Export] public float Gravity {get;set;} = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
	[Export] public float MouseSensitivity = 0.3f;
	[Export] public float MinCameraSize {get; set;} = 20.0f;
	[Export] public float MaxCameraSize {get; set;} = 50.0f;

	private bool _player_is_active = true;
	private Node3D _camera_gimbal;
	private float _cameraXRotation = 0.0f;
	private float _camera_zoom;
    private Camera3D _camera_3d;
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
	private Label _angle_label;
	private bool _lock_camera_angle = false;
    public static Player Instance { get; private set; }

    public override void _Ready() 
    {
        if (Engine.IsEditorHint()) return;

        Instance = this;

		// set mouse mode and capture
		Input.SetMouseMode(Input.MouseModeEnum.Captured);

		_angle_label = GetNode<Label>("%AngleLabel");

		_camera_gimbal = GetNode<Node3D>("%CameraGimbal");
		_camera_zoom = (MinCameraSize+MaxCameraSize)/2;
		_camera_3d = GetNode<Camera3D>("%CameraGimbal/Camera3D");
		_camera_3d.Size = _camera_zoom;
		_player_model = GetNode<Node3D>("gobot");

		_anim_tree = GetNode<AnimationTree>("AnimationTree");
		_anim_state_machine = (AnimationNodeStateMachinePlayback)_anim_tree.Get("parameters/playback");

		_left_eye_material = GetNode<MeshInstance3D>("gobot/Armature/Skeleton3D/Gobot").GetSurfaceOverrideMaterial(1) as StandardMaterial3D;
		_right_eye_material = GetNode<MeshInstance3D>("gobot/Armature/Skeleton3D/Gobot").GetSurfaceOverrideMaterial(2) as StandardMaterial3D;
		_blink_timer = new Timer(){WaitTime = 0.2f, OneShot = true, Autostart = true};
		_reopen_eyes_timer = new Timer(){WaitTime = 0.2f, OneShot = true, Autostart = true};
		_blink_timer.Timeout += () => {
			_left_eye_material.AlbedoTexture = _close_eye_texture;
			_right_eye_material.AlbedoTexture = _close_eye_texture;
			_reopen_eyes_timer.Start(0.085f);
		};
		_reopen_eyes_timer.Timeout += () => {
			_left_eye_material.AlbedoTexture = _open_eye_texture;
			_right_eye_material.AlbedoTexture = _open_eye_texture;
			_blink_timer.Start(Random.Shared.Next(4,10));
		};
		AddChild(_blink_timer);
		AddChild(_reopen_eyes_timer);
		_blink_timer.Start();
    }

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey)
		{
			var keyEvent = @event as InputEventKey;
			if (keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
			{
				if (Input.GetMouseMode() == Input.MouseModeEnum.Visible)
				{
					Input.SetMouseMode(Input.MouseModeEnum.Captured);
					_player_is_active = true;
				}
				else
				{
					Input.SetMouseMode(Input.MouseModeEnum.Visible);
					_player_is_active = false;
				}
			}
			else if (keyEvent.Pressed && keyEvent.Keycode == Key.F1)
			{
				if (GetViewport().DebugDraw==Viewport.DebugDrawEnum.Wireframe) {
					RenderingServer.SetDebugGenerateWireframes(false);
					GetViewport().DebugDraw=Viewport.DebugDrawEnum.Disabled;
				} else {
					RenderingServer.SetDebugGenerateWireframes(true);
					GetViewport().DebugDraw=Viewport.DebugDrawEnum.Wireframe;
				}
			}
			else if (keyEvent.Pressed && keyEvent.Keycode == Key.F2)
			{
				_lock_camera_angle = !_lock_camera_angle;
			}
		}

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
			_camera_3d.Size = _camera_zoom;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_player_is_active) return;

		var velocity = Velocity;

		if (!IsOnFloor())
		{
			velocity.Y -= Gravity * (float)delta;
		}
		else if (Input.IsActionJustPressed("Jump"))
		{
			velocity.Y = JumpVelocity;
		}

		var inputDirection = Input.GetVector("MoveLeft", "MoveRight", "MoveBack", "MoveForward");
		var y_input = ((Input.IsActionPressed("Jump") ? 1.0f : 0.0f) - (Input.IsActionPressed("Crouch") ? 1.0f : 0.0f)) * Vector3.Up;

		var direction = new Vector3(inputDirection.X, 0.0f, -inputDirection.Y);
		direction = Basis.FromEuler(_camera_3d.GlobalRotation*Vector3.Up) * direction.Normalized();

		if (GlobalTransform*-direction != _player_model.GlobalPosition)
		{
			_player_model.LookAt(GlobalTransform*-direction);
		}
		
		//direction += y_input;
		
		var move_speed = MoveSpeed * (Input.IsActionPressed("Sprint") ? SprintFactor : 1.0f);

		velocity.X = Mathf.Lerp(velocity.X, direction.X * move_speed, (float)delta * MoveLerpFactor);
		//velocity.Y = Mathf.Lerp(velocity.Y, direction.Y * move_speed, (float)delta * MoveLerpFactor);
		velocity.Z = Mathf.Lerp(velocity.Z, direction.Z * move_speed, (float)delta * MoveLerpFactor);

		Velocity = velocity;

		MoveAndSlide();

		HandleAnimations();
	}

	public void HandleAnimations()
	{
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
			if (vel_length > MoveSpeed*1.1f)
			{
				_anim_state_machine.Travel("Run");
			}
			else
			{
				_anim_state_machine.Travel("Walk");
			}
			
			_anim_tree.Set("parameters/Walk/blend_position", new Vector2(Velocity.X, Velocity.Z)/MoveSpeed);
			_anim_tree.Set("parameters/Walk/blend_amount", vel_length/MoveSpeed);
			_anim_tree.Set("parameters/Run/blend_position", new Vector2(Velocity.X, Velocity.Z)/(MoveSpeed*SprintFactor));
			_anim_tree.Set("parameters/Run/blend_amount", vel_length/(MoveSpeed*SprintFactor));
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
		GD.Print(val_changed);
		var inv_chunk_size = 1.0f/ChunkManager.Instance.ChunkSize;
		var resolution_amt = val_changed/50.0f;
		ChunkManager.Instance.PlaneResolution = Math.Max(inv_chunk_size,Mathf.RoundToInt(resolution_amt/inv_chunk_size)*inv_chunk_size);
		GD.Print($"Resolution: {ChunkManager.Instance.PlaneResolution}");
		var res_node = GetNode<Label>("Control/PanelContainer/VBoxContainer/HBoxContainer2/Label");
		res_node.Text = $"Resolution: {ChunkManager.Instance.PlaneResolution }";
	}
}
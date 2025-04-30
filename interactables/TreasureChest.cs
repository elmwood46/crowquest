using Godot;
using System;

public partial class TreasureChest : StaticBody3D
{
	private static readonly ShaderMaterial _shine_highlight_shadermat = GD.Load<ShaderMaterial>("res://interactables/mesh_shine_highlight_shader_material.tres");
	private static readonly Curve _chest_light_curve = GD.Load<Curve>("res://interactables/chest_light_curve.tres");
	[Signal] public delegate void OpenedEventHandler();
	[Export] public Godot.Collections.Array<AudioStream> OpenSounds {get;set;}
	[Export] public Node3D SpawnParticleLocation {get;set;}
	[Export] public float OpenTime = 0.5f;
	[Export] public float GlowTime = 1.0f;
	[Export] public string HoverText = "Open";
	[Export] public AnimationPlayer AnimationPlayer;
	[Export] public ChestType Type = ChestType.Sarcophagus;
	[Export] public MeshInstance3D ChestMesh;
	[Export] public OmniLight3D ChestLight;

	public bool IsOpen { get; private set; } = false;

	private InteractableComponent _interactableComponent;
	

	private Timer _animate_shine_timer;
	private Timer _chest_light_timer;

	public override void _Ready()
	{
		_interactableComponent = new InteractableComponent
		{
			HoverText = HoverText
		};
		AddChild(_interactableComponent);
		_interactableComponent.Interacted += Open;
	}

	public void ForceStateOpen()
	{
		if (IsOpen) return;
		AnimationPlayer.Play("open");
		IsOpen = true;
		_interactableComponent?.QueueFree();
		ChestLight?.QueueFree();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_animate_shine_timer != null && IsInstanceValid(_animate_shine_timer))
		{
			var progress = 1.0f- (float)(_animate_shine_timer.TimeLeft / _animate_shine_timer.WaitTime);
			((ShaderMaterial)ChestMesh.MaterialOverride.NextPass).SetShaderParameter("progress", progress);
		}

		if (_chest_light_timer != null && IsInstanceValid(_chest_light_timer))
		{
			if (ChestLight != null && IsInstanceValid(ChestLight))
			{
				var progress = 1.0f- (float)(_chest_light_timer.TimeLeft / _chest_light_timer.WaitTime);
				ChestLight.LightEnergy = 5.0f*_chest_light_curve.Sample(progress);
			}
		}
	}

	public void Open()
	{
		if (IsOpen) return;

		if (OpenSounds.Count > 0)
		{
			AudioManager.TryPlay(OpenSounds[Random.Shared.Next(0, OpenSounds.Count)], AudioBus.Misc, GlobalPosition);
		}

		ChunkPlane.SetChestOpened(this);
		AnimationPlayer.SpeedScale = AnimationPlayer.GetAnimation("open").Length/OpenTime;
		AnimationPlayer.Play("open");
		IsOpen = true;
		EmitSignal(SignalName.Opened);
		_interactableComponent.QueueFree();

		var chest_material = ChestMesh.MaterialOverride;
		var shine_mat = ChestMesh.MaterialOverride.Duplicate() as Material;
		var shadermat = _shine_highlight_shadermat.Duplicate() as ShaderMaterial;
		shine_mat.NextPass = shadermat;
		ChestMesh.MaterialOverride = shine_mat;

		// timer to trigger coin spawns
		var t = new Timer
		{
			WaitTime = 0.1,
			OneShot = true,
			Autostart = false
		};
		t.Timeout += () =>
		{
			t.QueueFree();
			var amount = Type switch
			{
				ChestType.Sarcophagus => Random.Shared.Next(0, 3),
				ChestType.BasicWooden => Random.Shared.Next(5, 15),
				_ => Random.Shared.Next(1, 5)
			};
			var spawn_particles = TreasureSpawner.MoneyParticlesScene().Instantiate<GpuParticles3D>();
			GetTree().GetCurrentScene().AddChild(spawn_particles);
			spawn_particles.Amount = amount == 0 ? 1 : amount;
			spawn_particles.Lifetime = 3.0f;
			spawn_particles.GlobalPosition = SpawnParticleLocation.GlobalPosition;
			AudioManager.TryPlay(Coin.PickupSound, AudioBus.Misc, SpawnParticleLocation.GlobalPosition);
			Player.AddMoney(amount);
			// else
			// {
			// 	var spawner = TreasureSpawner.Create(GlobalPosition, 10, 1.0);
			// 	if (Type is ChestType.Sarcophagus)
			// 	{
			// 		var coffin_spawn_box = new Vector3(0.654f, 0.071f, 1.762f)*0.8f;
			// 		spawner.SetSpawnExtents(coffin_spawn_box);
			// 	}
			// 	AddChild(spawner);
			// }
			
		};
		AddChild(t);
		t.Start();

		_animate_shine_timer = new Timer
		{
			WaitTime = OpenTime,
			OneShot = true,
			Autostart = false
		};
		_animate_shine_timer.Timeout += () =>
		{
			ChestMesh.MaterialOverride = chest_material;
			_animate_shine_timer.QueueFree();
		};
		AddChild(_animate_shine_timer);
		_animate_shine_timer.Start();

		if (ChestLight != null && IsInstanceValid(ChestLight))
		{
			_chest_light_timer = new Timer
			{
				WaitTime = GlowTime,
				OneShot = true,
				Autostart = false
			};
			_chest_light_timer.Timeout += () =>
			{
				_chest_light_timer.QueueFree();
				ChestLight.QueueFree();
			};
			AddChild(_chest_light_timer);
			_chest_light_timer.Start();
		}
	}
}

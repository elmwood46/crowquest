using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class EnemySpawnRoom : Node3D
{
    private List<Enemy> _spawned_enemies = [];
    private float _wall_rise_time = 1.0f;
    private float _time_passed;
    private Vector3 _wall_start_pos, _wall_end_pos;
    private bool _finished_spawning_wall = false;
    private bool _finished_spawning_enemies = false;
    public readonly List<Node3D> EnemiesToSpawn = [];
    private Node3D _brick_wall_enclosure;
    private float _enemy_spawn_time = 1.0f; // Time between enemy spawns
    private readonly HashSet<Vector3> _spawned_enemy_positions = [Vector3.Zero];
    private const int SPAWN_SQUARE_SIZE = 8;
    private static readonly PackedScene SpawnParticlesScene = GD.Load<PackedScene>("res://effects/enemy_spawn/enemy_spawn.tscn");
    private GpuParticles3D[] _wall_rising_particles;
    private bool _enemies_are_dead = false;

    private static readonly Dictionary<string, PackedScene> ALL_ENEMY_SCENES = new()
    {
        ["elijah"] = GD.Load<PackedScene>("res://enemies/enemy_instances/elijah/elijah.tscn"),
        ["storm"] = GD.Load<PackedScene>("res://enemies/enemy_instances/storm/storm.tscn"),
    };

    public override void _Ready()
    {
        //Player.Instance?.ForceCameraZoom(Player.Instance.MaxCameraSize/2f);

        if (EnemiesToSpawn.Count == 0)
        {
            GD.Print($"{this}: No enemies to spawn. Populating the spawner with 2-10 random enemies.");
            PopulateSpawnList();
        }

        _wall_rising_particles = [.. GetNode<Node3D>("WallRisingParticles").GetChildren().OfType<GpuParticles3D>()];
        foreach (var particle in _wall_rising_particles)
        {
            particle.Lifetime = _wall_rise_time;
            particle.Emitting = false;
        }

        _brick_wall_enclosure = GetNode<Node3D>("BrickWallEnclosurePath");
        _wall_start_pos = _brick_wall_enclosure.Position;
        _wall_end_pos = new Vector3(_wall_start_pos.X,-3f, _wall_start_pos.Z); // Assuming the wall rises along the Y-axis and extends 8 units in the Z direction
        _time_passed = 0f;
    }

    public override void _Process(double delta)
    {
        // finish; lower walls and despawn when all enemies dead
        if (!_enemies_are_dead && _finished_spawning_enemies && Engine.GetPhysicsFrames() % 2ul == 0ul)
        {
            _enemies_are_dead = true;

            foreach (var enemy in _spawned_enemies)
            {
                if (IsInstanceValid(enemy) && enemy != null && !enemy.IsDead())
                {
                    _enemies_are_dead = false;
                    break;
                }
            }
            if (_enemies_are_dead) _time_passed = 0f;
        }

        if (_enemies_are_dead)
            {
                if (_time_passed == 0f)
                {
                    foreach (var particle in _wall_rising_particles) particle.Emitting = true;
                }

                _time_passed += (float)delta;

                _brick_wall_enclosure.Position = _wall_end_pos.Lerp(_wall_start_pos, _time_passed / _wall_rise_time);

                if (_time_passed >= _wall_rise_time)
                {
                    // Player.Instance?.StopForcingCameraZoom(); // force camera zoom back to normal
                    QueueFree();
                }

                return;
            }

        // raise walls and spawn enemies
        if (!_finished_spawning_wall)
        {
            if (_time_passed == 0f)
            {
                foreach (var particle in _wall_rising_particles) particle.Emitting = true;
            }

            _time_passed += (float)delta;
            _brick_wall_enclosure.Position = _wall_start_pos.Lerp(_wall_end_pos, _time_passed / _wall_rise_time);

            if (_time_passed >= _wall_rise_time)
            {
                foreach (var particle in _wall_rising_particles) particle.Emitting = false;

                _time_passed = 0f;
                _finished_spawning_wall = true;
            }
        }
        else if (!_finished_spawning_enemies)
        {
            _time_passed += (float)delta;
            if (_time_passed >= _enemy_spawn_time && EnemiesToSpawn.Count > 0)
            {
                var enemy = EnemiesToSpawn[0];
                EnemiesToSpawn.RemoveAt(0);
                var pos = Vector3.Zero;
                while (_spawned_enemy_positions.Contains(pos)) pos = new Vector3(-1, 0, -1) * (SPAWN_SQUARE_SIZE / 2) + new Vector3(Random.Shared.Next(SPAWN_SQUARE_SIZE), Random.Shared.Next(SPAWN_SQUARE_SIZE), Random.Shared.Next(SPAWN_SQUARE_SIZE));

                _spawned_enemies.Add(enemy.GetNode<Enemy>("Enemy"));
                GetTree().CurrentScene.AddChild(enemy); // Add the enemy to the current scene
                enemy.GlobalPosition = GlobalPosition + pos; // Set the enemy's position to the room's position

                // spawn enemy particles
                var p = SpawnParticlesScene.Instantiate<Node3D>();
                enemy.AddChild(p);
                p.GlobalPosition = enemy.GlobalPosition + Vector3.Up * 0.5f; // Spawn particles at the enemy's position

                _time_passed = 0f;
            }

            if (EnemiesToSpawn.Count == 0) _finished_spawning_enemies = true;
        }
    }

    public void PopulateSpawnList(List<string> enemy_names = default)
    {
        if (enemy_names == default) // select up to 10 random enemies from the available enemies
        {
            enemy_names = [];
            var count = Random.Shared.Next(2, 11);
            for (int i = 0; i < count; i++) enemy_names.Add(ALL_ENEMY_SCENES.Keys.ElementAt(Random.Shared.Next(ALL_ENEMY_SCENES.Keys.Count)));
        }

        foreach (var enemy_name in enemy_names)
        {
            if (ALL_ENEMY_SCENES.TryGetValue(enemy_name, out var enemy_scene))
            {
                EnemiesToSpawn.Add(enemy_scene.Instantiate<Node3D>());
            }
            else
            {
                GD.PrintErr($"Enemy scene for {enemy_name} not found.");
            }
        }
    }
}

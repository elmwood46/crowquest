using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class EnemyData{
    public Vector3 Position;
    public Vector3 Velocity;
    public int Health;
    public int MaxHealth;
    public EnemyState State;
    public HashSet<EnemyTag> Tags;
    public Vector3 RandomWalkDir;
    public float JumpVelocity;
    public Timer RandomWalkDirTimer = new(){WaitTime = 1, Autostart = false, OneShot = true};
    public float Speed;
}

public partial class EnemySpawner : Node3D
{
    [Export] int EnemyCount = 5;
    [Export] Vector3 SpawnBoxDimensions;

    private PackedScene _enemy_scene = ResourceLoader.Load<PackedScene>("res://enemies/elijah/elijah.tscn");

    private static readonly List<Enemy> _enemies_pool = new();
    private static readonly List<Vector3> _enemy_directions = new();
    private static readonly List<Timer> _enemy_random_walk = new();

    private static readonly Dictionary<int, EnemyData> _enemy_data = new();

    public override void _Ready()
    {
        CallDeferred(MethodName.DeferredSpawn);
    }

    public void DeferredSpawn()
    {
        for (int i=0; i < EnemyCount; i++)
        {
            var enemy = _enemy_scene.Instantiate();
            ((Enemy)enemy.GetChild(0)).Index = i;
            
            if (i >= 300)
            {
                ((Enemy)enemy.GetChild(0)).Tags.Add(EnemyTag.Flying);
            }
            var setglob = GlobalTransform.Origin
            + new Vector3(
                (float)GD.RandRange(-SpawnBoxDimensions.X, SpawnBoxDimensions.X),
                (float)GD.RandRange(-SpawnBoxDimensions.Y, SpawnBoxDimensions.Y),
                (float)GD.RandRange(-SpawnBoxDimensions.Z, SpawnBoxDimensions.Z));
            //EnemyComputeShaderManager.SetEnemyPosition(i, setglob);
            AddSibling(enemy);
            enemy.CallDeferred(MethodName.SetGlobalPosition, setglob);
            _enemies_pool.Add(((Enemy)enemy.GetChild(0)));
            _enemy_data[i] = new EnemyData{
                Position = setglob,
                Velocity = Vector3.Zero,
                Health = ((Enemy)enemy.GetChild(0)).MaxHealth,
                MaxHealth = ((Enemy)enemy.GetChild(0)).MaxHealth,
                State = ((Enemy)enemy.GetChild(0)).State,
                Tags = ((Enemy)enemy.GetChild(0)).Tags,
                RandomWalkDir = Vector3.Zero,
                JumpVelocity = 5.0f,
                Speed = ((Enemy)enemy.GetChild(0)).Speed,
            };  
            enemy.CallDeferred(MethodName.AddChild,_enemy_data[i].RandomWalkDirTimer);
        }
        //QueueFree();
    }

    private int _batchSize = 100;
    async public override void _Process(double delta)
    {
        var tasks = new List<Task>();
        for (int batch=0; batch < Math.Ceiling(_enemies_pool.Count/(float)_batchSize); batch++)
        {
            tasks.Add(Task.Run(() => {
                for (int idx =0; idx < _batchSize; idx++)
                {
                    if (batch*_batchSize + idx >= _enemies_pool.Count) return Task.CompletedTask;
                    
                }
                return Task.CompletedTask;
            }));
        }
        await Task.WhenAll(tasks);

        foreach (var enemy in _enemies_pool)
        {
            if (enemy.State == EnemyState.Dead)
            {
                enemy.QueueFree();
            }
        }
    }
}
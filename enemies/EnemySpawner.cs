using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class EnemySpawner : Node3D
{
    [Export] int EnemyCount = 5;
    [Export] Vector3 SpawnBoxDimensions;

    private static readonly PackedScene _elijah_scene = ResourceLoader.Load<PackedScene>("res://enemies/enemy_instances/elijah/elijah.tscn");
    private static readonly PackedScene _storm_scene = ResourceLoader.Load<PackedScene>("res://enemies/enemy_instances/storm/storm.tscn");

    private PackedScene[] EnemyScenes = [
        _elijah_scene,
        _storm_scene
    ];

    public override void _Ready()
    {
        CallDeferred(MethodName.DeferredSpawn);
    }

    public void DeferredSpawn()
    {
        for (int i=0; i < EnemyCount; i++)
        {
            var enemy = EnemyScenes[1].Instantiate();
            
            if (i >= 300)
            {
                ((Enemy)enemy.GetChild(0)).Tags.Add(Enemy.TagEnum.Flying);
            }
            var setglob = GlobalTransform.Origin
            + new Vector3(
                (float)GD.RandRange(-SpawnBoxDimensions.X, SpawnBoxDimensions.X),
                (float)GD.RandRange(-SpawnBoxDimensions.Y, SpawnBoxDimensions.Y),
                (float)GD.RandRange(-SpawnBoxDimensions.Z, SpawnBoxDimensions.Z));
            //EnemyComputeShaderManager.SetEnemyPosition(i, setglob);
            AddSibling(enemy);
            enemy.CallDeferred(MethodName.SetGlobalPosition, setglob);
        }
    }
}
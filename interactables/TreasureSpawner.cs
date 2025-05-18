using Godot;
using System;

public partial class TreasureSpawner : Node3D
{
    private static readonly PackedScene _gem_scene = GD.Load<PackedScene>("res://interactables/gems/gem.tscn");
    private static readonly PackedScene _coin_particles = GD.Load<PackedScene>("res://interactables/coin/coin_spawn_particles.tscn");

    public double SpawnTime = 1.0;

    public bool SpawnTreasure = false;

    public int NumCoins = 10;

    private Timer _t;

    private double _spawned_coins_time;

    private double _secs_per_coin;

    private Vector3 _spawn_position;
    public bool ExplosiveSpawn = false;

    public override void _Ready()
    {
        GlobalPosition = _spawn_position;
        _secs_per_coin = SpawnTime/NumCoins;
        _spawned_coins_time = SpawnTime;
        _t = new Timer
        {
            Autostart = false,
            WaitTime = SpawnTime
        };
        _t.Timeout += QueueFree;
        AddChild(_t);
        _t.Start();
    }

    /// <summary>
    /// Creates a new TreasureSpawner, sets parameters, and returns it.
    /// </summary>
    /// <param name="globalposition"></param>
    /// <param name="numCoins"></param>
    /// <param name="spawnTime"></param>
    /// <returns></returns>
    public static TreasureSpawner Create(Vector3 globalposition, int numCoins = 10, double spawnTime = 1.0, bool explosive_spawn = false)
    {
        var spawner = new TreasureSpawner
        {
            _spawn_position = globalposition,
            NumCoins = numCoins,
            SpawnTime = spawnTime,
            ExplosiveSpawn = explosive_spawn
        };
        return spawner;
    }

    public static void PickupCoinsAtNode(Node3D node, int amount, float lifetime = 3.0f)
    {
        var spawn_particles = _coin_particles.Instantiate<GpuParticles3D>();
        node.GetTree().GetCurrentScene().AddChild(spawn_particles);
        spawn_particles.Amount = amount;
        spawn_particles.Lifetime = lifetime;
        spawn_particles.GlobalPosition = node.GlobalPosition;
        AudioManager.TryPlay(Coin.PickupSound, AudioBus.Misc, node.GlobalPosition);
        Player.AddMoney(amount);
    }

    public void SpawnPickup()
    {
        Coin pickup = _gem_scene.Instantiate<Coin>();
        GetTree().GetCurrentScene().AddChild(pickup);
        pickup.Freeze = true;
        pickup.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
        ApplyInitialConditions(pickup);
        var t = new Timer()
        {
            WaitTime = 0.5,
            OneShot = true,
            Autostart = false
        };
        t.Timeout += () =>
        {
            pickup.SetSpawnPosition(pickup.GlobalPosition);
            t.QueueFree();
        };
        pickup.AddChild(t);
        t.Start();
    }

    public void ApplyInitialConditions(Pickup pickup)
    {
        pickup.SetCollisionLayerValue(1,false);
        pickup.SetCollisionLayerValue(2,false);
        pickup.SetCollisionLayerValue(3,true);
        pickup.SetCollisionMaskValue(1,true);
        pickup.SetCollisionMaskValue(2,false);
        pickup.SetCollisionMaskValue(3,true);
        pickup.SetCollisionMaskValue(9,true);

        Vector3 _linvel = Vector3.Zero, _angvel = Vector3.Zero;
        if (ExplosiveSpawn)
        {
            _linvel = new Vector3(Random.Shared.NextSingle()*4-2,Random.Shared.NextSingle()*2+10,Random.Shared.NextSingle()*4-2);
            _angvel = new Vector3(Random.Shared.NextSingle()*Mathf.Tau, Random.Shared.NextSingle()*Mathf.Tau, Random.Shared.NextSingle()*Mathf.Tau);
        }

        pickup.ForcePhysicsStateUpdate(_spawn_position, _linvel, _angvel);

        pickup.Freeze = false;
        pickup.Visible = true;
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_t.TimeLeft < _spawned_coins_time)
        {
            SpawnPickup();
            _spawned_coins_time -= _secs_per_coin;
        }
    }
}

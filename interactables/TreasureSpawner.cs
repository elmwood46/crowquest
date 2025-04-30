using Godot;
using System;

public partial class TreasureSpawner : Node3D
{
    public static readonly PackedScene HamScene = ResourceLoader.Load<PackedScene>("res://interactables/food/Ham/ham.tscn");
    private static readonly PackedScene _coinScene = GD.Load<PackedScene>("res://interactables/coin/coin.tscn");
    private static readonly PackedScene _coin_particles = GD.Load<PackedScene>("res://interactables/coin/coin_spawn_particles.tscn");

    

    public static readonly RandomNumberGenerator RNG = new();

    public double SpawnTime = 1.0;

    public bool SpawnTreasure = false;

    public int NumCoins = 10;

    private Timer _t;

    private double _spawned_coins_time;

    private double _secs_per_coin;

    private Vector3 _spawn_position;
    private Vector3 _spawn_extents = Vector3.Zero;
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
    public static TreasureSpawner Create(Vector3 globalposition, int numCoins = 10, double spawnTime = 1.0, bool explosive_spawn = false, float box_x = 0, float box_y = 0, float box_z = 0)
    {
        var spawner = new TreasureSpawner
        {
            _spawn_position = globalposition,
            NumCoins = numCoins,
            SpawnTime = spawnTime,
            _spawn_extents = new Vector3(box_x, box_y, box_z),
            ExplosiveSpawn = explosive_spawn
        };
        return spawner;
    }

    public void SetSpawnExtents(Vector3 extents)
    {
        _spawn_extents = extents;
    }

    public void SpawnPickup(Node3D parent = null)
    {
        parent ??= (Node3D)GetTree().GetCurrentScene();
        Pickup pickup;
        if (SpawnTreasure && Random.Shared.NextSingle() < 0.0)
        {
            pickup = HamScene.Instantiate() as Pickup;
            parent.AddChild(pickup);
        }
        else
        {
            pickup = SpawnCoin(parent); //CoinPool.SpawnCoin(parent, _spawn_extents);
        }

        CallDeferred(MethodName.ApplyInitialConditions,pickup);
    }

    public static PackedScene MoneyParticlesScene()
    {
        return _coin_particles;
    }

    public Coin SpawnCoin(Node3D parent = null)
    {
        parent ??= (Node3D)GetTree().GetCurrentScene();
        var coin = (Coin)_coinScene.Instantiate();
        parent.AddChild(coin);
        return coin;
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
            _linvel = new Vector3(RNG.RandfRange(-2.0f,2.0f),RNG.RandfRange(10.0f,12.0f),RNG.RandfRange(-2.0f,2.0f));
            _angvel = new Vector3(RNG.Randf()*2.0f*(float)Math.PI, RNG.Randf()*2.0f*(float)Math.PI, RNG.Randf()*2.0f*(float)Math.PI);
        }

        pickup.Freeze = false;

        var parent = pickup.GetParent() as Node3D;
        var spawn_box = parent.GlobalTransform*new Vector3(
            _spawn_extents.X*(0.5f-Random.Shared.NextSingle()),
            _spawn_extents.Y*(0.5f-Random.Shared.NextSingle()),
            _spawn_extents.Z*(0.5f-Random.Shared.NextSingle())
        );
        GlobalPosition = spawn_box;
        pickup.ForcePhysicsStateUpdate(GlobalPosition, _linvel, _angvel);
        if (pickup is Coin coin) coin.CallDeferred(nameof(coin.Activate));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_t.TimeLeft < _spawned_coins_time)
        {
            SpawnPickup(GetParent() as Node3D);
            _spawned_coins_time -= _secs_per_coin;
        }
    }
}

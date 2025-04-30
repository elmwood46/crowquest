using Godot;
using System;
using System.Collections.Generic;

public partial class CoinPool : Node
{
    private const int MAX_COINS = 300;
    private static readonly Queue<Coin> available = new();
    private static readonly List<Coin> active = [];
    private static readonly PackedScene coinScene = GD.Load<PackedScene>("res://interactables/coin/coin.tscn");

    private static int _spawned_coins = 0;

    public static Coin SpawnCoin(Node3D parent, Vector3 extents)
    {
        Coin coinInstance;

        // Reuse or create a new coin
        if (_spawned_coins == MAX_COINS && available.Count > 0 && IsInstanceValid(available.Peek()))
        {
            if (!IsInstanceValid(available.Peek())) GD.Print("instance peeked in available()_ was invalid");
            coinInstance = available.Dequeue();
            if (coinInstance.GetParent() != parent) coinInstance.Reparent(parent);
            active.Add(coinInstance);
        }
        else if (_spawned_coins == MAX_COINS && IsInstanceValid(active[0]))
        {
            if (!IsInstanceValid(active[0])) GD.Print("instance [0] was invalid");
            coinInstance = active[0];
            active.RemoveAt(0);
            active.Add(coinInstance);
            if (coinInstance.GetParent() != parent) coinInstance.Reparent(parent);
        }
        else
        {
            coinInstance = (Coin)coinScene.Instantiate();
            if (coinInstance.GetParent() != parent) parent.AddChild(coinInstance);
            active.Add(coinInstance);
            _spawned_coins++;
        }

        coinInstance.Freeze = true;
        coinInstance.Visible = false;
        return coinInstance;
    }

    public static void AddToAvailableQueue(Coin coin)
    {
        active.Remove(coin);
        available.Enqueue(coin);
    }
}

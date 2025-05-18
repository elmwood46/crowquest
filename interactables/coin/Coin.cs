using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Coin : Pickup
{

    private static readonly AudioStream[] _coin_fall_sounds =
    [
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_2.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_3.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_4.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_5.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_6.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_7.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_8.ogg"),
        ResourceLoader.Load<AudioStream>("res://audio/coins/coin_fall_9.ogg"),
    ];

    public static readonly AudioStream PickupSound = ResourceLoader.Load<AudioStream>("res://audio/coins/coin_pickup.ogg");

    //private MeshInstance3D _coinMesh;

    public override void _Ready()
    {
        //_coinMesh = GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
        Bus = AudioBus.Coins;
        ImpactSounds = [.. _coin_fall_sounds];
        PickupSounds =
        [
            PickupSound
        ];

        base._Ready();
        _lifetime.Start();
    }



    public override void _PhysicsProcess(double delta)
    {
        if (Engine.GetPhysicsFrames() % 60ul == 0)
        {
            ApplyTorqueImpulse(Vector3.Right);
            ApplyCentralImpulse(Vector3.Up);
        }
        base._PhysicsProcess(delta);
    }

    override public void OnPickup()
    {
        GD.Print("Coin picked up");
        Player.AddXP(1);
        Deactivate();
    }
}

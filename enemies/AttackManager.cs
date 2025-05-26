using Godot;
using System;
using System.Collections.Generic;


public static class AttackManager
{
    public enum DieFaceEnum
    {
        d4 = 4,
        d6 = 6,
        d8 = 8,
        d10 = 10,
        d12 = 12,
        d20 = 20,
    }

    public enum AttackName
    {
        Scratch,
        SpiralBullet,
        StormToss,
        StormLazerAttack,
    }

    public static readonly AudioStream TossEnemyImpactSound = GD.Load("res://enemies/enemy_instances/storm/storm_impact.ogg") as AudioStream;

    public static readonly AudioStream HitSound = GD.Load("res://audio/attacks/hit-sound.ogg") as AudioStream;

    public static readonly PackedScene DamageNumberScene = GD.Load<PackedScene>("res://enemies/damage_number.tscn");

    public static readonly Dictionary<AttackName, Type> AllAttackNamesAndTypes = new()
    {
        {AttackName.Scratch,typeof(ScratchAttack)},
        {AttackName.SpiralBullet,typeof(SpiralBulletAttack)},
        {AttackName.StormToss,typeof(StormTossAttack)},
        {AttackName.StormLazerAttack, typeof(StormLazerAttack)}
    };

    public static int SphereDamageDropoff(Vector3 sphereCentre, Vector3 bodyGlobalPosition, float base_damage, float explosion_radius)
    {
        return Mathf.RoundToInt(base_damage * (1.0f - Mathf.Min(sphereCentre.DistanceSquaredTo(bodyGlobalPosition) / (explosion_radius * explosion_radius), 1f)));
    }

    public static void DamagePopup(int damage_val, Node3D source, Vector3 offset = default, Vector3 targ_scale = default)
    {
        if (targ_scale == default) targ_scale = Vector3.One;
        var dam_pop = DamageNumberScene.Instantiate<DamagePopupText>();
        dam_pop.DamageValue = damage_val;
        dam_pop.TargScale = targ_scale;
        source.GetTree().CurrentScene.AddChild(dam_pop);
        dam_pop.GlobalPosition = source.GlobalPosition + offset;
    }
}
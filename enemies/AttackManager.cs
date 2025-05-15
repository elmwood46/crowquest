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
    }

    public static readonly Dictionary<AttackName, Type> AllAttackNamesAndTypes = new() 
    {
        {AttackName.Scratch,typeof(ScratchAttack)},
        {AttackName.SpiralBullet,typeof(SpiralBulletAttack)}
    };

    public static int SphereDamageDropoff(Vector3 sphereCentre, Vector3 bodyGlobalPosition, float base_damage, float explosion_radius) {
		return Mathf.RoundToInt(base_damage * (1.0f - Mathf.Min(sphereCentre.DistanceSquaredTo(bodyGlobalPosition)/(explosion_radius*explosion_radius),1f)));
	}
}
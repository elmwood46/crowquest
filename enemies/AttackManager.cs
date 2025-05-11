using Godot;
using System;
using System.Collections.Generic;

public static class AttackManager
{
    public static readonly Dictionary<string, Type> AllAttackNamesAndTypes = new() 
    {
        {ScratchAttack.AttackName,typeof(ScratchAttack)},
        {SpiralBulletAttack.AttackName,typeof(SpiralBulletAttack)}
    };

    public static int SphereDamageDropoff(Vector3 sphereCentre, Vector3 bodyGlobalPosition, float base_damage, float explosion_radius) {
		return Mathf.RoundToInt(base_damage * (1.0f - Mathf.Min(sphereCentre.DistanceSquaredTo(bodyGlobalPosition)/(explosion_radius*explosion_radius),1f)));
	}
}
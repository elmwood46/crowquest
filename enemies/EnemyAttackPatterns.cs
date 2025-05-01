using Godot;
using System;

public static class EnemyAttackPatterns
{
    public static void BasicMeleeAttack(Enemy enemy)
    {
        // Do melee attack
    }

    public static void RangedAttack(Enemy enemy)
    {
        // Do ranged attack
    }

    public static void FlyingAttack(Enemy enemy)
    {
        // Do flying attack
    }
    
    public static int SphereDamageDropoff(Vector3 sphereCentre, Vector3 bodyGlobalPosition, float base_damage, float explosion_radius) {
		return Mathf.RoundToInt(base_damage * (1.0f - Mathf.Min(sphereCentre.DistanceSquaredTo(bodyGlobalPosition)/(explosion_radius*explosion_radius),1f)));
	}
}

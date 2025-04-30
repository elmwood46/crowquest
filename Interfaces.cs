using Godot;
using System;

public enum ChestType 
{
    Sarcophagus,
    BasicWooden
}

public enum DamageType
{
    Fire,
    Ice,
    Electric,
    Poison,
    Physical,
    Magic
}

public interface IHurtable
{
	void TakeDamage(int damage, DamageType type);
}

public interface IPickup
{
    abstract public void OnPickup();
}

public interface ISaveStateLoadable
{
    void LoadSavedState();
}
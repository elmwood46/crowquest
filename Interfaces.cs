using Godot;
using System;

public enum ChestType 
{
    Sarcophagus,
    BasicWooden
}

public enum DamageTypeFlagEnum
{
    Physical = 1 << 1,
    Fire = 1 << 2,
    Ice = 1 << 3,
    Electric = 1 << 4,
    Poison = 1 << 5,
    Magic = 1 << 6
}

public interface IHurtable
{
	void TakeDamage(int damage, DamageTypeFlagEnum type);
}

public interface IPickup
{
    abstract public void OnPickup();
}

public interface ISaveStateLoadable
{
    void LoadSavedState();
}

public interface IAttack
{
    public static readonly string AttackName;
    public bool CanMoveDuring {get;}
    public bool CanBeInterrupted {get;}
    public bool IsFinished {get;set;}
    abstract public bool CanTrigger(Enemy enemy);
    abstract public void Execute(Enemy enemy);
    abstract public void Finish(Enemy enemy);
    abstract public void ResetParams();
}
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
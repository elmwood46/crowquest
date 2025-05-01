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
    public bool CanMoveDuring {get;}
    public bool CanBeInterrupted {get;}
    public bool IsFinished {get;set;}
    public abstract bool CanTrigger(Enemy enemy);
    public abstract void Execute(Enemy enemy);
    public abstract void Finish(Enemy enemy);
    public abstract void ResetParams();
}
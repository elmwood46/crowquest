using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class AttackManager : Node
{
    public static AttackManager Instance {get; private set;}
    [Export] public Node3D ScratchHitbox;

    public List<Node3D> AttackHitboxes = new();

    public override void _Ready()
    {
        // disable the hitbox
        Instance = this;
        AttackHitboxes.Add(ScratchHitbox);
        AddChild(ScratchHitbox);
        ((Area3D)ScratchHitbox.GetChild(0)).ProcessMode = ProcessModeEnum.Disabled;  
    }

    public override void _PhysicsProcess(double delta)
    {
        foreach (Node3D hitbox in AttackHitboxes)
        {
            if (hitbox.ProcessMode == ProcessModeEnum.Pausable)
            {
                Area3D area = (Area3D)hitbox.GetChild(0);
                foreach (var pb in area.GetOverlappingBodies())
                {
                    if (pb is IHurtable hurtable)
                    {
                        hurtable.TakeDamage(5,DamageType.Physical);
                    }
                }
            }
        }
    }
}
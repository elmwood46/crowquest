using Godot;
using System;

[Tool]
public partial class brick_gen_test : Path3D
{
    [Export] public bool ReInitButton
    {
        get => false;
        set
        {
            if (value)
            {
                if (Engine.IsEditorHint() && IsInsideTree())
                {
                    GenerateBricks();
                }
            }
        }
    }

    [Export] public int WallRows = 10;

    [Export] public PackedScene BrickPrefab { get; set; }

    public void GenerateBricks()
    {
        foreach (Node n in GetChildren())
        {
            n.QueueFree();
        }

        var points = Curve.GetBakedPoints();
        for (int i=0; i<points.Length-1;i++)
        {
            var point = points[i];
            var brick_wall = BrickPrefab.Instantiate<MultiBrick>();
            AddChild(brick_wall);
            if (Engine.IsEditorHint()) brick_wall.Owner = GetTree().EditedSceneRoot;
            brick_wall.GlobalPosition = point;
            var step = points[i + 1] - point;
            brick_wall.DesiredLength = step.Length();
            brick_wall.WallBrickRows = WallRows;
            brick_wall.MultiBrickInit();
            brick_wall.LookAt(points[i + 1], Vector3.Up);
        }
    }
}

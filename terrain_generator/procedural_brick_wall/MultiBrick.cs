using Godot;
using System;

[Tool]
public partial class MultiBrick : StaticBody3D
{
    private enum BrickSurface
    {
        face_sides,
        gutters_bottom,
        gutters_front,
        gutters_back,
        gutters_top,
        face_bottom,
        face_top,
    }

    private static readonly PackedScene BRICK_MESH_SCN = ResourceLoader.Load<PackedScene>("res://terrain_generator/brick_mesh.tscn"); 
    private static readonly ShaderMaterial GUTTERS_SHADER = ResourceLoader.Load<ShaderMaterial>("res://terrain_generator/brick_shader_gutters.tres");
    private static readonly ShaderMaterial BRICK_SHADER = ResourceLoader.Load<ShaderMaterial>("res://terrain_generator/brick_shader.tres");
    private static readonly Vector3 BRICK_SIZE = new(0.32f, 0.4f, 0.8f); 
    private const int MAX_BRICKS_IN_ROW = 100;

    [Export] bool ReInitButton {
        get => false;
        set 
        {
            if (value)
            {   
                if (Engine.IsEditorHint() && IsInsideTree())
                {
                    MultiBrickInit();
                } 
            }
        }
    }

    [Export] public float BrickMinMult = 0.5f;
    [Export] public float BrickMaxMult = 1.3f;
    [Export] public float DesiredLength = 10.0f;
    [Export] public int WallBrickRows = 5;

    public void MultiBrickInit()
    {
        var container_node = GetNode("MultiBrickBatch") as Node3D;
        if (Engine.IsEditorHint()) container_node.Owner = GetTree().EditedSceneRoot;
        foreach (Node n in container_node.GetChildren())
        {
            n.QueueFree();
        }

        var collision_shape = GetNode("CollisionShape3D") as CollisionShape3D;
        collision_shape.Shape = new BoxShape3D()
        {
            Size = new Vector3(BRICK_SIZE.X, BRICK_SIZE.Y * WallBrickRows, DesiredLength)
        };

        collision_shape.Transform = new Transform3D(Basis.Identity, new Vector3(0, BRICK_SIZE.Y/2 * WallBrickRows, -DesiredLength/2));
        if (Engine.IsEditorHint()) collision_shape.Owner = GetTree().EditedSceneRoot;

        CallDeferred(nameof(GenerateBricks));
    }

    public void GenerateBricks()
    {
        var container_node = GetNode("MultiBrickBatch") as Node3D;
        var max_brick_size = BRICK_SIZE.Z * BrickMaxMult;

        // make a deep copy of the mesh and shaders to use with each wall (otherwise buffer runs out)
        var brick_shader_copy = BRICK_SHADER.Duplicate() as ShaderMaterial;
        var gutters_shader_copy = GUTTERS_SHADER.Duplicate() as ShaderMaterial;
        var brick_scene = BRICK_MESH_SCN.Instantiate()as MeshInstance3D;
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.face_sides, brick_shader_copy);
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_bottom, gutters_shader_copy);
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_front, gutters_shader_copy);
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_back, gutters_shader_copy);
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_top, gutters_shader_copy);
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.face_bottom, brick_shader_copy);
        brick_scene.SetSurfaceOverrideMaterial((int)BrickSurface.face_top, brick_shader_copy);

        for (int row=0;row<WallBrickRows;row++)
        {
            var last_brick_center = 0.0f;
            var last_brick_size = 0.0f;
            var total_length = 0.0f;
            var queue_last = false;
            var curr_row = new Node3D
            {
                Name = $"Row{row}"
            };
            container_node.AddChild(curr_row);
            if (Engine.IsEditorHint()) curr_row.Owner = GetTree().EditedSceneRoot;

            for (int i=0; i < MAX_BRICKS_IN_ROW; i++)
            {
                var t = new Transform3D();

                var mesh = brick_scene.Duplicate() as MeshInstance3D;
                mesh.Name = $"Brick{i}";

                var brick_size= Random.Shared.NextSingle() * (BrickMaxMult - BrickMinMult) + BrickMinMult;
                brick_size *= BRICK_SIZE.Z;
                
                if (total_length >= DesiredLength - max_brick_size)
                {
                    brick_size = DesiredLength - total_length;
                    queue_last = true;
                }

                if (row == WallBrickRows - 1)
                {
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.face_bottom, gutters_shader_copy);
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_top, brick_shader_copy);
                }
                else if (row == 0)
                {
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.face_top, gutters_shader_copy);
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_bottom, brick_shader_copy);
                }
                else
                {
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.face_top, gutters_shader_copy);
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.face_bottom, gutters_shader_copy);
                }

                if (i==0)
                {
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_back, brick_shader_copy);
                }
                else if (queue_last)
                {
                    mesh.SetSurfaceOverrideMaterial((int)BrickSurface.gutters_front, brick_shader_copy);
                }

                mesh.SetInstanceShaderParameter("brick_size", brick_size);
                mesh.SetInstanceShaderParameter("brick_color", new Vector3(0.8f, 0.1f+Random.Shared.NextSingle()*0.05f, 0.05f+Random.Shared.NextSingle()*0.05f));


                var brick_center = last_brick_center + (last_brick_size/2.0f) + (brick_size / 2.0f);

                t.Origin = new Vector3(0, BRICK_SIZE.Y/2 + row * BRICK_SIZE.Y, -brick_center);
                t.Basis = Basis.Identity;

                last_brick_center = brick_center;
                last_brick_size = brick_size;
                total_length += brick_size;

                mesh.GlobalTransform = t;
                curr_row.AddChild(mesh);
                if (Engine.IsEditorHint()) mesh.Owner = GetTree().EditedSceneRoot;

                if (queue_last) break;
            }
        }
    }
}

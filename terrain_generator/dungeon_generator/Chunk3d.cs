using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

[Tool]
public partial class Chunk3d : Node3D
{
    private Vector3I _prev_pos = Vector3I.Zero;

    [ExportToolButton("TestGenChunk")] public Callable ButtonGenerateChunk => new(this, nameof(TestGenerateChunk));

    public void TestGenerateChunk()
    {
        var bm = GetNode<BlockManager>("../BlockManager");
        bm._Ready();

        GreedyMesher.ClearBlockCache();

        foreach (var child in GetChildren()) child.QueueFree();

        var chunk = (Vector3I)GlobalPosition;
        GreedyMesher.GenBlocks(chunk, new FastNoiseLite());
        var meshData = GreedyMesher.BuildChunkMesh(chunk);
        var meshInstance = new MeshInstance3D
        {
            Mesh = meshData.GetUnifiedSurfaces(),
        };
        AddChild(meshData.StaticBody);
        AddChild(meshInstance);
        GD.Print($"Num Collision shapes: {meshData.StaticBody.GetChildren().OfType<CollisionShape3D>().Count()}");
        GD.Print($"Num test meshes: {meshData.StaticBody.GetChildren().OfType<MeshInstance3D>().Count()}");
    }

    // testing
    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint()) return;
        if ((Vector3I)GlobalPosition != _prev_pos)
        {
            _prev_pos = (Vector3I)GlobalPosition;
            TestGenerateChunk();
        }
    }
}
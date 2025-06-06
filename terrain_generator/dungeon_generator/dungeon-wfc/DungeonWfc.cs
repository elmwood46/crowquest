using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

[Tool]
[GlobalClass]
public partial class DungeonWfc : Node3D
{
    [Signal] public delegate void FinishedLoadingEventHandler();
    [Signal] public delegate void StartedLoadingEventHandler();

    [ExportToolButton("Generate Base Chunk")] public Callable ButtonGenerateBaseChunk => new(this, nameof(MeshBaseChunkAndSetDoors));
    //[ExportToolButton("Match Chunk Size To Base")] public Callable ButtonForceChunkSize =>  Callable.From(() => { ChunkSize = GreedyMesher.CHUNK_SIZE; });
    [Export] public BlockManager BlockManager;

    [Export] public int ChunkSize
    {
        get => _chunk_size;
        set
        {
            _chunk_size = GreedyMesher.CHUNK_SIZE;
            PaddedChunkSize = GreedyMesher.CSP;
            Csp2 = GreedyMesher.CSP2;
            Csp3 = GreedyMesher.CSP3;
            BaseChunk = new uint[Csp3];
        }
    }
    private int _chunk_size = 16;
    public int PaddedChunkSize { get; set; } = 16 + 2; // size of the chunk in blocks with padding for meshing
    public int Csp2 { get; set; } = 16 + 2; // size of the chunk in blocks with padding for csp
    public int Csp3 { get; set; } = 16 + 2; // size of the chunk in blocks with padding for csp

    public uint[] BaseChunk { get; set; }

    private const float _HALF_PI = Mathf.Pi / 2f;

    public static readonly List<Vector3I> CreatedRoomsXYZPositions = [Vector3I.Zero];

    [Export] public PackedScene DoorScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/dungeon-wfc/Door/door.tscn");
    public static readonly PackedScene DungeonWfcScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/dungeon-wfc/dungeon_wfc_test.tscn");
    public static readonly PackedScene BlockManagerScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/block_manager.tscn");
    public static readonly PackedScene PhysTrackerScene = GD.Load<PackedScene>("res://enemies/bullets/PhysBodyTracker.tscn");

    public static readonly Vector3[] DoorNormals =
    [
        new Vector3(0, 0, -1), // -z
        new Vector3(-1, 0, 0), // -x
        new Vector3(0, 0, 1), // +z
        new Vector3(1, 0, 0), // +x
    ];

    public DungeonWfc()
    {
        ChunkSize = GreedyMesher.CHUNK_SIZE;
        PaddedChunkSize = GreedyMesher.CSP;
        Csp2 = GreedyMesher.CSP2;
        Csp3 = GreedyMesher.CSP3;
        BaseChunk = new uint[Csp3];
    }

    public override void _Ready()
    {
        SetupDoorLambdaFunctions();
    }

    public void GenerateBaseChunk()
    {
        for (var x = 0; x < ChunkSize; x++)
        {
            for (var y = 0; y < ChunkSize; y++)
            {
                for (var z = 0; z < ChunkSize; z++)
                {
                    var pos = new Vector3I(x, y, z) + Vector3I.One;
                    var idx = GreedyMesher.BlockIndex(pos);
                    BaseChunk[idx] = 0u; // Initialize with a default block ID (e.g., 0 for air)

                    // fill in main block and add spots for doors
                    if (y < ChunkSize / 2)
                    {
                        if (x > 0 && x < ChunkSize - 1 && z > 0 && z < ChunkSize - 1) BaseChunk[idx] = 1u; // Example: 1 for a solid block
                        else if ((x == 0 || x == ChunkSize - 1) && (z == ChunkSize / 2 || z == ChunkSize / 2 - 1)) BaseChunk[idx] = 1u;
                        else if ((z == 0 || z == ChunkSize - 1) && (x == ChunkSize / 2 || x == ChunkSize / 2 - 1)) BaseChunk[idx] = 1u;
                    }
                }
            }
        }
    }

    public void MeshBaseChunkAndSetDoors()
    {
        var scene = GetTree().EditedSceneRoot;

        GenerateBaseChunk();

        foreach (var child in GetChildren()) child.QueueFree();

        if (Engine.IsEditorHint())
        {
            if (BlockManager == null)
            {
                BlockManager = BlockManagerScene.Instantiate<BlockManager>();
                AddChild(BlockManager);
                BlockManager.Owner = scene;
            }
            BlockManager._Ready();
        }

        GreedyMesher.ClearBlockCache();

        foreach (var child in GetChildren()) child.QueueFree();

        var chunk = (Vector3I)new Vector3(GlobalPosition.X, 0, GlobalPosition.Z);

        GreedyMesher.SetBlocks(chunk, BaseChunk);

        var sw = new Stopwatch();
        sw.Start();
        var meshData = GreedyMesher.BuildChunkMesh(chunk);
        sw.Stop();
        //GD.Print($"Chunk mesh generation took {sw.ElapsedMilliseconds} ms for chunk {chunk}");

        var meshInstance = new MeshInstance3D
        {
            Mesh = meshData.GetUnifiedSurfaces(),
        };

        AddChild(meshData.StaticBody);
        AddChild(meshInstance);
        if (Engine.IsEditorHint())
        {
            meshData.StaticBody.Owner = scene;
            meshInstance.Owner = scene;
            foreach (var shape in meshData.StaticBody.GetChildren()) shape.Owner = scene;
            var phys_tracker = PhysTrackerScene.Instantiate<PhysBodyTracker>();
            meshData.StaticBody.AddChild(phys_tracker);
            phys_tracker.Owner = scene;
        }

        var doornode = new Node3D
        {
            Name = "Doors",
        };
        AddChild(doornode);
        if (Engine.IsEditorHint()) doornode.Owner = scene;
        doornode.Name = "Doors";

        for (int i = 0; i < DoorNormals.Length; i++)
        {
            var door = DoorScene.Instantiate<Door>();
            door.Basis = Basis.Identity.Rotated(Vector3.Up, i * _HALF_PI);
            door.Position = Vector3.One * ChunkSize / 2 + new Vector3(DoorNormals[i].X * ChunkSize / 2, 0, DoorNormals[i].Z * ChunkSize / 2);
            doornode.AddChild(door);
            if (Engine.IsEditorHint()) door.Owner = scene;
            door.Name = $"Door_{i}";
        }
    }

    private void SetupDoorLambdaFunctions()
    {
        var door_node = GetNodeOrNull<Node3D>("Doors");
        if (door_node != null)
        {
            var door_array = door_node.GetChildren().OfType<Door>().ToArray();
            //GD.Print($"Found {door_array.Length} doors in DungeonWfc.");
            for (int i = 0; i < door_array.Length; i++)
            {
                var door = door_array[i];
                var door_normal = DoorNormals[i];
                var local_idx = i;

                //GD.Print($"Setting up door {i} at position {door_array[i].GlobalPosition} with normal {DoorNormals[i]}");
                // opening the door creates a new room !
                door.GenerateOnOpen += () =>
                {
                    //GD.Print($"Door {door.Name} opened, generating next room...");
                    var nextRoom = DungeonWfcScene.Instantiate<DungeonWfc>();

                    var next_pos = GlobalPosition + door_normal * ChunkSize;

                    if (CreatedRoomsXYZPositions.Contains(RoomToChunkPos(next_pos)))
                    {
                        //GD.Print($"Room at position {next_pos} already exists, skipping generation.");
                        return;
                    }

                    var doors_to_skip = new bool[4];

                    for (var neighbor_idx = 0; neighbor_idx < 4; neighbor_idx++)
                    {
                        var neighbor_chunk_pos = RoomToChunkPos(next_pos + DoorNormals[neighbor_idx] * ChunkSize);
                        doors_to_skip[neighbor_idx] = CreatedRoomsXYZPositions.Contains(neighbor_chunk_pos);
                    }

                    AddSibling(nextRoom);
                    CreatedRoomsXYZPositions.Add(RoomToChunkPos(next_pos));
                    nextRoom.GlobalPosition = next_pos + Vector3.Down * ChunkSize * 2; // set spawn position
                    nextRoom.EmitSignal(SignalName.StartedLoading);

                    // generate next room
                    // nextRoom.MeshBaseChunkAndSetDoors();
                    // nextRoom.SetupDoorLambdaFunctions();

                    // remove the appropriate door in the next room
                    var next_doors = nextRoom.GetNode<Node3D>("Doors").GetChildren().OfType<Door>().ToArray();
                    for (var skip_idx = 0; skip_idx < doors_to_skip.Length; skip_idx++) if (doors_to_skip[skip_idx]) next_doors[skip_idx].QueueFree();

                    //next_doors[(local_idx + DoorNormals.Length / 2) % DoorNormals.Length].QueueFree();

                    // tween next room upwards
                    var tween = nextRoom.CreateTween();
                    tween.TweenProperty(nextRoom, "global_position", next_pos, 1.0)
                        .SetEase(Tween.EaseType.InOut)
                        .SetTrans(Tween.TransitionType.Bounce);
                    tween.Finished += () =>
                    {
                        //GD.Print($"Room at position {nextRoom.GlobalPosition} generated.");
                        tween.Kill();
                        nextRoom.EmitSignal(SignalName.FinishedLoading);
                    };
                };
            }
        }
    }

    private Vector3I RoomToChunkPos(Vector3 room_pos)
    {
        return new Vector3I(Mathf.FloorToInt(room_pos.X / ChunkSize), Mathf.FloorToInt(room_pos.Y / ChunkSize), Mathf.FloorToInt(room_pos.Z / ChunkSize));
    }
}

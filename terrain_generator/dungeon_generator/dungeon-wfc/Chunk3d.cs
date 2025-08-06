using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

[Tool]
[GlobalClass]
public partial class Chunk3d : Node3D
{
    [Signal] public delegate void FinishedLoadingEventHandler();
    [Signal] public delegate void StartedLoadingEventHandler();

    [ExportToolButton("Generate Base Chunk")] public Callable ButtonGenerateBaseChunk => new(this, nameof(MeshBaseChunkAndSetDoors));
    //[ExportToolButton("Match Chunk Size To Base")] public Callable ButtonForceChunkSize =>  Callable.From(() => { ChunkSize = GreedyMesher.CHUNK_SIZE; });
    [Export] public BlockManager LocalBlockManager;
    [Export] public int BaseChunkHeight = 1;
    [Export] public bool BaseChunkHasRails = false;
    [Export] public bool DisableSarcophagusGeneration = false;

    private static readonly Noise NoiseTexture = new FastNoiseLite();
    public static bool IsFirstChunk = true; // used to determine if this is the starting chunk, which has special rules for treasure generation

    public MultiChunk3d ParentMultiChunk { get; set; } // reference to the parent MultiChunk3d node, if this chunk is part of a MultiChunk3d

    // ==================================================================
    // ====== Treasure ========
    // ==================================================================
    private static readonly HashSet<Vector3> _opened_chests = []; // keep track of which chests have been opened
    private const int TREASURE_CHEST_REROLLS = 3;
    private static readonly PackedScene[] _treasure_chests = // these are ordered by ascending rarity, when a chest is generated the list is rerolled TREASURE_CHEST_REROLLS times "with disadvantage"
    [
        GD.Load<PackedScene>("res://interactables/chest_scenes/big_chest.tscn"), // least rare
    ];
    private static readonly PackedScene _sarco_scene = GD.Load<PackedScene>("res://interactables/chest_scenes/stone_sarcophagus.tscn");

    private static readonly PackedScene[] _tree_scenes =
    [
        //GD.Load<PackedScene>("res://environment_models/halloween/scenes/tree.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/coniferous_tree.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/red_tree_1.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/red_tree_2.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/red_tree_3.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/red_tree_1.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/red_tree_2.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/red_tree_3.tscn"),
        GD.Load<PackedScene>("res://textures/ramperk/mushroom_1.tscn")
    ];

    // ==================================================================
    // ====== Particle Effects ========
    // ==================================================================
    private static readonly PackedScene _firefly_particle_scene = GD.Load<PackedScene>("res://effects/gpu_particle_fireflies.tscn");
    private static readonly PackedScene _butterfly_particle_scene = GD.Load<PackedScene>("res://effects/butterfly/butterfly_particles.tscn");

    private static readonly HashSet<Vector3I> _track_spawned_enemies = []; // keep track of chunks where enemies already spawned
    [Export] public Godot.Collections.Dictionary<int, Door> Doors = [];
    public NavigationRegion3D NavRegion = null;

    [Export]
    public int ChunkSize
    {
        get => _chunk_size;
        set
        {
            _chunk_size = value;
            PaddedChunkSize = _chunk_size + 2;
            Csp2 = PaddedChunkSize*PaddedChunkSize;
            Csp3 = Csp2*PaddedChunkSize;
            BaseChunk = new uint[Csp3];
        }
    }
    private int _chunk_size = 16;
    public int PaddedChunkSize { get; set; } = 16 + 2; // size of the chunk in blocks with padding for meshing
    public int Csp2 { get; set; } = 16 + 2; // size of the chunk in blocks with padding for csp
    public int Csp3 { get; set; } = 16 + 2; // size of the chunk in blocks with padding for csp

    public uint[] BaseChunk { get; set; }

    private const float _HALF_PI = Mathf.Pi / 2f;

    public static readonly Dictionary<Vector3I, Chunk3d> CreatedChunks = [];

    [Export] public PackedScene DoorScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/dungeon-wfc/Door/door.tscn");
    public static readonly PackedScene Chunk3dScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/dungeon-wfc/chunk3d.tscn");
    public static readonly PackedScene BlockManagerScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/block_manager.tscn");
    public static readonly PackedScene PhysTrackerScene = GD.Load<PackedScene>("res://enemies/bullets/PhysBodyTracker.tscn");
    public static readonly PackedScene EnemySpawnZoneScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/enemy_spawn_zone.tscn");
    private static readonly PackedScene FencePostScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/dungeon-wfc/fence/fence_post.tscn");
    private static readonly PackedScene FenceMiddleScene = GD.Load<PackedScene>("res://terrain_generator/dungeon_generator/dungeon-wfc/fence/fence_middle.tscn");

    public static readonly Vector3[] DoorNormals =
    [
        new Vector3(0, 0, -1), // -z
        new Vector3(-1, 0, 0), // -x
        new Vector3(0, 0, 1), // +z
        new Vector3(1, 0, 0), // +x
    ];

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;

        SetupDoorLambdaFunctions();

        if (IsFirstChunk)
        {
            CreatedChunks.TryAdd(Vector3I.Zero, this); // add the starting chunk to the created rooms
            IsFirstChunk = false;
        }

        if (NavRegion == null)
        {
            var nav_node = GetNodeOrNull<NavigationRegion3D>("NavRegion");
            if (nav_node != null)
            {
                NavRegion = nav_node;
            }
            else if (!Engine.IsEditorHint()) throw new Exception($"NavRegion not found in Chunk3d {this}. Please ensure it is present in the scene.");
        }

        var fence_node = GetNodeOrNull<Node3D>("NavRegion/Fence");
        foreach (var child in fence_node.GetChildren(true))
        {
            if (child is StaticBody3D)
            {
                var pb = PhysTrackerScene.Instantiate<PhysBodyTracker>();
                child.AddChild(pb);
                FinishedLoading += pb.UpdateThisTrackerInGrid;
            }
        }
    }

    private static void BlockIndex(int CSP, Vector3I pos, out int idx)
    {
        // Calculate the index in the BaseChunk array based on the position
        idx = pos.X + pos.Y * CSP * CSP + pos.Z * CSP;
    }

    public void GenerateBaseChunk()
    {
        if (!Engine.IsEditorHint()) return;

        var glob_chunk_pos = (Vector3I)GlobalPosition;
        var chunk_odd_sized = ChunkSize % 2 == 1;

        var fence_node = GetNodeOrNull<Node3D>("Fence");
        if (fence_node == null)
        {
            fence_node = new Node3D() { Name = "Fence" };
            AddChild(fence_node);
            if (Engine.IsEditorHint()) fence_node.Owner = GetTree().EditedSceneRoot;
            fence_node.Name = "Fence";
        }

        for (var x = 0; x < ChunkSize; x++)
        {
            for (var y = 0; y < ChunkSize; y++)
            {
                for (var z = 0; z < ChunkSize; z++)
                {
                    var pos = new Vector3I(x, y, z) + Vector3I.One;
                    BlockIndex(PaddedChunkSize, pos, out var idx);
                    BaseChunk[idx] = 0u; // Initialize with a default block ID (e.g., 0 for air)

                    var chess_block_id = Mathf.RoundToInt(Vector3.One.Dot(glob_chunk_pos.Abs() + pos.Abs())) % 2 == 0 ? BlockManager.ChessBlackBlockId : BlockManager.ChessWhiteBlockId;

                    // fill in main block and add spots for doors
                    if (y < BaseChunkHeight)
                    {
                        if (x > 0 && x < ChunkSize - 1 && z > 0 && z < ChunkSize - 1) BaseChunk[idx] = chess_block_id; // main block
                        else
                        { // door outposts
                            if (chunk_odd_sized)
                            {
                                if ((x == 0 || x == ChunkSize - 1) && (z == ChunkSize / 2 || z == ChunkSize / 2 - 1 || z == ChunkSize / 2 + 1)) BaseChunk[idx] = chess_block_id;
                                else if ((z == 0 || z == ChunkSize - 1) && (x == ChunkSize / 2 || x == ChunkSize / 2 - 1 || x == ChunkSize / 2 + 1)) BaseChunk[idx] = chess_block_id;
                            }
                            else
                            {
                                if ((x == 0 || x == ChunkSize - 1) && (z == ChunkSize / 2 || z == ChunkSize / 2 - 1)) BaseChunk[idx] = chess_block_id;
                                else if ((z == 0 || z == ChunkSize - 1) && (x == ChunkSize / 2 || x == ChunkSize / 2 - 1)) BaseChunk[idx] = chess_block_id;
                            }
                        }

                    }

                    if (BaseChunkHasRails)
                    {
                        // add door rails
                        if (y <= BaseChunkHeight && (x == 0 || x == ChunkSize - 1 || z == 0 || z == ChunkSize - 1))
                        {
                            if (chunk_odd_sized)
                            {
                                if ((x == ChunkSize / 2 + 2 || x == ChunkSize / 2 - 2) || (z == ChunkSize / 2 + 2 || z == ChunkSize / 2 - 2)) BaseChunk[idx] = chess_block_id;
                            }
                            else
                            {
                                if ((x == ChunkSize / 2 + 1 || x == ChunkSize / 2 - 2) || (z == ChunkSize / 2 + 1 || z == ChunkSize / 2 - 2)) BaseChunk[idx] = chess_block_id;
                            }
                        }

                        // add walls leaving gaps for doors
                        else if (y == BaseChunkHeight && x > 0 && x < ChunkSize - 1 && z > 0 && z < ChunkSize - 1
                            && (x == 1 || x == ChunkSize - 2 || z == 1 || z == ChunkSize - 2))
                        {
                            if (x==z || (z == ChunkSize - 2 && x == 1) || (x == ChunkSize - 2 && z == 1)) // corner piece fence post and walls
                            {
                                var fence_post = FencePostScene.Instantiate<StaticBody3D>();
                                var child_idx = 0;
                                foreach (var nodechild in fence_post.GetChildren())
                                {
                                    nodechild.Name = $"FencePost_{x}_{z}_Child_{child_idx++}";
                                }
                                fence_post.Position = new Vector3(x + 0.5f, y, z + 0.5f);
                                fence_post.Name = $"FencePost_{x}_{z}";
                                fence_node.AddChild(fence_post);

                                var adjusted_scale = new Vector3(1, 1, (ChunkSize - 2) / 2 - 2f - 0.1f);

                                var middle_1 = FenceMiddleScene.Instantiate<StaticBody3D>();
                                middle_1.GetChild<MeshInstance3D>(0).Scale = adjusted_scale;
                                foreach (var collision_shape in middle_1.GetChildren().OfType<CollisionShape3D>())
                                {
                                    collision_shape.Shape = collision_shape.Shape.Duplicate() as BoxShape3D;
                                    ((BoxShape3D)collision_shape.Shape).Size = new Vector3(0.2f,0.2f,adjusted_scale.Z);
                                    collision_shape.Position = new Vector3(collision_shape.Position.X,collision_shape.Position.Y,-adjusted_scale.Z / 2f);
                                }
                                foreach (var mesh_instance in middle_1.GetChildren().OfType<MeshInstance3D>())
                                {
                                    mesh_instance.Mesh = mesh_instance.Mesh.Duplicate(true) as ArrayMesh;
                                }
                                var middle_2 = middle_1.Duplicate() as StaticBody3D;
                                middle_2.Name = "FenceMiddle2";

                                fence_post.AddChild(middle_1);
                                fence_post.AddChild(middle_2);
                                middle_1.Position = new Vector3(0, 0, -0.1f);
                                middle_2.Position = new Vector3(0, 0, (chunk_odd_sized ? -2f : -1f) - (ChunkSize - 2) / 2);
                                var sub_post = FencePostScene.Instantiate<StaticBody3D>();
                                sub_post.Name = "FenceSubPost1";
                                sub_post.Position = middle_1.Position + middle_1.GetChild<MeshInstance3D>(0).Scale * Vector3.Forward;
                                var sub_post2 = sub_post.Duplicate() as StaticBody3D;
                                sub_post2.Name = "FenceSubPost2";
                                sub_post2.Position += Vector3.Forward * (chunk_odd_sized ? 4f : 3f);
                                fence_post.AddChild(sub_post);
                                fence_post.AddChild(sub_post2);

                                var val = (x, z);
                                if (val == (1, 1)) fence_post.Basis = Basis.Identity.Rotated(Vector3.Up, -Mathf.Pi / 2);
                                else if (val == (1, ChunkSize - 2)) fence_post.Basis = Basis.Identity;
                                else if (val == (ChunkSize - 2, ChunkSize - 2)) fence_post.Basis = Basis.Identity.Rotated(Vector3.Up, Mathf.Pi / 2);
                                else if (val == (ChunkSize - 2, 1)) fence_post.Basis = Basis.Identity.Rotated(Vector3.Up, -Mathf.Pi);
                                fence_post.Owner = GetTree().EditedSceneRoot;
                                foreach (var child in fence_post.GetChildren())
                                {
                                    if (child is StaticBody3D static_body)
                                    {
                                        static_body.Owner = GetTree().EditedSceneRoot;
                                        static_body.Name = $"FencePost_{x}_{z}_StaticBody";
                                        child_idx = 0;
                                        foreach (var shape in static_body.GetChildren())
                                        {
                                            shape.Owner = GetTree().EditedSceneRoot;
                                            shape.Name = $"FencePost_{x}_{z}_Collision_{child_idx++}";
                                        }
                                    }
                                    else child.Owner = GetTree().EditedSceneRoot;
                                }
                            }

                            if (chunk_odd_sized)
                            {
                                if (!(x == ChunkSize / 2 || x == ChunkSize / 2 - 1 || x == ChunkSize / 2 + 1) && !(z == ChunkSize / 2 || z == ChunkSize / 2 - 1 || z == ChunkSize / 2 + 1))
                                {
                                    BaseChunk[idx] = 0u; // Set to a solid block ID for walls
                                }
                            }
                            else
                            {
                                if (!(x == ChunkSize / 2 || x == ChunkSize / 2 - 1) && !(z == ChunkSize / 2 || z == ChunkSize / 2 - 1))
                                {
                                    BaseChunk[idx] = 0u; // Set to a solid block ID for walls
                                }
                            }
                        }

                        // make railings not chequered
                        if (y == BaseChunkHeight && BaseChunk[idx] != 0u)
                        {
                            BaseChunk[idx] = BlockManager.ChessRedBlockId;
                        }
                    }
                }
            }
        }
    }

    public void MeshBaseChunkAndSetDoors()
    {
        if (!Engine.IsEditorHint()) return;

        var scene = GetTree().EditedSceneRoot;

        foreach (var child in GetChildren()) child.QueueFree();

        GenerateBaseChunk();

        if (Engine.IsEditorHint())
        {
            LocalBlockManager = BlockManagerScene.Instantiate<BlockManager>();
            LocalBlockManager.Name = "BlockManager" + GetHashCode().ToString();
            LocalBlockManager.SetupBlockManager();
        }

        var sw = new Stopwatch();
        sw.Start();
        var meshData = GreedyMesher.BuildChunkMesh(ChunkSize, BaseChunk);
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
        Doors.Clear();
        for (int i = 0; i < DoorNormals.Length; i++)
        {
            var door = DoorScene.Instantiate<Door>();
            door.Basis = Basis.Identity.Rotated(Vector3.Up, i * _HALF_PI);
            door.Position = new Vector3(1, 0, 1) * ChunkSize / 2 + Vector3.Up * BaseChunkHeight + new Vector3(DoorNormals[i].X * ChunkSize / 2, 0, DoorNormals[i].Z * ChunkSize / 2);
            doornode.AddChild(door);
            if (Engine.IsEditorHint()) door.Owner = scene;
            door.Name = $"Door_{i}";
            Doors[i] = door;
        }

        var navnode = new NavigationRegion3D()
        {
            NavigationMesh = new NavigationMesh()
        };
        AddChild(navnode);
        if (Engine.IsEditorHint()) navnode.Owner = scene;
        navnode.Name = "NavRegion";
        NavRegion = navnode;

        var fence_node = GetNodeOrNull<Node3D>("Fence");
        fence_node?.Reparent(NavRegion);

        foreach (var c in GetChildren())
        {
            c.SetDisplayFolded(true);
            if (c.Owner != scene)
            {
                c.QueueFree();
            }
        }
    }

    /// <summary>
    /// Spawns features and returns the node containing them.
    /// </summary>
    /// <returns></returns>
    public Node3D AddFeatures()
    {
        var features_container = new Node3D
        {
            Name = "Features",
        };
        NavRegion.AddChild(features_container);

        static void add_chest(Node3D treasure_chest_parent_node, TreasureChest chest, Transform3D transform)
        {
            chest.Transform = transform;
            if (chest.Type != ChestType.BasicWooden) chest.AddChild(PhysTrackerScene.Instantiate<PhysBodyTracker>());
            treasure_chest_parent_node.AddChild(chest);
            if (_opened_chests.Contains(chest.GlobalPosition)) chest.ForceStateOpen();
        }

        var chunk_pos = RoomToChunkPos(GlobalPosition);
        var chunk_offset = ChunkSize * chunk_pos;
        var centerPoint = chunk_offset + new Vector3(ChunkSize / 2, BaseChunkHeight, ChunkSize / 2);
        var is_starting_chunk = chunk_pos == Vector3I.Zero;

        var seeded_random = NoiseTexture.GetNoise2D(chunk_pos.X * ChunkSize, chunk_pos.Z * ChunkSize);
        var chunk_rng = new Random((int)ChunkCantorNumber(chunk_pos * Mathf.RoundToInt(seeded_random * 1000)));
        seeded_random = seeded_random * 0.5f + 0.5f; // remap to 0-1
        var chunk_already_spawned_enemies = _track_spawned_enemies.Contains(chunk_pos);

        // add enemy spawn triggers
        var should_spawn_enemies = !chunk_already_spawned_enemies && !is_starting_chunk && seeded_random < 1.0f;
        if (should_spawn_enemies)
        {
            var _enemy_spawn_trigger = new Area3D
            {
                Name = "EnemySpawnTrigger" + GetHashCode().ToString(),
            };
            var shape = new CollisionShape3D()
            {
                Shape = new BoxShape3D
                {
                    Size = Vector3.One * ChunkSize * 0.75f
                }
            };
            _enemy_spawn_trigger.AddChild(shape);
            _enemy_spawn_trigger.BodyEntered += (body) =>
            {
                if (body is Player player)
                {
                    // spawn enemies
                    foreach (var (_, door) in Doors)
                    {
                        door.LockDoor();
                    }

                    var spawn_delay = new Timer()
                    {
                        WaitTime = 0.5,
                        OneShot = true,
                        Autostart = true
                    };
                    spawn_delay.Timeout += () =>
                    {
                        // spawn enemies in the room
                        var enemy_spawn_zone = EnemySpawnZoneScene.Instantiate<EnemySpawnZone>();
                        enemy_spawn_zone.Position = new Vector3(ChunkSize / 2, BaseChunkHeight, ChunkSize / 2);
                        features_container.AddChild(enemy_spawn_zone);
                        _track_spawned_enemies.Add(chunk_pos);

                        enemy_spawn_zone.AllEnemiesDead += () =>
                        {
                            // unlock doors
                            foreach (var (_, door) in Doors) door.UnlockDoorAndOpen();
                        };

                        // remove trigger
                        _enemy_spawn_trigger.QueueFree();
                    };
                    _enemy_spawn_trigger.AddChild(spawn_delay);
                }
            };
            _enemy_spawn_trigger.Position = Vector3.One * ChunkSize / 2;
            features_container.AddChild(_enemy_spawn_trigger);
        }

        //====== generate sarcophagi =====
        var has_sarco = seeded_random < 0.5f;
        var treasure_node = new Node3D
        {
            Name = "treasure_spawns"
        };
        features_container.AddChild(treasure_node);

        if (has_sarco && !is_starting_chunk && !DisableSarcophagusGeneration)
        {
            var sarco_count = chunk_rng.Next(0, 2);
            {
                for (int j = 0; j < sarco_count; j++)
                {
                    var sarco = _sarco_scene.Instantiate<TreasureChest>();
                    var transform = new Transform3D(Basis.Identity.Scaled(2f*Vector3.One), centerPoint - chunk_offset + Vector3.Up*0.25f);
                    add_chest(treasure_node, sarco, transform);
                }
            }
        }

        //====== generate treasure =====
        var treasure_gen_chance = 0.3f;
        if (!has_sarco && chunk_rng.NextSingle() < treasure_gen_chance)
        {
            var idx = _treasure_chests.Length - 1;
            for (int i = 0; i < TREASURE_CHEST_REROLLS; i++) idx = Math.Min(idx, chunk_rng.Next(0, _treasure_chests.Length));
            var chest = _treasure_chests[idx].Instantiate<TreasureChest>();
            var transform = new Transform3D(Basis.Identity.Rotated(Vector3.Up, chunk_rng.Next(4) * Mathf.Pi / 2), Vector3.Zero).Scaled(Vector3.One * 0.5f);
            transform.Origin = centerPoint - chunk_offset;
            add_chest(treasure_node, chest, transform);
        }

        //====== generate trees =====
        var hasTree = !is_starting_chunk && seeded_random < 0.3f;
        var num_trees = chunk_rng.Next(1, 4);
        if (hasTree) for (var t=0;t<num_trees;t++) // && SimpleWfc.ChunkHasNoWalls(chunk_tile_id))
        {
            // init tree
            var tree_scene = _tree_scenes[chunk_rng.Next(0, _tree_scenes.Length)];
            var tree = tree_scene.Instantiate<StaticBody3D>();
            var rand_angle = chunk_rng.NextSingle() * Mathf.Tau;
            var rand_dist = 2f + Math.Max(chunk_rng.NextSingle(), chunk_rng.NextSingle()) * 8f;
            var pos = (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);

            // add physics tracker to tree
            tree.AddChild(PhysTrackerScene.Instantiate<PhysBodyTracker>());

            var transform = tree.Transform;
            transform.Origin = centerPoint - chunk_offset + pos; // for SOME REASON all my paths are scaled by 2?
            transform = transform.Rotated(Vector3.Up, rand_angle);
            tree.Transform = transform.Scaled(Vector3.One * 3.0f);

            NavRegion.AddChild(tree);

            // generate butterfly particles around trees
            if (chunk_rng.NextSingle() < 0.5f)
            {
                var butterflies = _butterfly_particle_scene.Instantiate<Node3D>();
                //butterflies.Position = Vector3.Up*chunk_rng.Next(1,2)*0.5f;
                tree.AddChild(butterflies);
                butterflies.Scale = 0.5f*Vector3.One;
            }
        }

        return features_container;
    }

    private void SetupDoorLambdaFunctions()
    {
        var door_node = GetNodeOrNull<Node3D>("Doors");
        if (door_node != null)
        {
            var door_array = door_node.GetChildren().OfType<Door>().ToArray();
            //GD.Print($"Found {door_array.Length} doors in Chunk3d.");
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
                    var nextRoom = Chunk3dScene.Instantiate<Chunk3d>();

                    var next_pos = GlobalPosition + door_normal * ChunkSize;

                    // skip generation if the next room position is already created
                    if (CreatedChunks.ContainsKey(RoomToChunkPos(next_pos))) return;

                    AddSibling(nextRoom);
                    CreatedChunks.TryAdd(RoomToChunkPos(next_pos), nextRoom);
                    nextRoom.GlobalPosition = next_pos + Vector3.Down * ChunkSize * 2; // set spawn position
                    nextRoom.EmitSignal(SignalName.StartedLoading);

                    // remove the appropriate door in the next room, and set its doors dictionary
                    var next_doors = nextRoom.GetNode<Node3D>("Doors").GetChildren().OfType<Door>().ToArray();
                    nextRoom.Doors.Clear();
                    for (var neighbor_idx = 0; neighbor_idx < 4; neighbor_idx++)
                    {
                        var neighbor_chunk_pos = RoomToChunkPos(next_pos + DoorNormals[neighbor_idx] * ChunkSize);

                        if (CreatedChunks.TryGetValue(neighbor_chunk_pos, out Chunk3d neighbor_chunk))
                        {
                            next_doors[neighbor_idx].QueueFree();
                            if (neighbor_chunk.Doors.TryGetValue((neighbor_idx + 2) % 4, out var neigh_door))
                            {
                                nextRoom.Doors.TryAdd(neighbor_idx, neigh_door);
                            }
                        }
                        else
                        {
                            nextRoom.Doors[neighbor_idx] = next_doors[neighbor_idx];
                        }
                    }

                    var feature_container = nextRoom.AddFeatures();
                    var features = feature_container.GetChildren();
                    var delay = 0.5;

                    // tween next room upwards
                    var tween = nextRoom.CreateTween();
                    tween.Parallel().TweenProperty(nextRoom, "global_position", next_pos, 1.0)
                        .SetEase(Tween.EaseType.InOut)
                        .SetTrans(Tween.TransitionType.Bounce);
                    foreach (var feature in features.Cast<Node3D>())
                    {
                        var feature_targ_pos = feature.GlobalPosition + Vector3.Up * ChunkSize *2f;;
                        feature.GlobalPosition += Vector3.Up * ChunkSize * 3f; // start above the ground
                        tween.Parallel().TweenProperty(feature, "global_position", feature_targ_pos, 1.0)
                            .SetEase(Tween.EaseType.Out)
                            .SetTrans(Tween.TransitionType.Bounce)
                            .SetDelay(delay);
                        delay += 0.1;
                    }
                    tween.Finished += () =>
                    {
                        tween.Kill();
                        nextRoom.EmitSignal(SignalName.FinishedLoading);
                    };
                };
            }
        }
    }

    private Vector3I RoomToChunkPos(Vector3 room_pos) => new(Mathf.FloorToInt(room_pos.X / ChunkSize), Mathf.FloorToInt(room_pos.Y / ChunkSize), Mathf.FloorToInt(room_pos.Z / ChunkSize));
    private static uint ChunkCantorNumber(Vector3I chunk_pos) => CantorPairing(CantorPairing((uint)chunk_pos.X, (uint)chunk_pos.Z), (uint)chunk_pos.Y);
    private static uint CantorPairing(uint a, uint b) => (a + b) * (a + b + 1u) / 2u + b;
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class ChunkPlane : StaticBody3D
{
    [Export] public MeshInstance3D MeshInstance { get; set; }
    [Export] public CollisionShape3D CollisionShape { get; set; }
    [Export] public bool DisableWallGeneration { get; set; } = false;
    [Export] public bool DisableLampGeneration { get; set; } = false;
    [Export] public bool DisableSarcophagusGeneration { get; set; } = false;
    [Export] public bool DrawDebugMeshes = false;

    // private static readonly StandardMaterial3D _ground_material = GD.Load<StandardMaterial3D>("res://terrain_generator/infinite_heightmap_terrain/ground_material.tres");
    // private static readonly StandardMaterial3D _ground_material_2 = GD.Load<StandardMaterial3D>("res://terrain_generator/infinite_heightmap_terrain/ground_light_material.tres");
    
    private static readonly StandardMaterial3D _ground_material = GD.Load<StandardMaterial3D>("res://terrain_generator/grass/dark_green.tres");
    private static readonly StandardMaterial3D _ground_material_2 = GD.Load<StandardMaterial3D>("res://terrain_generator/grass/dark_green.tres");
    private static readonly PackedScene _csg_brick_wall_scene = GD.Load<PackedScene>("res://terrain_generator/procedural_brick_wall/csg/csg_brick_wall.tscn");

    private static readonly PackedScene _lamp_post_scene = GD.Load<PackedScene>("res://environment_models/street_lamps/streetlamp_small.tscn");

    private static readonly Noise _grass_noise = GD.Load<FastNoiseLite>("res://terrain_generator/grass/grass_fast_noise_lite.tres");
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

    private static readonly PackedScene[] _treasure_chests = 
    [
        GD.Load<PackedScene>("res://interactables/chest_scenes/big_chest.tscn"),
    ];

    private static readonly HashSet<Vector3> _opened_chests = []; 

    private static readonly PackedScene _sarco_scene = GD.Load<PackedScene>("res://interactables/chest_scenes/stone_sarcophagus.tscn");

    private static readonly PackedScene _firefly_particle_scene = GD.Load<PackedScene>("res://effects/gpu_particle_fireflies.tscn");
    private static readonly PackedScene _butterfly_particle_scene = GD.Load<PackedScene>("res://effects/butterfly/butterfly_particles.tscn");

    private const int MaxFlowersPerChunk = 1000;
    private static readonly Transform3D _base_flower_transform = new(
        Basis.Identity.Rotated(Vector3.Right, -Mathf.Pi/2).Rotated(Vector3.Up, Mathf.Pi).Scaled(Vector3.One*0.10f),
        Vector3.Zero
    );

    private Grass[] _grass_nodes;

    private bool _only_generate_mesh_once = true;
    



    private static readonly Vector3[] QuadVertices = [
        new (0, 0, 0),
        new (1, 0, 0),
        new (0, 0, 1),
        new (1, 0, 1)
    ];
    private static readonly Vector2[] QuadUVs = [
        new (0, 0),
        new (1, 0),
        new (0, 1),
        new (1, 1)
    ];
    private static readonly int[,] QuadTris = {
        { 0, 1, 2 },
        { 1, 3, 2 }
    };

    public override void _Ready()
    {
        _grass_nodes = new Grass[2];
        var mesh = GenerateHeightmapMesh(Vector2.Zero);
        if (GetNode("Grass") is Grass grass)
        {
            grass.Position += new Vector3(1,0,1)*ChunkManager.Instance.ChunkSize/8;
            grass.TerrainMesh = mesh;
            grass.Rebuild();
            _grass_nodes[0] = grass;
        }
        if (GetNode("DarkGrass") is Grass darkgrass)
        {
            darkgrass.Position = new Vector3(0,0,1)*ChunkManager.Instance.ChunkSize/8;
            darkgrass.TerrainMesh = mesh;
            darkgrass.Rebuild();
            _grass_nodes[1] = darkgrass;
        }

        var blue_flowers_multimesh = GetNode("BlueFlowerMultiMesh") as MultiMeshInstance3D;
        blue_flowers_multimesh.Multimesh = blue_flowers_multimesh.Multimesh.Duplicate() as MultiMesh;
        blue_flowers_multimesh.Multimesh.InstanceCount = MaxFlowersPerChunk;

        foreach (var child in GetNode("MultiMeshes").GetChildren())
        {
            if (child is MultiMeshInstance3D multimesh) {
                multimesh.Multimesh = multimesh.Multimesh.Duplicate() as MultiMesh;
                multimesh.Multimesh.InstanceCount = MaxFlowersPerChunk;
            }
        }
    }

    /// <summary>
    /// NOT IMLPEMENTED due to performance issues
    /// // HACK This only works because we have y-value locked to zero
    /// This is also massively slow and lags the game.
    /// Innefficient due to sampling the noise for every grass blade and updating the transform
    /// </summary>
    /// <param name="global_xz_offset"></param>
    public void ApplyGrassNoise(Vector2 global_xz_offset)
    {
        foreach (var grass in _grass_nodes)
        {
            for (int i=0; i<grass.Multimesh.InstanceCount; i++)
            {
                var transform = grass.Multimesh.GetInstanceTransform(i);
                var noise = _grass_noise.GetNoise2D(global_xz_offset.X+transform.Origin.X, global_xz_offset.Y+transform.Origin.Z);
                if (noise > 0.5)
                {
                    grass.Multimesh.SetInstanceTransform(i, new Transform3D(transform.Basis, transform.Origin - Vector3.Up*10000));
                }
                else
                {
                    grass.Multimesh.SetInstanceTransform(i, new Transform3D(transform.Basis, transform.Origin * new Vector3(1,0,1)));
                }
            }
        }
    }

    private static ArrayMesh GenerateHeightmapMesh(Vector2 global_xz_offset)
    {
        var chunkSize = ChunkManager.Instance.ChunkSize;
        var unitStep = ChunkManager.Instance.InverseResolution;
        var surftool = new SurfaceTool();
        var surftool2 = new SurfaceTool();
        surftool.Begin(Mesh.PrimitiveType.Triangles);
        surftool2.Begin(Mesh.PrimitiveType.Triangles);
        surftool.SetMaterial(_ground_material);

        for (float x=-chunkSize/2;x<chunkSize/2-1;x+=unitStep)
        {
            for (float z=-chunkSize/2;z<chunkSize/2-1;z+=unitStep)
            {
                var vertices = new Vector3[4];
                var uvs = new Vector2[4];

                for (int i=0; i < 4; i++)
                {
                    vertices[i] = new Vector3(x + QuadVertices[i].X*unitStep, 0, z + QuadVertices[i].Z*unitStep);
                    vertices[i].Y = ChunkManager.GetNoiseHeight(new Vector2(global_xz_offset.X+vertices[i].X,global_xz_offset.Y+vertices[i].Z));
                    uvs[i] = new Vector2(x+QuadUVs[i].X*unitStep, z+QuadUVs[i].Y*unitStep)/chunkSize;
                }

                var tris = new Vector3[6];
                var uvtris = new Vector2[6];

                for (int i = 0; i < 6; i++)
                {
                    int index = QuadTris[i / 3, i % 3];
                    tris[i] = vertices[index];
                    uvtris[i] = uvs[index];
                }

                var n1 = (tris[0] - tris[1]).Cross(tris[2] - tris[0]).Normalized();
                var n2 = (tris[3] - tris[4]).Cross(tris[5] - tris[3]).Normalized();
                var normals = new Vector3[] {n1,n1,n1,n2,n2,n2};

                var targsuftool = (x/unitStep+z/unitStep)%2==0 ? surftool : surftool2;

                targsuftool.AddTriangleFan([tris[0],tris[1],tris[2]], [uvtris[0],uvtris[1],uvtris[2]], normals:[normals[0],normals[1],normals[2]]);
                targsuftool.AddTriangleFan([tris[3],tris[4],tris[5]], [uvtris[3],uvtris[4],uvtris[5]], normals:[normals[3],normals[4],normals[5]]);
            }
        }
        surftool.Index();
        surftool2.Index();

        var mesh = surftool.Commit();
        //mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surftool.CommitToArrays());
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surftool2.CommitToArrays());
        mesh.SurfaceSetMaterial(1, _ground_material_2);
        return mesh;
    }

    public static void SetChestOpened(TreasureChest chest)
    {
        _opened_chests.Add(chest.GlobalPosition);
    }

    private void ClearGeneratedObjects()
    {
        // clear all children
        foreach (var child in GetChildren())
        {
            if (child is CsgPolygon3D || child is Path3D) child.QueueFree();
            else if (child is MeshInstance3D && child != MeshInstance) child.QueueFree(); // remove debug mesh instances
            else if (child is Node3D && child is not MultiMeshInstance3D && child is not CollisionShape3D && child != MeshInstance)
            {
                switch(child.Name)
                {
                    case "MultiMeshes":
                        break;
                    default:
                        child.QueueFree();
                        break;
                }
            }
        }
    }

    private void GenerateWallsAndMeshes()
    {
        ClearGeneratedObjects();

        var chunk_pos = GetChunkPosition();
        var is_starting_chunk = chunk_pos == Vector2I.Zero;
        var seeded_random = ChunkManager.Instance.NoiseTexture.GetNoise2D(chunk_pos.X*32,chunk_pos.Y*32);
        var chunk_rng = new Random(GetCantorPairing(chunk_pos*Mathf.RoundToInt(seeded_random*1000)));
        seeded_random = seeded_random * 0.5f + 0.5f; // remap to 0-1
        var chunk_tile_id = SimpleWfc.GetTileID(chunk_pos);
        
        // ====== generate walls =====
        var chunk_offset = ChunkManager.Instance.ChunkSize * chunk_pos;
        var path_set = SimpleWfc.GetTilePaths(chunk_pos);
        var nodePaths = new List<string>();
        foreach (var path in path_set)
        {
            var path_copy = path.Duplicate() as Path3D;
            path_copy.Curve = path.Curve.Duplicate() as Curve3D;

            // HACK no need to set y position of path points when we're just doing a flat plane
            // for (int i=0;i<path_copy.Curve.GetPointCount();i++)
            // {
            //     var point = path_copy.Curve.GetPointPosition(i);
            //     point.Y = ChunkManager.GetNoiseHeight(new Vector2(point.X,point.Z) + chunk_offset);
            //     path_copy.Curve.SetPointPosition(i,point);
            // }
            //ResourceSaver.Save(path_copy.Curve,$"res://curves/curve{path.Name}.tres");
            AddChild(path_copy);

            if (DisableWallGeneration)
            {
                nodePaths.Add(path_copy.GetPath());
                continue;
            }

            var wall = _csg_brick_wall_scene.Instantiate<CsgPolygon3D>();
            if (seeded_random < 0.5 || is_starting_chunk)
            {
                wall.PathInterval = 10.0f;
            }
            path_copy.AddChild(wall);
            wall.PathNode = path_copy.GetPath();
            nodePaths.Add(wall.PathNode);
            path_copy.GlobalPosition *= 0.5f; // ???? the global position is doubled for some reason
        }
        
        //====== generate lamps =====
        var centerPoint = new Vector3(chunk_offset.X,0,chunk_offset.Y);
        var distance_between_lamps = 16.0f;
        var chunk_has_lamps = seeded_random < 0.4f || is_starting_chunk; 
        var lamp_node = new Node3D();
        AddChild(lamp_node);
        lamp_node.Name = "Lamps";
        
        if (chunk_has_lamps && !DisableLampGeneration) for (int i=0; i<nodePaths.Count;i++)
        {
            var path_str = nodePaths[i];
            var path_node = GetNodeOrNull<Path3D>(path_str);
            if (path_node == null) continue;
            var path_length = path_node.Curve.GetBakedLength();
            var lamp_count = 1+Mathf.FloorToInt(path_length / distance_between_lamps);

            for (int j=0;j<lamp_count;j++)
            {
                var lamp = _lamp_post_scene.Instantiate<StaticBody3D>();
                lamp_node.AddChild(lamp);
                var point = path_node.Curve.SampleBaked(j*distance_between_lamps);
                lamp.Position = point*0.8f;

                // add firefly particles to lamps
                if (chunk_rng.NextSingle() < 0.5f)
                {
                    var firefly = _firefly_particle_scene.Instantiate<Node3D>();
                    lamp.AddChild(firefly);
                    firefly.Position = Vector3.Up*7.735f;
                    //firefly.Scale = 0.1f*Vector3.One;
                }
            }
        }

        //====== generate sarcophagi =====
        var has_sarco = seeded_random < (chunk_has_lamps ? 0.2f : 0.5f);
        var treasure_node = new Node3D
        {
            Name = "treasure_spawns"
        };
        AddChild(treasure_node);
        void add_chest(TreasureChest chest, Transform3D transform)
        {
            chest.Transform = transform;
            treasure_node.AddChild(chest);
            if (_opened_chests.Contains(chest.GlobalPosition))
            {
                chest.ForceStateOpen();
            }
        }
        if (has_sarco && !is_starting_chunk && !DisableSarcophagusGeneration)
        {
            var sarco_count = chunk_rng.Next(0,2) + (chunk_has_lamps ? 0 : chunk_rng.Next(0,2));
            {
                for (int j=0;j<sarco_count;j++)
                {
                    var sarco = _sarco_scene.Instantiate<TreasureChest>();
                    var rand_angle = chunk_rng.NextSingle()*Mathf.Pi*2;
                    var rand_dist = 2.0f+Math.Max(chunk_rng.NextSingle(),chunk_rng.NextSingle())*14.0f;
                    var pos = (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);
                    var transform = new Transform3D(Basis.Identity.Rotated(Vector3.Up, rand_angle), pos);
                    add_chest(sarco, transform);
                }
            }
        }
        
        //====== generate flowers around base of lamps at random =====
        var blue_flowers_multimesh = GetNode("BlueFlowerMultiMesh") as FlowerMultiMesh;
        blue_flowers_multimesh.Multimesh.VisibleInstanceCount = 0;
        var lamp_flower_distance = new Vector2(0.5f,2.0f); // min max distance from lamp to flowers around base
        var lamp_flower_amount = new Vector2I(1,6);
        var flower_sway_yaw = FlowerMultiMesh.SwayYawRadians;
        var flower_sway_pitch = FlowerMultiMesh.SwayPitchRadians;
        foreach (var child in lamp_node.GetChildren())
        {
            if (child is not StaticBody3D lamp) continue;
            if (chunk_rng.NextSingle() < 0.5f)
            {
                var num_flower = chunk_rng.Next(lamp_flower_amount.X,lamp_flower_amount.Y);

                if (blue_flowers_multimesh.Multimesh.VisibleInstanceCount + num_flower < blue_flowers_multimesh.Multimesh.InstanceCount)
                {
                    blue_flowers_multimesh.Multimesh.VisibleInstanceCount += num_flower;
                }
                else 
                {
                    num_flower = blue_flowers_multimesh.Multimesh.InstanceCount - blue_flowers_multimesh.Multimesh.VisibleInstanceCount;
                    blue_flowers_multimesh.Multimesh.VisibleInstanceCount = blue_flowers_multimesh.Multimesh.InstanceCount;
                }

                for (var k=0; k < num_flower; k++)
                {
                    //var flower_instance = blue_flowers_multimesh.Multimesh.GetInstanceTransform(k);
                    var rand_angle = chunk_rng.NextSingle()*Mathf.Pi*2;
                    var rand_dist = lamp_flower_distance.X+chunk_rng.NextSingle()*(lamp_flower_distance.Y-lamp_flower_distance.X);
                    var transform = _base_flower_transform;
                    transform = transform.Rotated(Vector3.Up, rand_angle);
                    transform.Origin = lamp.Position + (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);

                    var customParams = new Color( 
                        (float)GD.RandRange(FlowerMultiMesh.BladeWidth.X,FlowerMultiMesh.BladeWidth.Y),
                        (float)GD.RandRange(FlowerMultiMesh.BladeHeight.X,FlowerMultiMesh.BladeHeight.Y),
                        (float)GD.RandRange(flower_sway_yaw.X, flower_sway_yaw.Y),
                        (float)GD.RandRange(flower_sway_pitch.X, flower_sway_pitch.Y)
                    );

                    var flower_idx = blue_flowers_multimesh.Multimesh.VisibleInstanceCount-num_flower+k;
                    blue_flowers_multimesh.Multimesh.SetInstanceTransform(flower_idx, transform);
                    blue_flowers_multimesh.Multimesh.SetInstanceCustomData(flower_idx, customParams);
                    blue_flowers_multimesh.Multimesh.SetInstanceColor(flower_idx, new Color(transform.Origin.Length()/16,1,1));
                    
                    //flower.GlobalPosition = lamp.GlobalPosition + (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);
                    // flower.Rotation = new Vector3(0, rand_angle, 0);
                }
            }
        }

        //====== generate flowers around center =====
        var flower_count = SimpleWfc.GetTileFlowerAmount(chunk_rng, SimpleWfc.GetTileID(chunk_pos));
        if (blue_flowers_multimesh.Multimesh.VisibleInstanceCount + flower_count < blue_flowers_multimesh.Multimesh.InstanceCount)
        {
            blue_flowers_multimesh.Multimesh.VisibleInstanceCount += flower_count;
        }
        else 
        {
            flower_count = blue_flowers_multimesh.Multimesh.InstanceCount - blue_flowers_multimesh.Multimesh.VisibleInstanceCount;
            blue_flowers_multimesh.Multimesh.VisibleInstanceCount = blue_flowers_multimesh.Multimesh.InstanceCount;
        }
        for (int i=0; i<flower_count; i++)
        {
            var rand_angle = chunk_rng.NextSingle()*Mathf.Pi*2;
            var rand_dist = Math.Max(chunk_rng.NextSingle(),chunk_rng.NextSingle())*16.0f;
            var glob_pos = centerPoint + (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);

            if (chunk_has_lamps)
            {
                foreach (var child in lamp_node.GetChildren())
                {
                    if (child is not StaticBody3D lamp) continue;
                    var lamp_pos = lamp.GlobalPosition;
                    if (glob_pos.DistanceTo(lamp_pos) < 2)
                    {
                        glob_pos = centerPoint + (Vector3.Forward * (rand_dist-4)).Rotated(Vector3.Up, rand_angle);
                    }
                }
            }
            var transform = _base_flower_transform;
            transform = transform.Rotated(Vector3.Up, rand_angle);
            transform.Origin = glob_pos - centerPoint;

            var customParams = new Color( 
                        (float)GD.RandRange(FlowerMultiMesh.BladeWidth.X,FlowerMultiMesh.BladeWidth.Y),
                        (float)GD.RandRange(FlowerMultiMesh.BladeHeight.X,FlowerMultiMesh.BladeHeight.Y),
                        (float)GD.RandRange(flower_sway_yaw.X, flower_sway_yaw.Y),
                        (float)GD.RandRange(flower_sway_pitch.X, flower_sway_pitch.Y)
                    );

            var flower_idx = blue_flowers_multimesh.Multimesh.VisibleInstanceCount-flower_count+i;
            blue_flowers_multimesh.Multimesh.SetInstanceTransform(flower_idx, transform);
            blue_flowers_multimesh.Multimesh.SetInstanceCustomData(flower_idx, customParams);
            blue_flowers_multimesh.Multimesh.SetInstanceColor(flower_idx, new Color(transform.Origin.Length()/16,1,1));
        }

        //====== generate some generic shrubs around center =====
        foreach (var child in GetNode("MultiMeshes").GetChildren())
        {
            if (child is MultiMeshInstance3D multimesh){
                var gen_count = chunk_rng.Next(0, 10);
                PopulateMultimesh(multimesh, chunk_rng, gen_count);
            }
        }

        //====== generate trees =====
        var has_walls = !SimpleWfc.ChunkHasNoWalls(chunk_tile_id);
        var hasTree = !is_starting_chunk && (seeded_random < 0.3f + (has_walls ? 0 : 1) * 0.5f);
        var num_trees = chunk_rng.Next(1, 2);
        if (SimpleWfc.ChunkHasNoWalls(chunk_tile_id)) num_trees += 3;
        if (hasTree) for (var t=0;t<num_trees;t++) // && SimpleWfc.ChunkHasNoWalls(chunk_tile_id))
        {
            var tree_scene = _tree_scenes[chunk_rng.Next(0, _tree_scenes.Length)];
            var tree = tree_scene.Instantiate<StaticBody3D>();
            var rand_angle = chunk_rng.NextSingle() * Mathf.Pi * 2;
            var rand_dist = Math.Max(chunk_rng.NextSingle(), chunk_rng.NextSingle()) * 16.0f;
            var pos = (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);

            // ensure that we don't spawn trees on top of walls
            for (var i=0; i<nodePaths.Count;i++)
            {
                var path_str = nodePaths[i];
                var path_node = GetNodeOrNull<Path3D>(path_str);
                if (path_node == null) continue;
                var path_points = path_node.Curve.GetBakedPoints();
                var random_point = path_points[chunk_rng.Next(0, path_points.Length)];
                pos = random_point*chunk_rng.NextSingle()*0.4f;
            }

            var transform = tree.Transform;
            transform.Origin = pos*0.5f; // for SOME REASON all my paths are scaled by 2?
            transform = transform.Rotated(Vector3.Up, rand_angle);
            tree.Transform = transform.Scaled(Vector3.One * 3.0f);

            AddChild(tree);

            // generate butterfly particles around trees
            if (chunk_rng.NextSingle() < 0.5f)
            {
                var butterflies = _butterfly_particle_scene.Instantiate<Node3D>();
                //butterflies.Position = Vector3.Up*chunk_rng.Next(1,2)*0.5f;
                tree.AddChild(butterflies);
                butterflies.Scale = 0.5f*Vector3.One;
            }
        }

        //====== generate treasure =====
        var treasure_gen_chance = SimpleWfc.GetTileTreasureGenerationChance(chunk_tile_id);
        if (chunk_rng.NextSingle() < treasure_gen_chance)
        {
            var idx = 0;
            var rerolls = 3;
            for (int i=0;i<rerolls;i++) idx = Math.Min(idx, chunk_rng.Next(0, _treasure_chests.Length));
            var chest = _treasure_chests[idx].Instantiate<TreasureChest>();
            var transform = new Transform3D(Basis.Identity.Rotated(Vector3.Up,chunk_rng.Next(4) * Mathf.Pi/2), Vector3.Zero).Scaled(Vector3.One*0.5f);
            add_chest(chest, transform);
        }

        if (DrawDebugMeshes)
        {
            // generate centre mesh
            var test = new MeshInstance3D
            {
                Mesh = new BoxMesh(),
                MaterialOverride = new StandardMaterial3D() { AlbedoColor = new Color(1, 0, 0) }
            };
            AddChild(test);
            test.GlobalPosition = centerPoint;

            //test generate "treasure"
            if (chunk_rng.NextSingle() < treasure_gen_chance)
            {
                var chest = new MeshInstance3D
                {
                    Mesh = new BoxMesh() {Size = Vector3.One*2},
                    MaterialOverride = new StandardMaterial3D(){AlbedoColor = new Color(1,0.1f,1)}
                };
                AddChild(chest);
                test.GlobalPosition = centerPoint;
            }
        }
    }

    private static void PopulateMultimesh(MultiMeshInstance3D multimesh, Random chunk_rng, int count)
    {
        if (count == 0) return;
        multimesh.Multimesh.VisibleInstanceCount = 0;
        if (multimesh.Multimesh.VisibleInstanceCount + count < multimesh.Multimesh.InstanceCount)
        {
            multimesh.Multimesh.VisibleInstanceCount += count;
        }
        else 
        {
            count = multimesh.Multimesh.InstanceCount - multimesh.Multimesh.VisibleInstanceCount;
            multimesh.Multimesh.VisibleInstanceCount = multimesh.Multimesh.InstanceCount ;
        }
        for (int i=0; i<count; i++)
        {
            var rand_angle = chunk_rng.NextSingle()*Mathf.Pi*2;
            var rand_dist = Math.Max(chunk_rng.NextSingle(),chunk_rng.NextSingle())*16.0f;
            var pos = (Vector3.Forward * rand_dist).Rotated(Vector3.Up, rand_angle);

            var transform = new Transform3D(Basis.Identity, Vector3.Zero);
            transform = transform.Rotated(Vector3.Up, rand_angle);
            transform.Origin = pos;
            transform = transform.Scaled(Vector3.One*2.0f);

            var customParams = new Color( 
                GD.RandRange(0,1),
                GD.RandRange(0,1),
                GD.RandRange(0,1),
                GD.RandRange(0,1)
            );

            var mesh_idx = multimesh.Multimesh.VisibleInstanceCount-count+i;
            multimesh.Multimesh.SetInstanceTransform(mesh_idx, transform);
            multimesh.Multimesh.SetInstanceCustomData(mesh_idx, customParams);
        }
    }

    public async void UpdateMeshAndCollisionShape()
    {
        // disable mesh generation because it's flat rn
        if (_only_generate_mesh_once)
        {
            _only_generate_mesh_once = false;
            var mesh = MeshInstance.Mesh;
            var global_xz_offset = ChunkManager.Instance.ChunkSize * GetChunkPosition();
            await Task.Run(() =>
            {
                mesh = GenerateHeightmapMesh(global_xz_offset);
            });
            MeshInstance.Mesh = mesh;
            CollisionShape.Shape = MeshInstance.Mesh.CreateTrimeshShape();
        }

        GenerateWallsAndMeshes();
        //CallDeferred(nameof(GenerateWallsAndMeshes));
        
        //await Task.Run(()=>ApplyGrassNoise(global_xz_offset));
    }

    public Vector2I GetChunkPosition()
    {
        return new Vector2I(
            Mathf.FloorToInt(GlobalPosition.X / ChunkManager.Instance.ChunkSize),
            Mathf.FloorToInt(GlobalPosition.Z / ChunkManager.Instance.ChunkSize)
        );
    }

    public void SetChunkPosition(Vector2I position)
    {
        var prev_pos = GetChunkPosition();
        GlobalPosition = new Vector3(position.X * ChunkManager.Instance.ChunkSize, 0, position.Y * ChunkManager.Instance.ChunkSize);
        ChunkManager.UpdateChunkPosition(this, position, prev_pos);
        UpdateMeshAndCollisionShape();
    }

    private int GetChunkCantorPairing()
    {
        var chunk_pos = GetChunkPosition();
        return GetCantorPairing(chunk_pos);
    }

    private static int GetCantorPairing(Vector2I chunk_pos)
    {
        var x = chunk_pos.X;
        var y = chunk_pos.Y;
        return (x + y) * (x + y + 1) / 2 + y;
    }
}
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class ChunkPlane : StaticBody3D
{
    [Export] public MeshInstance3D MeshInstance { get; set; }
    [Export] public CollisionShape3D CollisionShape { get; set; }

    // private static readonly StandardMaterial3D _ground_material = GD.Load<StandardMaterial3D>("res://terrain_generator/infinite_heightmap_terrain/ground_material.tres");
    // private static readonly StandardMaterial3D _ground_material_2 = GD.Load<StandardMaterial3D>("res://terrain_generator/infinite_heightmap_terrain/ground_light_material.tres");
    
    private static readonly StandardMaterial3D _ground_material = GD.Load<StandardMaterial3D>("res://terrain_generator/grass/dark_green.tres");
    private static readonly StandardMaterial3D _ground_material_2 = GD.Load<StandardMaterial3D>("res://terrain_generator/grass/dark_green.tres");
    private static readonly PackedScene _csg_brick_wall_scene = GD.Load<PackedScene>("res://terrain_generator/procedural_brick_wall/csg/csg_brick_wall.tscn");

    private static readonly PackedScene _lamp_post_scene = GD.Load<PackedScene>("res://environment_models/street_lamps/streetlamp_small.tscn");

    private static readonly Noise _grass_noise = GD.Load<FastNoiseLite>("res://terrain_generator/grass/grass_fast_noise_lite.tres");

    private Grass[] _grass_nodes;

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
    }

    /// <summary>
    /// // HACK This only works because we have y-value locked to zero
    /// This is also massively slow and doesn't work.
    /// Innefficient due to sampling the noise for every grass blade
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

        for (float x=0;x<chunkSize;x+=unitStep)
        {
            for (float z=0;z<chunkSize;z+=unitStep)
            {
                var vertices = new Vector3[4];
                var uvs = new Vector2[4];

                for (int i=0; i < 4; i++)
                {
                    vertices[i] = new Vector3(x + QuadVertices[i].X*unitStep, 0, z + QuadVertices[i].Z*unitStep);
                    vertices[i].Y = ChunkManager.GetNoiseHeight(new Vector2(global_xz_offset.X+vertices[i].X,global_xz_offset.Y+vertices[i].Z));
                    //vertices[i].Y =  Mathf.RoundToInt(vertices[i].Y/unitStep)*unitStep;
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

    private void GenerateWallsAndMeshes()
    {
        var cent_offset = new Vector3(16,0,16);
        var chunk_pos = GetChunkPosition();
        var seeded_random = ChunkManager.Instance.NoiseTexture.GetNoise2D(chunk_pos.X*32,chunk_pos.Y*32);
        
        // clear all children
        foreach (var child in GetChildren())
        {
            if (child is CsgPolygon3D || child is Path3D) child.QueueFree();
            if (child is Streetlamp) child.QueueFree();
            if (child is MeshInstance3D && child != MeshInstance) child.QueueFree();
        }

        // generate walls
        var chunk_offset = ChunkManager.Instance.ChunkSize * chunk_pos;
        var path_set = SimpleWfc.GetTilePaths(chunk_pos);
        var nodePaths = new List<string>();
        foreach (var path in path_set)
        {
            var path_copy = path.Duplicate() as Path3D;
            path_copy.Curve = path.Curve.Duplicate() as Curve3D;
            for (int i=0;i<path_copy.Curve.GetPointCount();i++)
            {
                var point = path_copy.Curve.GetPointPosition(i)+cent_offset;
                point.Y = ChunkManager.GetNoiseHeight(new Vector2(point.X,point.Z) + chunk_offset);
                path_copy.Curve.SetPointPosition(i,point);
            }
            //ResourceSaver.Save(path_copy.Curve,$"res://curves/curve{path.Name}.tres");
            AddChild(path_copy);
            var wall = _csg_brick_wall_scene.Instantiate<CsgPolygon3D>();
            if (seeded_random > 0)
            {
                wall.PathInterval = 8.0f;
            }
            path_copy.AddChild(wall);
            wall.PathNode = path_copy.GetPath();
            nodePaths.Add(wall.PathNode);
            path_copy.GlobalPosition *= 0.5f; // ???? the global position is doubled for some reason
        }

        //generate lamps
        var cantor = GetCantorPairing();
        var centerPoint = new Vector3(chunk_offset.X,0,chunk_offset.Y)+cent_offset;
        // force y to be zero because we're not using heightmap in this build
        // centerPoint.Y = ChunkManager.GetNoiseHeight(new Vector2(centerPoint.X,centerPoint.Z));
        var distance_between_lamps = 16.0f;

        if (seeded_random>0) for (int i=0; i<nodePaths.Count;i++)
        {
            var path_str = nodePaths[i];
            var path_node = GetNodeOrNull<Path3D>(path_str);
            if (path_node == null) continue;
            var path_length = path_node.Curve.GetBakedLength();
            var lamp_count = Mathf.FloorToInt(path_length / distance_between_lamps);

            for (int j=0;j<lamp_count;j++)
            {
                var point = path_node.Curve.SampleBaked(j*distance_between_lamps);
                var lamp = _lamp_post_scene.Instantiate<StaticBody3D>();
                AddChild(lamp);
                lamp.GlobalPosition = centerPoint-cent_offset+point;//+(point-centerPoint).Normalized()*lamp_distance_from_wall;

                var cent_len = (centerPoint - lamp.GlobalPosition).Length()*0.8f;
                lamp.GlobalPosition = centerPoint+cent_len*(lamp.GlobalPosition-centerPoint).Normalized()*0.8f;
                lamp.LookAt(centerPoint, Vector3.Up);

                var mesh = new BoxMesh
                {
                    Size = new Vector3(0.1f, 0.1f, cent_len)
                };

                var test_line = new MeshInstance3D
                {
                    Mesh = mesh,
                    MaterialOverride = new StandardMaterial3D() { AlbedoColor = new Color(1.0f-cent_len/16.0f, 0.2f, 0.2f) }
                };
                AddChild(test_line);
                test_line.Transform = new Transform3D(Basis.Identity, new Vector3(0.0f, 0.0f, cent_len/2));
                test_line.GlobalPosition = lamp.GlobalPosition;
                test_line.LookAt(centerPoint, Vector3.Up);
            }
        }

        // generate centre mesh
        var test = new MeshInstance3D
        {
            Mesh = new BoxMesh(),
            MaterialOverride = new StandardMaterial3D() { AlbedoColor = new Color(1, 0, 0) }
        };
        AddChild(test);
        test.GlobalPosition = centerPoint;

        //test generate "treasure"
        var tile_id = SimpleWfc.GetTileID(chunk_pos);
        var gen_chance = SimpleWfc.GetTileGenerationChance(tile_id);
        
        if (Random.Shared.NextSingle() < gen_chance)
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

    public async void UpdateMeshAndCollisionShape()
    {
        var mesh = MeshInstance.Mesh;
        var global_xz_offset = ChunkManager.Instance.ChunkSize * GetChunkPosition();
        await Task.Run(() =>
        {
            mesh = GenerateHeightmapMesh(global_xz_offset);
        });
        MeshInstance.Mesh = mesh;
        CollisionShape.Shape = MeshInstance.Mesh.CreateTrimeshShape();
        GenerateWallsAndMeshes();
        
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

    private int GetCantorPairing()
    {
        var chunk_pos = GetChunkPosition();
        var x = chunk_pos.X;
        var y = chunk_pos.Y;
        return (x + y) * (x + y + 1) / 2 + y;
    }
}
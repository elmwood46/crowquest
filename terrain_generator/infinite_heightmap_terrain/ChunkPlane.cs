using Godot;
using System;
using System.Threading.Tasks;

public partial class ChunkPlane : StaticBody3D
{
    [Export] public MeshInstance3D MeshInstance { get; set; }
    [Export] public CollisionShape3D CollisionShape { get; set; }

    private static readonly StandardMaterial3D _ground_material = GD.Load<StandardMaterial3D>("res://terrain_generator/infinite_heightmap_terrain/ground_material.tres");
    private static readonly StandardMaterial3D _ground_material_2 = GD.Load<StandardMaterial3D>("res://terrain_generator/infinite_heightmap_terrain/ground_light_material.tres");
    private static readonly PackedScene _csg_brick_wall_scene = GD.Load<PackedScene>("res://terrain_generator/procedural_brick_wall/csg/csg_brick_wall.tscn");

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
        GD.Print("mesh surfaces: ", mesh.GetSurfaceCount());
        mesh.SurfaceSetMaterial(1, _ground_material_2);
        return mesh;
    }

    private void GenerateWalls()
    {
        foreach (var child in GetChildren())
        {
            if (child is CsgPolygon3D || child is Path3D) child.QueueFree();
        }

        var chunk_pos = GetChunkPosition();
        var path_set = SimpleWfc.GetTilePaths(chunk_pos);
        foreach (var path in path_set)
        {
            var path_copy = path.Duplicate() as Path3D;
            path_copy.Curve = path.Curve.Duplicate() as Curve3D;
            for (int i=0;i<path_copy.Curve.GetPointCount();i++)
            {
                var point = path_copy.Curve.GetPointPosition(i)+new Vector3(16,0,16);
                point.Y = ChunkManager.GetNoiseHeight(new Vector2(point.X,point.Z) + ChunkManager.Instance.ChunkSize * chunk_pos);
                path_copy.Curve.SetPointPosition(i,point);
            }
            //ResourceSaver.Save(path_copy.Curve,$"res://curves/curve{path.Name}.tres");
            AddChild(path_copy);
            var wall = _csg_brick_wall_scene.Instantiate<CsgPolygon3D>();
            path_copy.AddChild(wall);
            wall.PathNode = path_copy.GetPath();
            path_copy.GlobalPosition *= 0.5f; // ???? the global position is doubled for some reason
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
        GenerateWalls();
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
}
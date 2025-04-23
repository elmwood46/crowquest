using Godot;
using System;
using System.Threading.Tasks;

public partial class ChunkPlane : StaticBody3D
{
    [Export] public MeshInstance3D MeshInstance { get; set; }
    [Export] public CollisionShape3D CollisionShape { get; set; }
    private static readonly StandardMaterial3D _ground_material = GD.Load<StandardMaterial3D>("res://terrain_generator/ground_material.tres");

    public static readonly Vector3[] QuadVertices = [
        new (0, 0, 0),
        new (1, 0, 0),
        new (0, 0, 1),
        new (1, 0, 1)
    ];

    public static readonly Vector2[] QuadUVs = [
        new (0, 0),
        new (1, 0),
        new (0, 1),
        new (1, 1)
    ];

    public static readonly int[,] QuadTris = {
        { 0, 1, 2 },
        { 1, 3, 2 }
    };

    private ArrayMesh GenerateHeightmapMesh(Vector2I chunk_position)
    {
        var noise = ChunkManager.Instance.NoiseTexture;
        var chunkSize = ChunkManager.Instance.ChunkSize;
        var unitStep = ChunkManager.Instance.InverseResolution;
        var surftool = new SurfaceTool();
        var globalPos = chunk_position;
        
        surftool.Begin(Mesh.PrimitiveType.Triangles);
        for (float x=0;x<chunkSize;x+=unitStep)
        {
            for (float z=0;z<chunkSize;z+=unitStep)
            {
                var vertices = new Vector3[4];
                var uvs = new Vector2[4];

                for (int i=0; i < 4; i++)
                {
                    vertices[i] = new Vector3(x + QuadVertices[i].X*unitStep, 0, z + QuadVertices[i].Z*unitStep);
                    vertices[i].Y = ChunkManager.Instance.MaxHeight*(noise.GetNoise2D(globalPos.X+vertices[i].X,globalPos.Y+vertices[i].Z)+1)/2;
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

                surftool.AddTriangleFan([tris[0],tris[1],tris[2]], [uvtris[0],uvtris[1],uvtris[2]], normals:[normals[0],normals[1],normals[2]]);
                surftool.AddTriangleFan([tris[3],tris[4],tris[5]], [uvtris[3],uvtris[4],uvtris[5]], normals:[normals[3],normals[4],normals[5]]);
            }
        }
        surftool.Index();
        surftool.SetMaterial(_ground_material);
        return surftool.Commit();
    }

    public async void UpdateMeshAndCollisionShape()
    {
        var mesh = MeshInstance.Mesh;
        var chunkpos =  ChunkManager.Instance.ChunkSize * GetChunkPosition();
        await Task.Run(() =>
        {
            mesh = GenerateHeightmapMesh(chunkpos);
        });
        MeshInstance.Mesh = mesh;
        CollisionShape.Shape = MeshInstance.Mesh.CreateTrimeshShape();
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
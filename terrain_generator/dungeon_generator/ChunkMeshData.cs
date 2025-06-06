using System.Collections.Generic;
using Godot;
//using MessagePack;

public class ChunkMeshData {
    public const byte CHUNK_SURFACE = 0;
    public const byte GRASS_SURFACE = 1;
    public const byte LAVA_SURFACE = 2;
    public const byte GOLD_SURFACE = 3;
    public const byte ALL_SURFACES = 4; // must be equal to the number of surfaces

    private readonly ArrayMesh[] _surfaces = new ArrayMesh[ALL_SURFACES + 1];

    private static readonly StandardMaterial3D DefaultMaterial = new()
    {
        AlbedoColor = new Color(1, 1, 1),
    };

    public StaticBody3D StaticBody { get; set; } // holds collision data

    public ChunkMeshData(Dictionary<int, Dictionary<int, List<float>>> serializedData)
    {
        _surfaces[CHUNK_SURFACE] = new ArrayMesh();
        _surfaces[GRASS_SURFACE] = new ArrayMesh();
        _surfaces[LAVA_SURFACE] = new ArrayMesh();
        _surfaces[GOLD_SURFACE] = new ArrayMesh();
        _surfaces[ALL_SURFACES] = new ArrayMesh();
        ReconstructFromSerializedData(serializedData);
        UnifySurfaces();
        //_trimesh_shape = _surfaces[ALL_SURFACES].GetSurfaceCount() > 0 ? _surfaces[ALL_SURFACES].CreateTrimeshShape() : new ConcavePolygonShape3D();
    }

    public ChunkMeshData(ArrayMesh[] input_surfaces) {
        _surfaces[CHUNK_SURFACE] = input_surfaces[CHUNK_SURFACE];
        _surfaces[GRASS_SURFACE] = input_surfaces[GRASS_SURFACE];
        _surfaces[LAVA_SURFACE] = input_surfaces[LAVA_SURFACE];
        _surfaces[GOLD_SURFACE] = input_surfaces[GOLD_SURFACE];
        _surfaces[ALL_SURFACES] = new ArrayMesh();
        UnifySurfaces();
    }

    public ChunkMeshData() { }
    
    public void SetSurfaces(ArrayMesh[] input_surfaces)
    {
        _surfaces[CHUNK_SURFACE] = input_surfaces[CHUNK_SURFACE];
        _surfaces[GRASS_SURFACE] = input_surfaces[GRASS_SURFACE];
        _surfaces[LAVA_SURFACE] = input_surfaces[LAVA_SURFACE];
        _surfaces[GOLD_SURFACE] = input_surfaces[GOLD_SURFACE];
        _surfaces[ALL_SURFACES] = new ArrayMesh();
        UnifySurfaces();
    }

    public bool HasSurfaceOfType(byte type)
    {
        return _surfaces[type]?.GetSurfaceCount() > 0;
    }

    public ArrayMesh GetSurface(byte type) {
        return _surfaces[type];
    }

    public ArrayMesh GetUnifiedSurfaces() {
        return _surfaces[ALL_SURFACES];
    }

    private void UnifySurfaces() {
        _surfaces[ALL_SURFACES].ClearSurfaces();
        for (byte type = 0; type < ALL_SURFACES; type++) {
            if (HasSurfaceOfType(type)) {
                var surface = _surfaces[type];
                ShaderMaterial material = type switch {
                    CHUNK_SURFACE => BlockManager.Instance.ChunkMaterial,
                    GRASS_SURFACE => BlockManager.Instance.ChunkMaterial,
                    LAVA_SURFACE => BlockManager.Instance.LavaShader,
                    GOLD_SURFACE => BlockManager.Instance.ChunkMaterialDamagePulse,
                    _ => BlockManager.Instance.ChunkMaterial
                };
                _surfaces[ALL_SURFACES].AddSurfaceFromArrays(
                    Mesh.PrimitiveType.Triangles,
                    surface.SurfaceGetArrays(0)
                );
                _surfaces[ALL_SURFACES].SurfaceSetMaterial(
                    _surfaces[ALL_SURFACES].GetSurfaceCount() - 1,
                    material
                );
            }
        }
    }

    public ArrayMesh GetNavigableSurfaces()
    {
        var ret = new ArrayMesh();
        for (byte type = 0; type < ALL_SURFACES; type++) {
            if (type != LAVA_SURFACE && HasSurfaceOfType(type)) {
                var surface = _surfaces[type];
                ret.AddSurfaceFromArrays(
                    Mesh.PrimitiveType.Triangles,
                    surface.SurfaceGetArrays(0)
                );
            }
        }
        return ret;
    }

    public Dictionary<int,Dictionary<int,List<float>>> SerializeSurfaceData()
    {
        List<float> extract_array_data(Godot.Collections.Array surfaceArray, int arrayType) {
            List<float> result;
            if (arrayType == (int)Mesh.ArrayType.Vertex|| arrayType == (int)Mesh.ArrayType.Normal)
            {
                if ((Vector3[])surfaceArray[arrayType] is Vector3[] pts) {
                    result = [];
                    foreach (var p in pts) {
                        result.Add(p.X);
                        result.Add(p.Y);
                        result.Add(p.Z);
                    }
                    return result;
                }
            }
            else if (arrayType == (int)Mesh.ArrayType.Color)
            {
                if ((Color[])surfaceArray[arrayType] is Color[] metadata) {
                    result = [];
                    foreach (var color in metadata) {
                        result.Add(color.R);
                        result.Add(color.G);
                        result.Add(color.B);
                        result.Add(color.A);
                    }
                    return result;
                }
            }
            else if (arrayType == (int)Mesh.ArrayType.TexUV)
            {
                if ((Vector2[])surfaceArray[arrayType] is Vector2[] texuvs) {
                    result = [];
                    foreach (var uv in texuvs) {
                        result.Add(uv.X);
                        result.Add(uv.Y);
                    }
                    return result;
                }
            }
            else if (arrayType == (int)Mesh.ArrayType.Index)
            {
                if ((int[])surfaceArray[arrayType] is int[] indices) {
                    result = [.. indices];
                    return result;
                }
            }
            return [];
        }

        var result = new Dictionary<int, Dictionary<int,List<float>>>();
        for (byte type = 0; type < ALL_SURFACES; type++) {
            if (HasSurfaceOfType(type)) {
                var surface = _surfaces[type];
                result[type] = [];
                var arrays = surface.SurfaceGetArrays(0);
                result[type].TryAdd((int)Mesh.ArrayType.Vertex, extract_array_data(arrays, (int)Mesh.ArrayType.Vertex));
                result[type].TryAdd((int)Mesh.ArrayType.Normal, extract_array_data(arrays, (int)Mesh.ArrayType.Normal));
                result[type].TryAdd((int)Mesh.ArrayType.TexUV, extract_array_data(arrays, (int)Mesh.ArrayType.TexUV));
                if (type != LAVA_SURFACE) result[type].TryAdd((int)Mesh.ArrayType.Color, extract_array_data(arrays, (int)Mesh.ArrayType.Color));
                if (type == GRASS_SURFACE) result[type].TryAdd((int)Mesh.ArrayType.Index, extract_array_data(arrays, (int)Mesh.ArrayType.Index));
            }
        }
        return result;
    }

    public void ReconstructFromSerializedData(Dictionary<int,Dictionary<int,List<float>>> serializedData)
    {
        foreach (var (surface_type, surface_arrays) in serializedData) {
            var surface = _surfaces[surface_type];
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            foreach (var (arrayType, arrayData) in surface_arrays) {
                if (arrayType == (int)Mesh.ArrayType.Vertex || arrayType == (int)Mesh.ArrayType.Normal)
                {
                    var pts = new Vector3[arrayData.Count / 3];
                    for (int i = 0; i < arrayData.Count; i += 3) {
                        pts[i / 3] = new Vector3(arrayData[i], arrayData[i + 1], arrayData[i + 2]);
                    }
                    arrays[arrayType] = pts;
                }
                else if (arrayType == (int)Mesh.ArrayType.Color)
                {
                    var colors = new Color[arrayData.Count / 4];
                    for (int i = 0; i < arrayData.Count; i += 4) {
                        colors[i / 4] = new Color(arrayData[i], arrayData[i + 1], arrayData[i + 2], arrayData[i + 3]);
                    }
                    arrays[arrayType] = colors;
                }
                else if (arrayType == (int)Mesh.ArrayType.TexUV)
                {
                    var texuvs = new Vector2[arrayData.Count / 2];
                    for (int i = 0; i < arrayData.Count; i += 2) {
                        texuvs[i / 2] = new Vector2(arrayData[i], arrayData[i + 1]);
                    }
                    arrays[arrayType] = texuvs;
                }
                else if (arrayType == (int)Mesh.ArrayType.Index)
                {
                    var indices = new int[arrayData.Count];
                    for (int i = 0; i < arrayData.Count; i++) {
                        indices[i] = (int)arrayData[i];
                    }
                    arrays[arrayType] = indices;
                }
            }
            surface.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            _surfaces[surface_type] = surface;
        }
    }
}

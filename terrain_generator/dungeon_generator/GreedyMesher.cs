using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

public partial class GreedyMesher : Node
{
    private struct GreedyQuad
    {
        public int col; // column offset
        public int row; // row offset
        public int delta_row; // width of quad
        public int delta_col; // height of quad

        public GreedyQuad(int col, int row, int w, int h)
        {
            this.col = col;
            this.row = row;
            this.delta_row = w;
            this.delta_col = h;
        }
    }

    private static readonly Vector3I[] CUBE_VERTS =
        [
            new(0, 0, 0),
            new(1, 0, 0),
            new(0, 1, 0),
            new(1, 1, 0),
            new(0, 0, 1),
            new(1, 0, 1),
            new(0, 1, 1),
            new(1, 1, 1)
        ];

    // vertices for a square face of the above, cube depending on axis
    // axis has 2 entries for each coordinate - y, x, z and alternates between -/+
    // axis 0 = down, 1 = up, 2 = right, 3 = left, 4 = front (-z is front in godot), 5 = back
    private static readonly int[,] CUBE_AXIS =
        {
            {0, 4, 5, 1}, // bottom
            {2, 3, 7, 6}, // top
            {6, 4, 0, 2}, // left
            {3, 1, 5, 7}, // right
            {2, 0, 1, 3}, // front
            {7, 5, 4, 6}  // back
        };

    private static readonly Vector3[] AXIS_NORMALS = 
    [
        Vector3.Down, // -y
        Vector3.Up,   // +y
        Vector3.Left, // -x
        Vector3.Right, // +x
        Vector3.Forward, // -z is forward in godot
        Vector3.Back     // +z
    ];

    public static uint[] GenBlocks(Vector3I chunkPosition, int CHUNK_SIZE, Noise sample_noise = default)
    {
        int CSP = CHUNK_SIZE + 2; // chunk size with padding
        int CSP2 = CSP * CSP; // chunk size squared with padding
        int CSP3 = CSP2 * CSP; // chunk size cubed with padding

        int BlockIndex(Vector3I blockPaddedPosition)
        {
            return blockPaddedPosition.X + blockPaddedPosition.Z * CSP + blockPaddedPosition.Y * CSP2;
        }

        var chunk_blocks = new uint[CSP3];


        if (sample_noise == default)
        {
            for (int i = 0; i < CSP3; i++)
            {
                if (i / CSP2 < 3) chunk_blocks[i] = 1;
                else chunk_blocks[i] = 0;
            }
        }
        else
        {
            for (int x = 0; x < CSP; x++)
            {
                for (int y = 0; y < CSP; y++)
                {
                    for (int z = 0; z < CSP; z++)
                    {

                        var block_local_pos = new Vector3I(x, y, z) - Vector3I.One;
                        var globalBlockPosition = chunkPosition * CHUNK_SIZE + block_local_pos;

                        var idx = BlockIndex(new Vector3I(x, y, z));
                        var noise_value = sample_noise.GetNoise3D(globalBlockPosition.X, globalBlockPosition.Y, globalBlockPosition.Z);
                        if (noise_value > 0.0f)
                        {
                            chunk_blocks[idx] = 1; // solid block
                        }
                        else
                        {
                            chunk_blocks[idx] = 0; // empty block
                        }
                    }
                }
            }
        }
        return chunk_blocks;
    }

    public static void ChunkSpatialMap(int CHUNK_SIZE, Dictionary<uint, Dictionary<int, uint[]>>[] data, uint[] chunk_blocks)
    {
        int CSP = CHUNK_SIZE + 2; // chunk size with padding
        int CSP2 = CSP * CSP; // chunk size squared with padding
        int CSP3 = CSP2 * CSP; // chunk size cubed with padding

        int BlockIndex(Vector3I blockPaddedPosition) => blockPaddedPosition.X + blockPaddedPosition.Z * CSP + blockPaddedPosition.Y * CSP2;
        
        // check for block empty
        bool IsBlockEmpty(uint blockinfo) => blockinfo == 0;

        var axis_cols = new uint[CSP3 * 3];
        var col_face_masks = new uint[CSP3 * 6];
        var slope_blocks = new Dictionary<int, uint[]>();

        // generate binary 0 1 voxel representation for each axis
        // central chunk loop
        for (int x = 0; x < CSP; x++)
        {
            for (int y = 0; y < CSP; y++)
            {
                for (int z = 0; z < CSP; z++)
                {
                    var idx = BlockIndex(new Vector3I(x, y, z));

                    var blockinfo = chunk_blocks[idx];

                    // HACK set blockinfo to zero to prevent sloped air blocks bug
                    if (IsBlockEmpty(blockinfo)) blockinfo = 0;
                    chunk_blocks[idx] = blockinfo;

                    if (!IsBlockEmpty(blockinfo))
                    { // if block is solid
                        axis_cols[x + z * CSP] |= 1u << y;           // y axis defined by x,z
                        axis_cols[z + y * CSP + CSP2] |= 1u << x;    // x axis defined by z,y
                        axis_cols[x + y * CSP + CSP2 * 2] |= 1u << z;  // z axis defined by x,y
                    }
                }
            }
        }

        // add slope blocks to entry zero of the extra "axis"
        // data 0-5 are the cube axes, 6 is the sloped blocks 
        data[6].Add(0, slope_blocks);

        // do face culling for each axis
        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < CSP2; i++)
            {
                var col = axis_cols[i + axis * CSP2];
                // sample descending axis and set true when air meets solid
                col_face_masks[CSP2 * axis * 2 + i] = col & ~(col << 1);
                // sample ascending axis and set true when air meets solid
                col_face_masks[CSP2 * (axis * 2 + 1) + i] = col & ~(col >> 1);
            }
        }

        // put the data into the hash maps
        for (int axis = 0; axis < 6; axis++)
        {
            // i and j are coords in the binary plane for the given axis
            // i is column, j is row
            for (int j = 0; j < CHUNK_SIZE; j++)
            {
                for (int i = 0; i < CHUNK_SIZE; i++)
                {
                    // get column index for col_face_masks
                    // add 1 to i and j because we are skipping the first row and column due to padding
                    var col_idx = (i + 1) + ((j + 1) * CSP) + (axis * CSP2);

                    // removes rightmost and leftmost padded bit (it's outside the chunk)
                    var col = col_face_masks[col_idx] >> 1;
                    col &= ~(1u << CHUNK_SIZE);

                    // now get y coord of faces (it's their bit location in the UInt64, so trailing zeroes can find it)
                    while (col != 0)
                    {
                        var k = System.Numerics.BitOperations.TrailingZeroCount(col);
                        // clear least significant (rightmost) set bit
                        col &= col - 1;

                        var voxel_pos = axis switch
                        {
                            0 or 1 => new Vector3I(i, k, j),  // down, up    (xz -> y axis)
                            2 or 3 => new Vector3I(k, j, i),  // right, left (zy -> x axis)
                            _ => new Vector3I(i, j, k),       // back, front (xy -> z axis)
                        };
                        var blockinfo = chunk_blocks[BlockIndex(voxel_pos + Vector3I.One)];

                        if (!data[axis].TryGetValue(blockinfo, out Dictionary<int, uint[]> planeSet))
                        {
                            planeSet = [];
                            data[axis].Add(blockinfo, planeSet);
                        }

                        if (!planeSet.TryGetValue(k, out uint[] data_entry))
                        {
                            data_entry = new uint[CHUNK_SIZE];
                            planeSet.Add(k, data_entry);
                        }
                        data_entry[j] |= 1u << i;     // push the "row" bit into the "column" UInt32
                        planeSet[k] = data_entry;

                        // =========================================
                        // store the combined mesh (diregarding block type) with blockinfo of uint.MaxValue
                        // use this to make the physics shape
                        // =========================================
                        if (!data[axis].TryGetValue(uint.MaxValue, out var planeSet2))
                        {
                            planeSet2 = [];
                            data[axis].Add(uint.MaxValue, planeSet2);
                        }

                        if (!planeSet2.TryGetValue(k, out var data_entry2))
                        {
                            data_entry2 = new uint[CHUNK_SIZE];
                            planeSet2.Add(k, data_entry2);
                        }
                        data_entry2[j] |= 1u << i;
                        planeSet2[k] = data_entry2;
                    }
                }
            }
        }
    }

    public static ChunkMeshData BuildChunkMesh(int CHUNK_SIZE, uint[] chunk_blocks)
    {
        var chunk_mesh_data = new ChunkMeshData
        {
            StaticBody = new StaticBody3D
            {
                Name = $"Chunk_StaticBody"
            }
        };
        var checked_positions = new HashSet<Vector3I>();

        static int get_surface_tool_index(uint blockinfo, int axis)
        {
            return ChunkMeshData.CHUNK_SURFACE;

            // TODO -- set surface type based on block type 
            /*
            var blockId = GetBlockID(blockinfo);
            if (blockId == BlockManager.Instance.LavaBlockId)
            {
                return ChunkMeshData.LAVA_SURFACE;
            }
            else if (blockId == BlockManager.BlockID("GoldOre"))
            {
                return ChunkMeshData.GOLD_SURFACE;
            }
            else if (axis == 1 && !IsBlockDamaged(blockinfo) && blockId == BlockManager.BlockID("Grass"))
            {
                return ChunkMeshData.GRASS_SURFACE;
            }
            else
            {
                return ChunkMeshData.CHUNK_SURFACE;
            }
            */
        }

        static uint GetBlockID(uint blockInfo)
        {
            return blockInfo & 0xffffu;
        }


        // data is an array of dictionaries, one for each axis
        // each dictionary is a hash map of block types to a set binary planes
        // we need to group by block type like this so we can batch the meshing and texture blocks correctly
        var data = new Dictionary<uint, Dictionary<int, uint[]>>[7];
        short i;
        for (i = 0; i < 6; i++) data[i] = []; // initialize the hash maps for each axis value
        data[i] = []; // an extra one for sloped blocks

        // do binary face culling and construct the binary planes
        ChunkSpatialMap(CHUNK_SIZE, data, chunk_blocks);

        // construct mesh
        var surfToolArray = new SurfaceTool[ChunkMeshData.ALL_SURFACES];
        for (var s = 0; s < ChunkMeshData.ALL_SURFACES; s++)
        {
            surfToolArray[s] = new();
            surfToolArray[s].Begin(Mesh.PrimitiveType.Triangles);
        }

        for (int axis = 0; axis < 6; axis++)
        {
            foreach (var (blockinfo, planeSet) in data[axis])
            {
                foreach (var (k, binary_plane) in planeSet)
                {
                    var blockId = GetBlockID(blockinfo);

                    // sloped blocks are not greedy meshed
                    var greedy_quads = GreedyMeshBinaryPlane(CHUNK_SIZE, binary_plane);

                    foreach (GreedyQuad quad in greedy_quads)
                    {
                        Vector3I quad_offset, quad_delta; // row and col, width and height
                        Vector2 uv_offset;

                        quad_offset = axis switch
                        {
                            // row, col -> axis
                            0 => new Vector3I(quad.col, k, quad.row), // down, up    (xz -> y axis)
                            1 => new Vector3I(quad.col, k + 1, quad.row),
                            2 => new Vector3I(k, quad.row, quad.col), // left, right (zy -> x axis)
                            3 => new Vector3I(k + 1, quad.row, quad.col),
                            4 => new Vector3I(quad.col, quad.row, k), // back, front (xy -> z axis)
                            _ => new Vector3I(quad.col, quad.row, k + 1)  // remember -z is forward in godot, we are still in chunk space so we add 1
                        };

                        quad_delta = axis switch
                        {
                            // row, col -> axis
                            0 or 1 => new Vector3I(quad.delta_col, 0, quad.delta_row),  // down, up    (xz -> y axis)
                            2 or 3 => new Vector3I(0, quad.delta_row, quad.delta_col),  // right, left (zy -> x axis)
                            _ => new Vector3I(quad.delta_col, quad.delta_row, 0),       // back, front (xy -> z axis)
                        };

                        var normal = AXIS_NORMALS[axis];
                        Vector3[] normals = [normal, normal, normal];

                        if (blockinfo == uint.MaxValue) // we stored this specifically to make the collision shape 
                        {
                            // generate collision shape by adding greedy meshed boxes for all blocks
                            // you only need to do this for one axis
                            // (sloped and stair blocks you will need to handle differently)
                            var halfstep = (quad_delta - normal).Sign() * 0.5f;
                            var start = (Vector3I)(quad_offset + halfstep);
                            var end = (Vector3I)(quad_offset + quad_delta - normal - halfstep);

                            if (end.X < start.X) (start.X, end.X) = (end.X, start.X);
                            if (end.Y < start.Y) (start.Y, end.Y) = (end.Y, start.Y);
                            if (end.Z < start.Z) (start.Z, end.Z) = (end.Z, start.Z);

                            var should_add = false;

                            for (int x = start.X; x <= end.X; x++)
                            {
                                for (int y = start.Y; y <= end.Y; y++)
                                {
                                    for (int z = start.Z; z <= end.Z; z++)
                                    {
                                        var pos = new Vector3I(x, y, z);
                                        if (!checked_positions.Contains(pos))
                                        {
                                            should_add = true;
                                            checked_positions.Add(pos);
                                        }
                                    }
                                }
                            }

                            if (should_add)
                            {
                                // // test mesh instance 3d -- uncomment to visualize the collision shapes
                                // var test = new MeshInstance3D()
                                // {
                                //     Mesh = new BoxMesh
                                //     {
                                //         Size = quad_delta + normal.Abs(),
                                //         Material = new StandardMaterial3D
                                //         {
                                //             AlbedoColor = new Color(axis == 0 || axis == 1 ? 1 : 0, axis == 2 || axis == 3 ? 1 : 0, axis == 4 || axis == 5 ? 1 : 0, 0.5f),
                                //             Transparency = BaseMaterial3D.TransparencyEnum.Alpha
                                //         }
                                //     },
                                //     Position = quad_offset + (quad_delta - normal) * 0.5f,
                                // };
                                // test.Scale *= 0.95f;
                                // chunk_mesh_data.StaticBody.AddChild(test);

                                var shape = new CollisionShape3D()
                                {
                                    Shape = new BoxShape3D
                                    {
                                        Size = quad_delta + normal.Abs()
                                    },
                                    Position = quad_offset + (quad_delta - normal) * 0.5f,
                                    DebugFill = true,
                                    DebugColor = new Color(axis == 0 || axis == 1 ? 1 : 0, axis == 2 || axis == 3 ? 1 : 0, axis == 4 || axis == 5 ? 1 : 0, 0.5f)
                                };
                                chunk_mesh_data.StaticBody.AddChild(shape);
                            }
                        }
                        else // if not the key uint.MaxValue, then we are dealing with a regular block and we add it with the surface tool
                        {
                            // construct vertices for mesh
                            Vector3[] verts = new Vector3[4];
                            for (i = 0; i < 4; i++)
                            {
                                verts[i] = quad_offset + (Vector3)CUBE_VERTS[CUBE_AXIS[axis, i]] * quad_delta;
                            }

                            Vector3[] triangle1 = [verts[0], verts[1], verts[2]];
                            Vector3[] triangle2 = [verts[0], verts[2], verts[3]];

                            uv_offset = axis switch
                            {
                                0 => new Vector2(quad_delta.X, quad_delta.Z), // down, up    (xz -> y axis)
                                1 => new Vector2(quad_delta.Z, quad_delta.X), // for some reason y is flipped on the top face???? :( 
                                2 or 3 => new Vector2(quad_delta.Z, quad_delta.Y), // right, left (zy -> x axis)
                                _ => new Vector2(quad_delta.X, quad_delta.Y),      // back, front (xy -> z axis)
                            };
                            
                            Vector2 uvA, uvB, uvC, uvD;
                            (uvA, uvB, uvC, uvD) = (Vector2.Zero, new Vector2(0, 1), Vector2.One, new Vector2(1, 0));

                            var uvTriangle1 = new Vector2[] { uvA, uvB, uvC };
                            var uvTriangle2 = new Vector2[] { uvA, uvC, uvD };

                            var surfidx = get_surface_tool_index(blockinfo, axis);
                            if (surfidx == ChunkMeshData.LAVA_SURFACE)
                            {
                                // lava surface has no metadata
                                surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, normals: normals);
                                surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, normals: normals);
                            }
                            else
                            {
                                // TODO set block data properly
                                var blockDamage = 0; //GetBlockDamageData(blockinfo);
                                var block_face_texture_idx = BlockManager.BlockTextureArrayPositions(blockId)[axis];
                                var notacolour = new Color(block_face_texture_idx, uv_offset.X, uv_offset.Y, blockDamage) * (1 / 255f);
                                var metadata = new Color[] { notacolour, notacolour, notacolour };
                                surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
                                surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
                            }
                        }
                    }
                }
            }
        }

        // sloped blocks are not greedy meshed, but constucted seperately
        // their data is stored in the 7th dictionary
        // foreach (var (block_idx, blockdata) in data[6][0])
        // {
        //     var blockinfo = (int)blockdata[0];
        //     var blockId = GetBlockID(blockinfo);
        //     var slopeType = GetBlockSlopeType(blockinfo);
        //     var flipSlope = GetBlockSlopeFlip(blockinfo);

        //     // two types of slope, regular slope (id:1) or angled (7 face) corner slope (id:2)
        //     // all blocks in this set are sloped so it's either going to be 1 or 2
        //     var regularSlope = slopeType == (int)SlopeType.Side;
        //     var cornerSlope = slopeType == (int)SlopeType.Corner;
        //     var invCornerSlope = slopeType == (int)SlopeType.InvCorner;
        //     float rotation_angle = GetBlockSlopeRotation(blockinfo);
        //     //rotation_angle += Mathf.Pi/2;
        //     while (rotation_angle > Mathf.Pi * 2) rotation_angle -= Mathf.Pi * 2;
        //     // DEBUG no flip slope
        //     //if (flipSlope && !regularSlope) rotation_degrees -= 90f;
        //     /*
        //         var x = chunk_idx % CHUNK_SIZE;
        //         var z = (chunk_idx / CHUNK_SIZE) % CHUNK_SIZE;
        //         var y = chunk_idx / CHUNKSQ;*/
        //     Vector3I pos = BlockIndexToVector(block_idx);//new(x,y,z);//BlockIndexToVector(chunk_idx);
        //     pos -= Vector3I.One; // remove padding

        //     for (int axis = 0; axis < 6; axis++)
        //     {
        //         // regular slope - skip front face because it's a ramp
        //         if (regularSlope && axis == 4) continue;

        //         //pos += quad_offset;

        //         var blockDamage = GetBlockDamageData(blockinfo);
        //         var block_face_texture_idx = BlockManager.BlockTextureArrayPositions(blockId)[axis];
        //         var notacolour = new Color(block_face_texture_idx, 1.0f, 1.0f, blockDamage) * (1 / 255f);
        //         var metadata = new Color[] { notacolour, notacolour, notacolour };

        //         Vector3[] verts = new Vector3[4];

        //         for (i = 0; i < 4; i++)
        //         {
        //             // get local vertex coords
        //             verts[i] = (Vector3)CUBE_VERTS[CUBE_AXIS[axis, i]] - Vector3.One * 0.5f;

        //             // shift down top face into a slope, for regular slope
        //             if (regularSlope && axis == 1 && (i == 0 || i == 1)) verts[i] -= Vector3.Up;
        //             if (cornerSlope && axis == 1 && (i == 0 || i == 1 || i == 2)) verts[i] -= Vector3.Up; // else shift corner down by 1 for corner slopes
        //             if (invCornerSlope && axis == 1 && i == 1) verts[i] -= Vector3.Up; // else shift corner down by 1


        //             verts[i] = verts[i].Rotated(Vector3.Up, rotation_angle);
        //             if (flipSlope) verts[i] = verts[i].Rotated(Vector3.Forward, Mathf.Pi);
        //             verts[i] += (Vector3)pos + Vector3.One * 0.5f;
        //         }

        //         Vector3[] triangle1 = { verts[0], verts[1], verts[2] };
        //         Vector3[] triangle2 = { verts[0], verts[2], verts[3] };
        //         Vector3 normal = axis switch
        //         {
        //             0 => Vector3.Down,    // -y
        //             1 => Vector3.Up,      // +y
        //             2 => Vector3.Left,    // -x
        //             3 => Vector3.Right,   // +x
        //             4 => Vector3.Forward, // -z is forward in godot
        //             _ => Vector3.Back     // +z
        //         };
        //         if (flipSlope) normal = normal.Rotated(Vector3.Forward, Mathf.Pi);
        //         normal = normal.Rotated(Vector3.Up, rotation_angle);

        //         Vector3[] normals = { normal, normal, normal };

        //         var uvA = Vector2.Zero;
        //         var uvB = new Vector2(0, 1);
        //         var uvC = Vector2.One;
        //         var uvD = new Vector2(1, 0);
        //         var uvTriangle1 = new Vector2[] { uvA, uvB, uvC };
        //         var uvTriangle2 = new Vector2[] { uvA, uvC, uvD };

        //         var surfidx = get_surface_tool_index(blockinfo, axis);
        //         switch (axis)
        //         {
        //             case 1: // top face - modify normals
        //                 if (invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);

        //                 var normrotate = SlopedNormalNegZ;
        //                 if (cornerSlope || invCornerSlope) normrotate = SlopedCornerNormalNegZ;
        //                 if (flipSlope) normrotate = normrotate.Rotated(Vector3.Forward, Mathf.Pi);
        //                 normrotate = normrotate.Rotated(Vector3.Up, rotation_angle);
        //                 normals = new Vector3[] { normrotate, normrotate, normrotate };
        //                 if (regularSlope || invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);

        //                 if (!invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
        //                 break;
        //             case 2: // side face, only add one of the triangles
        //                 if (regularSlope || cornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                 else if (invCornerSlope)
        //                 {
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                     surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
        //                 }
        //                 break;
        //             case 3: // obverse side face, only add one of the triangles and adjust its vertices accordingly
        //                 triangle1 = new Vector3[] { verts[1], verts[2], verts[3] };

        //                 //if (invCornerSlope) uvTriangle1 = new Vector2[] { uvC, uvB, uvA };
        //                 if (regularSlope || invCornerSlope)
        //                 {
        //                     uvTriangle1 = new Vector2[] { uvC, uvB, uvA };
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                 }
        //                 break;
        //             case 4: // facing -z, front, corner slopes only add one triangle, else normal
        //                 if (regularSlope)
        //                 {
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                     surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
        //                 }
        //                 else if (invCornerSlope) surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                 break;
        //             case 5:
        //                 if (regularSlope || invCornerSlope)
        //                 {
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                     surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
        //                 }
        //                 if (cornerSlope)
        //                 {
        //                     triangle1 = new Vector3[] { verts[1], verts[2], verts[3] };
        //                     uvTriangle1 = new Vector2[] { uvC, uvB, uvA };
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                 }
        //                 break;
        //             default: // bottom face is always drawn, corner slopes only have 1 triangle
        //                 if (cornerSlope)
        //                 {
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                 }
        //                 else
        //                 {
        //                     surfToolArray[surfidx].AddTriangleFan(triangle1, uvTriangle1, colors: metadata, normals: normals);
        //                     surfToolArray[surfidx].AddTriangleFan(triangle2, uvTriangle2, colors: metadata, normals: normals);
        //                 }
        //                 break;
        //         }
        //     }
        // }

        // index grass surface
        surfToolArray[ChunkMeshData.GRASS_SURFACE].Index();
        surfToolArray[ChunkMeshData.CHUNK_SURFACE].Index();
        surfToolArray[ChunkMeshData.LAVA_SURFACE].Index();
        surfToolArray[ChunkMeshData.GOLD_SURFACE].Index();
        var surfaces = new ArrayMesh[ChunkMeshData.ALL_SURFACES];
        surfaces[ChunkMeshData.CHUNK_SURFACE] = surfToolArray[ChunkMeshData.CHUNK_SURFACE].Commit();
        surfaces[ChunkMeshData.LAVA_SURFACE] = surfToolArray[ChunkMeshData.LAVA_SURFACE].Commit();
        surfaces[ChunkMeshData.GRASS_SURFACE] = surfToolArray[ChunkMeshData.GRASS_SURFACE].Commit();
        surfaces[ChunkMeshData.GOLD_SURFACE] = surfToolArray[ChunkMeshData.GOLD_SURFACE].Commit();

        chunk_mesh_data.SetSurfaces(surfaces);

        return chunk_mesh_data;
    }

    /// <summary>
    // greedy quad for a 32 x 32 binary plane (assuming data length is 32) // CHANGED THIS TO 64 CHUNK SIZE
    // each Uint32 in data[] is a row of 32 bits
    // offsets along this row represent columns
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private static List<GreedyQuad> GreedyMeshBinaryPlane(int CHUNK_SIZE, uint[] data)
    { // modify this so chunks are 30 and padded 1 on each side to 32
        List<GreedyQuad> greedy_quads = [];
        int data_length = data.Length;
        for (int j = 0; j < data_length; j++)
        { // j selects a row from the data[j]
            var i = 0; // i  traverses the bits in current row j
            while (i < CHUNK_SIZE)
            {
                i += System.Numerics.BitOperations.TrailingZeroCount(data[j] >> i);
                if (i >= CHUNK_SIZE) continue;
                var h = System.Numerics.BitOperations.TrailingZeroCount(~(data[j] >> i)); // count trailing ones from i upwards
                uint h_as_mask = 0; // create a mask of h bits
                for (int xx = 0; xx < h; xx++) h_as_mask |= 1u << xx;
                var mask = h_as_mask << i; // a mask of h bits starting at i
                var w = 1;
                while (j + w < data_length)
                {
                    var next_row_h = (data[j + w] >> i) & h_as_mask; // check next row across
                    if (next_row_h != h_as_mask) break; // if we can't expand aross the row, break
                    data[j + w] &= ~mask;  // if we can, we clear bits from next row so they won't be processed again
                    w++;
                }
                greedy_quads.Add(new GreedyQuad { row = j, col = i, delta_row = w, delta_col = h });
                i += h; // jump past the ones to check if there are any more in this column
            }
        }
        return greedy_quads;
    }
}

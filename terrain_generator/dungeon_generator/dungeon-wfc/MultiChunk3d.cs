using Godot;
using System;
using System.Collections.Generic;

public partial class MultiChunk3d : Node
{
    private Dictionary<Vector3I, Chunk3d> _chunks = [];
    private int _map_rotation = 0; // 0 - 3 describes the ninety degree rotations of this tile in the map


    
}

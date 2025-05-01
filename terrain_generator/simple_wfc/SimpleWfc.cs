using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public static class SimpleWfc
{
    const int North = 0x1;
    const int East = 0x2;
    const int South = 0x3;
    const int West = 0x4;
    private static readonly Node3D _tile_path_nodes = GD.Load<PackedScene>("res://terrain_generator/simple_wfc/TilePaths.tscn").Instantiate<Node3D>();

    private static readonly Dictionary<Vector2I,int> _cell_walls = new()
    {
        { new Vector2I(0, -1), North }, // N
        { new Vector2I(1, 0), East },  // E
        { new Vector2I(0, 1), South },  // S
        { new Vector2I(-1, 0), West }  // W
    };

    private static readonly Dictionary<Vector2I, int> _generated_tiles = new() { { new(0, 0), (int)WallDirections.None } };

    public static Path3D[] GetTilePaths(int tileID)
    {
        var dead_ends = _tile_path_nodes.GetNode("DeadEnds").GetChildren().OfType<Path3D>().ToArray();
        Path3D[] ret = tileID switch
        {
            (int)WallDirections.None => [.. _tile_path_nodes.GetNode("Intersection").GetChildren().OfType<Path3D>()],
            (int)WallDirections.N => [.. _tile_path_nodes.GetNode("TJunctions/TJunction3").GetChildren().OfType<Path3D>()],
            (int)WallDirections.E => [.. _tile_path_nodes.GetNode("TJunctions/TJunction1").GetChildren().OfType<Path3D>()],
            (int)WallDirections.NE => [.. _tile_path_nodes.GetNode("CornerPieces/Corner3").GetChildren().OfType<Path3D>()],
            (int)WallDirections.S => [.. _tile_path_nodes.GetNode("TJunctions/TJunction0").GetChildren().OfType<Path3D>()],
            (int)WallDirections.NS => [.. _tile_path_nodes.GetNode("StraightPaths/StraightPathHor").GetChildren().OfType<Path3D>()],
            (int)WallDirections.ES => [.. _tile_path_nodes.GetNode("CornerPieces/Corner0").GetChildren().OfType<Path3D>()],
            (int)WallDirections.NES => [dead_ends[0]],
            (int)WallDirections.W => [.. _tile_path_nodes.GetNode("TJunctions/TJunction2").GetChildren().OfType<Path3D>()],
            (int)WallDirections.NW => [.. _tile_path_nodes.GetNode("CornerPieces/Corner1").GetChildren().OfType<Path3D>()],
            (int)WallDirections.EW => [.. _tile_path_nodes.GetNode("StraightPaths/StraightPathVert").GetChildren().OfType<Path3D>()],
            (int)WallDirections.NEW => [dead_ends[1]],
            (int)WallDirections.SW => [.. _tile_path_nodes.GetNode("CornerPieces/Corner2").GetChildren().OfType<Path3D>()],
            (int)WallDirections.NSW => [dead_ends[2]],
            (int)WallDirections.ESW => [dead_ends[3]],
            (int)WallDirections.All => [],
            _ => throw new ArgumentOutOfRangeException(nameof(tileID), tileID, null)
        };
        return ret;
    }

    public static Path3D[] GetTilePaths(Vector2I cell)
    {
        return GetTilePaths(GetTileID(cell));
    }

    public static int GetTileID(Vector2I cell)
    {
        if (_generated_tiles.TryGetValue(cell, out var tileID))
        {
            return tileID;
        }
        else
        {
            GenerateTile(cell);
            return _generated_tiles[cell];
        }
    }

    public static float GetTileTreasureGenerationChance(int tile_id)
    {
        if (IsCorner(tile_id))            return 0.0f;
        else if (IsTJunction(tile_id))    return 0.01f;
        else if (IsDeadEnd(tile_id))      return 0.5f;
        else if (IsStraight(tile_id))     return 0.0f;
        else if (IsIntersection(tile_id)) return 0.3f;
        else if (IsOpen(tile_id))         return 0.3f;
        return 0.0f;
    }

    public static int GetTileFlowerAmount(Random rng, int tile_id)
    {
        var flower_base = 10;
        if (IsCorner(tile_id))            return rng.Next(0,flower_base);
        else if (IsTJunction(tile_id))    return rng.Next(0,flower_base);
        else if (IsDeadEnd(tile_id))      return rng.Next(0,flower_base);
        else if (IsStraight(tile_id))     return rng.Next(0,flower_base);
        else if (IsIntersection(tile_id)) return rng.Next(0,flower_base);
        else if (IsOpen(tile_id))         return rng.Next(flower_base,20);
        return 0;
    }

    public static bool ChunkHasNoWalls(int tile_id)
    {
        return IsOpen(tile_id);
    }

    private static bool IsCorner(int tileID)
    {
        return tileID switch
        {
            (int)WallDirections.NE => true,
            (int)WallDirections.NW => true,
            (int)WallDirections.SW => true,
            (int)WallDirections.ES => true,
            _ => false
        };
    }
    private static bool IsDeadEnd(int tileID)
    {
        return tileID switch
        {
            (int)WallDirections.NES => true,
            (int)WallDirections.NEW => true,
            (int)WallDirections.NSW => true,
            (int)WallDirections.ESW => true,
            _ => false
        };
    }
    private static bool IsStraight(int tileID)
    {
        return tileID switch
        {
            (int)WallDirections.NS => true,
            (int)WallDirections.EW => true,
            _ => false
        };
    }
    private static bool IsIntersection(int tileID)
    {
        return tileID switch
        {
            (int)WallDirections.None => true,
            _ => false
        };
    }
    private static bool IsTJunction(int tileID)
    {
        return tileID switch
        {
            (int)WallDirections.N => true,
            (int)WallDirections.E => true,
            (int)WallDirections.S => true,
            (int)WallDirections.W => true,
            _ => false
        };
    }
    private static bool IsOpen(int tileID)
    {
        return tileID switch
        {
            (int)WallDirections.All => true,
            _ => false
        };
    }

    private static void GenerateTile(Vector2I cell)
    {
        var cells = FindValidCells(cell);
        _generated_tiles[cell] = cells[Random.Shared.Next()%cells.Length];
    }

    private static int[] FindValidCells(Vector2I cell)
    {
        var valid_cells = new List<int>();

        for (int i=0; i<16;i++)
        {
            var is_match = false;
            foreach (var (neighbour,dir) in _cell_walls)
            {
                if (_generated_tiles.TryGetValue(cell + neighbour, out var neighbor_cell))
                {
                    is_match = (neighbor_cell & _cell_walls[neighbour])/_cell_walls[neighbour] == (i & dir)/dir;
                }
                else is_match = true;
            }
            if (is_match && !valid_cells.Contains(i))
            {
                valid_cells.Add(i);
            }
        }

        return [.. valid_cells];
    }

    private enum WallDirections
    {
        None,
        N,
        E,
        NE,
        S,
        NS,
        ES,
        NES,
        W,
        NW,
        EW,
        NEW,
        SW,
        NSW,
        ESW,
        All
    }
}

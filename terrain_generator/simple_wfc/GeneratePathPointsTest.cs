using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class GeneratePathPointsTest : Node3D
{

    [Export] public bool Init
    {
        get => false;
        set
        {
            RunInit();
        }
    }

    public void RunInit()
    {
        foreach (Node n in GetChildren())
        {
            if (n is Node3D) n.QueueFree();
        }

        // do corner pieces
        var cornerNE = CornerPiece();
        var cornerNW = new List<Vector2I>[] {Rotate90(cornerNE[0]), Rotate90(cornerNE[1])};
        var cornerSW = new List<Vector2I>[] {Rotate90(cornerNW[0]), Rotate90(cornerNW[1])};
        var cornerSE = new List<Vector2I>[] {Rotate90(cornerSW[0]), Rotate90(cornerSW[1])};
        var cornerPieces = new List<List<Vector2I>[]>(){cornerNE, cornerNW, cornerSW, cornerSE};

        var corner_piece_node = new Node3D();
        AddChild(corner_piece_node);
        corner_piece_node.Name = "CornerPieces";
        corner_piece_node.Owner = GetTree().EditedSceneRoot;

        var i=0;
        foreach (var path_set in cornerPieces)
        {
            var corner = new Node3D();
            corner_piece_node.AddChild(corner);
            corner.Owner = GetTree().EditedSceneRoot;
            corner.Name = $"Corner{i++}";
            foreach (var path in path_set)
            {
                AddPathToNode(corner, path);
            }
        }

        // do dead ends
        var dead_end_node = new Node3D();
        AddChild(dead_end_node);
        dead_end_node.Name = "DeadEnds";
        dead_end_node.Owner = GetTree().EditedSceneRoot;

        var dead_end_N = DeadEnd();
        var dead_end_W = Rotate90(dead_end_N);
        var dead_end_S = Rotate90(dead_end_W);
        var dead_end_E = Rotate90(dead_end_S);

        var dead_ends = new List<Vector2I>[] {dead_end_N, dead_end_W, dead_end_S, dead_end_E};
        foreach (var path in dead_ends)
        {
            AddPathToNode(dead_end_node, path);
        }

        // do straight paths
        var straight_path_node = new Node3D();
        AddChild(straight_path_node);
        straight_path_node.Name = "StraightPaths";
        straight_path_node.Owner = GetTree().EditedSceneRoot;

        var straight_path_vert = StraightPath();
        var straight_path_hor = new List<Vector2I>[] {Rotate90(straight_path_vert[0]), Rotate90(straight_path_vert[1])};

        var straight_path_N = new Node3D();
        straight_path_node.AddChild(straight_path_N);
        straight_path_N.Owner = GetTree().EditedSceneRoot;
        straight_path_N.Name = "StraightPathVert";
        var straight_path_W = new Node3D();
        straight_path_node.AddChild(straight_path_W);
        straight_path_W.Owner = GetTree().EditedSceneRoot;
        straight_path_W.Name = "StraightPathHor";
        foreach (var path in straight_path_vert)
        {
            AddPathToNode(straight_path_N, path);
        }
        foreach (var path in straight_path_hor)
        {
            AddPathToNode(straight_path_W, path);
        }

        // do T junctions
        var t_junction_node = new Node3D();
        AddChild(t_junction_node);
        t_junction_node.Name = "TJunctions";
        t_junction_node.Owner = GetTree().EditedSceneRoot;
        var t_junction_N = TJunction();
        var t_junction_W = new List<Vector2I>[] {Rotate90(t_junction_N[0]), Rotate90(t_junction_N[1]), Rotate90(t_junction_N[2])};
        var t_junction_S = new List<Vector2I>[] {Rotate90(t_junction_W[0]), Rotate90(t_junction_W[1]), Rotate90(t_junction_W[2])};
        var t_junction_E = new List<Vector2I>[] {Rotate90(t_junction_S[0]), Rotate90(t_junction_S[1]), Rotate90(t_junction_S[2])};
        var junction_pieces = new List<List<Vector2I>[]>(){t_junction_N, t_junction_W, t_junction_S, t_junction_E};

        i=0;
        foreach (var path_set in junction_pieces)
        {
            var t_junct = new Node3D();
            t_junction_node.AddChild(t_junct);
            t_junct.Owner = GetTree().EditedSceneRoot;
            t_junct.Name = $"TJunction{i++}";
            foreach (var path in path_set)
            {
                AddPathToNode(t_junct, path);
            }
        }

        // do intersection (all roads)
        var upper_left_inner = UpperLeftInnerCorner();
        var lower_left_inner = Rotate90(upper_left_inner);
        var lower_right_inner = Rotate90(lower_left_inner);
        var upper_right_inner = Rotate90(lower_right_inner);
        var intersection = new List<Vector2I>[] {upper_left_inner, lower_left_inner, lower_right_inner, upper_right_inner};

        var interection_node = new Node3D();
        AddChild(interection_node);
        interection_node.Name = "Intersection";
        interection_node.Owner = GetTree().EditedSceneRoot;
        foreach (var path in intersection)
        {
            AddPathToNode(interection_node, path);
        }
    }

    private static Path3D MakePath(List<Vector2I> points)
    {
        var path = new Path3D();
        var curve = new Curve3D();
        foreach (var point in points)
        {
            curve.AddPoint(new Vector3(point.X, 0, point.Y));
        }
        path.Curve = curve;
        return path;
    }

    private void AddPathToNode(Node3D node, List<Vector2I> points)
    {
        var path = MakePath(points);
        node.AddChild(path);
        path.Owner = GetTree().EditedSceneRoot;
    }

    private static readonly Vector2I[] _upperLeftInnerCorner =
    [
        new (-16,-8),
        new (-8, -8),
        new (-8, -16),
    ];

    private static readonly Vector2I[] _cornerPiece =
    [
        // outer
        new (-8, -16),
        new (-8, 8),
        new (16, 8),
        // inner
        new (8, -16),
        new (8, -8),
        new (16, -8),
    ];

    private static readonly Vector2I[] _straightPath =
    [
        //leftSide
        new (-8, -16),
        new (-8, 16),
        //rightSide
        new (8, -16),
        new (8, 16),
    ];

    private static readonly Vector2I[] _deadEnd =
    [
        new (-8, -16),
        new (-8, 8),
        new (8, 8),
        new (8, -16),
    ];

    private static readonly Vector2I[] _t_junction =
    [
        // upper left corner
        new (-16,-8),
        new (-8, -8),
        new (-8, -16),

        // upper right corner
        new (8, -16),
        new (8, -8),
        new (16, -8),

        // horizontal base
        new (-16, 8),
        new (16, 8),
    ];

    private static List<Vector2I> UpperLeftInnerCorner()
    {
        var seg1 = ConnectPoints(_upperLeftInnerCorner[0], _upperLeftInnerCorner[1]);
        var seg2 = ConnectPoints(_upperLeftInnerCorner[1], _upperLeftInnerCorner[2]);
        seg1.AddRange(seg2);
        return seg1;
    }

    private static List<Vector2I>[] CornerPiece()
    {
        var seg1 = ConnectPoints(_cornerPiece[0], _cornerPiece[1]);
        var seg2 = ConnectPoints(_cornerPiece[1], _cornerPiece[2]);
        seg1.AddRange(seg2);
        var seg3 = ConnectPoints(_cornerPiece[3], _cornerPiece[4]);
        var seg4 = ConnectPoints(_cornerPiece[4], _cornerPiece[5]);
        seg3.AddRange(seg4);

        return [seg1, seg3];
    }

    private static List<Vector2I>[] StraightPath()
    {
        var seg1 = ConnectPoints(_straightPath[0], _straightPath[1]);
        var seg2 = ConnectPoints(_straightPath[2], _straightPath[3]);
        return [seg1, seg2];
    }

    private static List<Vector2I> DeadEnd()
    {
        var seg1 = ConnectPoints(_deadEnd[0], _deadEnd[1]);
        var seg2 = ConnectPoints(_deadEnd[1], _deadEnd[2]);
        var seg3 = ConnectPoints(_deadEnd[2], _deadEnd[3]);
        seg1.AddRange(seg2);
        seg1.AddRange(seg3);
        return seg1;
    }

    private static List<Vector2I>[] TJunction()
    {
        var seg1 = ConnectPoints(_t_junction[0], _t_junction[1]);
        var seg2 = ConnectPoints(_t_junction[1], _t_junction[2]);
        seg1.AddRange(seg2);

        var seg3 = ConnectPoints(_t_junction[3], _t_junction[4]);
        var seg4 = ConnectPoints(_t_junction[4], _t_junction[5]);
        seg3.AddRange(seg4);
        
        var seg5 = ConnectPoints(_t_junction[6], _t_junction[7]);

        return [seg1, seg3, seg5];
    }

    private static Vector2I Rotate90(Vector2I v)
    {
        return new Vector2I(-v.Y, v.X);
    }

    private static List<Vector2I> Rotate90(List<Vector2I> points)
    {
        var ret = new List<Vector2I>();
        foreach (var point in points)
        {
            ret.Add(Rotate90(point));
        }
        return ret;
    }

    private static List<Vector2I> ConnectPoints(Vector2I a, Vector2I b)
    {
        var ret = new List<Vector2I>();
        var v = a;
        ret.Add(v);
        while (v != b)
        {
            if (v.X < b.X) v.X++;
            else if (v.X > b.X) v.X--;
            if (v.Y < b.Y) v.Y++;
            else if (v.Y > b.Y) v.Y--;
            ret.Add(v);
        }
        ret.Add(b);
        return ret;
    }
}

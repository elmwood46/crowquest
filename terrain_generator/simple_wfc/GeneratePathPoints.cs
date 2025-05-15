using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[Tool]
public partial class GeneratePathPoints : Node3D
{
    private static readonly PackedScene _csg_wall_scene = GD.Load<PackedScene>("res://terrain_generator/procedural_brick_wall/csg/csg_brick_wall.tscn"); 
    private static readonly PackedScene _phys_body_tracker = GD.Load<PackedScene>("res://enemies/bullets/PhysBodyTracker.tscn");
    private static readonly MeshConvexDecompositionSettings _decomp_settings = new()
    {
        ConvexHullApproximation = true,
        ConvexHullDownsampling = 1u,
        PlaneDownsampling = 1u,
        MaxConvexHulls = 8u,
        Resolution = 50000u,
        MaxNumVerticesPerConvexHull = 64u,
        MaxConcavity = 0.0f
    };

    private static Vector2 _path_mesh_dimensions = new(1.25f, 5f);

    [Export] public bool CanRunMethods {get;set;} = false;
    [ExportToolButton("Clear Child Nodes",Icon = "Node3D")] public Callable ClearChildrenCallable => Callable.From(ClearChildren);
    [ExportToolButton("Create Paths",Icon = "Path3D")] public Callable InitCallable => Callable.From(RunInit);
    [ExportToolButton("Clear Child Nodes Except Intersection")] public Callable ClearChildrenXIntCallable => Callable.From(()=>{
        if (!CanRunMethods) return;
        foreach (Node n in GetChildren())
        {
            if (n.Name != "Intersection")
            {
                n.QueueFree();
            }
        }
    });

    [ExportToolButton("Generate CSG Static Bodies")] public Callable GenCsgCallable => Callable.From(GenCsgStaticBodies);

    [ExportToolButton("Add Trackers")] public Callable AddTrackersCallable => Callable.From(()=>{
        if (!CanRunMethods) return;
        foreach (var n in GetChildren())
        {
            if (n is Node3D d)
            {
                bool gen_paths = true;
                foreach (var n2 in n.GetChildren())
                {
                    if (n2 is Node3D d1 && d1 is not StaticBody3D)
                    {
                        gen_paths = false;
                        foreach (var n3 in d1.GetChildren())
                        {
                            if (n3 is StaticBody3D sb)
                            {
                                var phys_tracker = _phys_body_tracker.Instantiate<Node>();
                                sb.AddChild(phys_tracker);
                                phys_tracker.Owner = GetTree().EditedSceneRoot;
                            }
                        }
                    }
                }
                if (gen_paths)
                {
                    foreach (var n3 in d.GetChildren())
                    {
                        if (n3 is StaticBody3D sb)
                        {
                            var phys_tracker = _phys_body_tracker.Instantiate<Node>();
                            sb.AddChild(phys_tracker);
                            phys_tracker.Owner = GetTree().EditedSceneRoot;
                        }
                    }
                }
            }
        }
    });

    public void GenCsgStaticBodies()
    {
        if (!CanRunMethods) return;
        void populate_path(Path3D path)
        {
            GD.Print("Generating CSG for path: ", path.Name);
            var csg = _csg_wall_scene.Instantiate<CsgPolygon3D>();
            csg.Name = "CsgWall"+path.Name.ToString();
            path.AddChild(csg);
            csg.Owner = GetTree().EditedSceneRoot;
            csg.PathNode = path.GetPath();

            // var mesh = csg.BakeStaticMesh();
            // var mesh_i = new MeshInstance3D
            // {
            //     Mesh = mesh,
            // };
            // csg.AddChild(mesh_i);
            // mesh_i.Owner = GetTree().EditedSceneRoot;
            // mesh_i.CreateMultipleConvexCollisions(_decomp_settings);
            // var static_child = mesh_i.GetChildren().OfType<StaticBody3D>().FirstOrDefault();
            // static_child.Owner = GetTree().EditedSceneRoot;
            // static_child.Reparent(path.GetParent());
            // mesh_i.Reparent(static_child);
            // csg.QueueFree();
        }

        foreach (var n in GetChildren())
        {
            if (n is Node3D && n is not Path3D)
            {
                foreach (var n2 in n.GetChildren())
                {
                    if (n2 is Path3D path)
                    {
                        populate_path(path);
                        
                    }
                    else if (n2 is Node3D)
                    {
                        foreach (var n3 in n2.GetChildren())
                        {
                            if (n3 is Path3D path2)
                            {
                                populate_path(path2);
                            }
                        }
                    }
                }
            }
        }

        Callable.From(()=>{
            foreach (var n in GetChildren())
            {
                if (n is Node3D d)
                {
                    bool gen_paths = true;
                    foreach (var n2 in n.GetChildren())
                    {
                        if (n2 is Node3D d1 && d1 is not Path3D)
                        {
                            gen_paths = false;
                            GenerateOOBSAlongPath(d1);
                        }
                    }
                    if (gen_paths) GenerateOOBSAlongPath(d);
                }
            }
        }).CallDeferred();

        // foreach (var n in GetChildren())
        // {
        //     if (n is Node3D)
        //     {
        //         foreach (var n2 in n.GetChildren())
        //         {
        //             if (n2 is Path3D path)
        //             {
        //                 path.QueueFree();
                        
        //             }
        //             else if (n2 is Node3D)
        //             {
        //                 foreach (var n3 in n2.GetChildren())
        //                 {
        //                     if (n3 is Path3D path2)
        //                     {
        //                        path2.QueueFree();
        //                     }
        //                 }
        //             }
        //         }
        //     }
        // }
    }

    public static void GenerateOOBSAlongPath(Node3D path_parent)
    {
        var child_idx = 0;
        foreach (var child in path_parent.GetChildren())
        {
            if (child is Path3D path)
            {
                var path_csg_node = path.GetChildren().OfType<CsgPolygon3D>().FirstOrDefault();
                var path_interval = 1.0f;//path_csg_node?.PathInterval ?? 1.0f;
                var oobslist = GeneratePathOBBs(path, path_interval);
                var static_body = new StaticBody3D
                {
                    Name = $"PathOBB{path_parent.Name}{++child_idx}"
                };
                path_parent.AddChild(static_body);
                static_body.Owner = path_parent.GetTree().EditedSceneRoot;
                if (path_csg_node != null && path_csg_node is CsgPolygon3D csg)
                {
                    var mesh = csg.BakeStaticMesh();
                    var mesh_i = new MeshInstance3D
                    {
                        Mesh = mesh,
                        Name = "CsgWall"+path.Name.ToString(),
                    };
                    static_body.AddChild(mesh_i);
                    mesh_i.Owner = path_parent.GetTree().EditedSceneRoot;
                }
                
                var tracker_node = _phys_body_tracker.Instantiate<Node>();
                static_body.AddChild(tracker_node);
                tracker_node.Owner = path_parent.GetTree().EditedSceneRoot;

                var i=0;
                foreach (var (transform, size) in oobslist)
                {
                    //GD.Print(aabb.Position, aabb.Size);
                    var box = new CollisionShape3D
                    {
                        Shape = new BoxShape3D
                        {
                            Size = size, // aabb.Size,
                        }
                    };

                    if (i == 0) static_body.AddChild(box);
                    else 
                    {
                        var sb = new StaticBody3D
                        {
                            Name = $"PathOBB{path_parent.Name}{child_idx}_{i+1}"
                        };
                        path_parent.AddChild(sb);
                        sb.Owner = path_parent.GetTree().EditedSceneRoot;
                        
                        var trck = _phys_body_tracker.Instantiate<Node>();
                        sb.AddChild(trck);
                        trck.Owner = path_parent.GetTree().EditedSceneRoot;
                        sb.AddChild(box);
                    }

                    box.GlobalTransform = transform;
                    box.Position += transform.Basis*size*0.5f*new Vector3(-1,1,-1);
                    box.Owner = path_parent.GetTree().EditedSceneRoot;
                    i++;
                }
                path.QueueFree();
            }
        }
    }

    private static (Transform3D, Vector3) CreatePathOBB(Vector3 start_pos, Vector3 dir, float len)
    {
        var size = new Vector3(_path_mesh_dimensions.X, _path_mesh_dimensions.Y, len);
        var rot = Basis.LookingAt(dir, Vector3.Up);
        var offset = new Vector3(0,0,-1)*size.X*0.5f;//Vector3.Zero;//dir.Rotated(Vector3.Up, -Mathf.Pi / 2f) * size.X * 0.5f; // DEBUG fix offset
        return (new Transform3D(rot, start_pos + rot.Rotated(Vector3.Up,-Mathf.Pi/2)*offset), size); // add offset to size
    }

    private static List<(Transform3D,Vector3)> GeneratePathOBBs(Path3D path, float path_interval)
    {
        //var rad_angle_cutoff = Mathf.DegToRad(deg_angle_cutoff);
        path.Curve.BakeInterval = 1.0f;
        var points = path.Curve.GetBakedPoints();
        if (points.Length<=1) return [];

        var selected_points = new List<Vector3>();

        for (int i=0;i<points.Length-1;i+=(int)path_interval)
        {
            selected_points.Add(points[i]);
        }
        if (points.Length % path_interval != 0)
        {
            selected_points.Add(points[^1]);
        }

        if (selected_points.Count <= 1) return [];

        var obbs = new List<(Transform3D,Vector3)>();
        var prev_dir = selected_points[1] - selected_points[0];
        var current_length = 0f;
        var start_pos = path.GlobalPosition+selected_points[0];
        for (var i=0; i<selected_points.Count-1; i++)
        {
            var next_dir = selected_points[i+1] - selected_points[i];
            if (next_dir != prev_dir)
            {
                if (prev_dir != Vector3.Zero) obbs.Add(CreatePathOBB(start_pos, prev_dir, current_length));
                current_length = 0f;
                start_pos = path.GlobalPosition+selected_points[i];
            }

            current_length += (selected_points[i+1] - selected_points[i]).Length();
            prev_dir = next_dir;

            if (i==selected_points.Count-2 && prev_dir != Vector3.Zero) obbs.Add(CreatePathOBB(start_pos, prev_dir, current_length));
        }
        

        return obbs;
    }

    public void ClearChildren()
    {
        foreach (Node n in GetChildren())
        {
            if (n is Node3D) n.QueueFree();
        }
    }

    public void RunInit()
    {
        if (!CanRunMethods) return;
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

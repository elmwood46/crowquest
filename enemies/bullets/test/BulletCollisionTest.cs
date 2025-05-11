using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class BulletCollisionTest : Node3D
{
    [Export] public bool EnableProcessing {
        get => _enable_processing;
        set
        {
            _enable_processing = value;
            if (_enable_processing) _prev_sphere_position = Sphere.GlobalPosition;
        }
    }

    [Export] public bool GenPathMeshes {
        get => false;
        set
        {
            ClearPathMeshes = true;
            GenerateMeshesAlongPath();
        }
    }

    [Export] public bool ClearPathMeshes {
        get => false;
        set
        {
            foreach (var child in GetChildren())
            {
                if (child is Path3D path)
                {
                    foreach (var pc in path.GetChildren())
                    {
                        if (pc is MeshInstance3D) pc.QueueFree();
                    }
                }
            }
        }
    }

    [Export] public bool GenCSGStaticBodies {
        get => false;
        set
        {
            CreateCSGStaticBodies();
        }
    }

    [Export] public bool ClearCSGStaticBodies {
        get => false;
        set
        {
            foreach (var child in GetChildren())
            {
                if (child is StaticBody3D sb && !_exclude_node_names.Contains(child.Name.ToString()))
                {
                    sb.QueueFree();
                }
                else if (child is Path3D path)
                {
                    foreach (var path_child in path.GetChildren())
                    {
                        if (path_child is StaticBody3D s)
                        {
                            s.QueueFree();
                        }
                    }
                } 
            }
        }
    }

    [Export] public Vector2 PathMeshDimensions {
        get => _path_mesh_dimensions;
        set
        {
            _path_mesh_dimensions = value;
            ClearPathMeshes = true;
            GenerateMeshesAlongPath();
        }
    }
    private static Vector2 _path_mesh_dimensions = new(0.2f, 5f);
    [Export] public bool VisualizeAabbs {
        get => _visualize_aabbs;
        set
        {
            _visualize_aabbs = value;
            if (_visualize_aabbs) AabbVisualizer();
            else ClearAabbs = true;
        }
    }
    private bool _visualize_aabbs = false;

    [Export] public bool ClearAabbs {
        get => false;
        set
        {
            foreach (var child in GetChildren())
            {
                if (child is MeshInstance3D mesh && mesh.Name.ToString().Contains("AabbVisualizer"))
                {
                    mesh.QueueFree();
                }
            }
        }
    }

    [Export] public float BulletRadius // must match multimesh's mesh sphere radius (default 0.25)
        {
            get => _bullet_radius;
            private set
            {
                _bullet_radius = value;

                _bullet_collision_box = new Vector3[CUBE_VERTS.Length];
                for (int i=0;i<CUBE_VERTS.Length;i++)
                {
                    _bullet_collision_box[i] = value*2f*CUBE_VERTS[i];
                }
            }
        }
    private float _bullet_radius = 0.25f;
    private Vector3[] _bullet_collision_box = new Vector3[CUBE_VERTS.Length];

    [Export] public MeshInstance3D Sphere;

    private bool _enable_processing = false;
    private Timer _emit_bullets_timer;

    private Vector3 _prev_sphere_position;
    public static readonly List<Aabb> AabbsList = [];
    private static readonly List<PhysBodyTrackerData> _tracked_csg_meshes = [];
    private MeshInstance3D _test_sphere_collision_mesh;
    private static readonly ConvexPolygonShape3D _sphere_convex_shape = GD.Load<ConvexPolygonShape3D>("res://enemies/bullets/test/bulletConvexShape.tres");
    private static readonly Vector3[] _sphere_convex_shape_verts = _sphere_convex_shape.Points;
    [Export] public CollisionShape3D TestConvexHullShape;

    // used to collide bullet with convex hulls (bullet uses a box collision for these bc its small)
    // it's centered around the origin and scaled by the bullet radius
    private static readonly Vector3[] CUBE_VERTS = 
        {
            new(-1f, -1f, -1f),
            new(1f, -1f, -1f),
            new(-1f, 1f, -1f),
            new(1f, 1f, -1f),
            new(-1f, -1f, 1f),
            new(1f, -1f, 1f),
            new(-1f, 1f, 1f),
            new(1f, 1f, 1f)
        };

    public override void _Ready()
    {
        _prev_sphere_position = Sphere.GlobalPosition;

        if (Engine.IsEditorHint()) return;

        ClearPathMeshes = true;
        GenPathMeshes = true;
        ClearCSGStaticBodies = true;
        GenCSGStaticBodies = true;
        _test_sphere_collision_mesh = Sphere.GetNodeOrNull<MeshInstance3D>("TestSphereCollisionBox");
        var hulltest = PhysBodyTrackerData.Create(TestConvexHullShape.GlobalTransform, TestConvexHullShape.Shape);
        _tracked_csg_meshes.Add(hulltest);
    }

    public override void _Process(double delta)
    {
        if (!EnableProcessing) return;

        if (_prev_sphere_position != Sphere.GlobalPosition) 
        {
            _prev_sphere_position = Sphere.GlobalPosition;
            CollisionTest();
        }

        if (_visualize_aabbs) AabbVisualizer();
    }

    public void CollisionTest()
    {
        var sphere_radius = ((SphereMesh)Sphere.Mesh).Radius;
        ChangeAlbedo(false);

        foreach (var child in GetChildren())
        {
            if (child is StaticBody3D body)
            {
                if (body.GetNode<CollisionShape3D>("CollisionShape3D") is CollisionShape3D shape)
                {
                    if (shape.Shape is SphereShape3D sphere)
                    {
                        var static_sphere_radius = sphere.Radius*shape.GlobalTransform.Basis.Scale.X;
                        var collide_with_sphere = SphereToSphere(Sphere.GlobalPosition, sphere_radius, body.GlobalPosition, static_sphere_radius);
                        if (collide_with_sphere) ChangeAlbedo(collide_with_sphere, Colors.Lime);
                    }
                    else if (shape.Shape is CapsuleShape3D capsule)
                    {
                        var collide_with_capsule = SphereToCapsule(
                            Sphere.GlobalPosition,
                            sphere_radius,
                            shape.GlobalTransform,
                            capsule.Radius,
                            capsule.Height);
                        if (collide_with_capsule) ChangeAlbedo(collide_with_capsule, Colors.Purple);
                    }
                    else if (shape.Shape is BoxShape3D box)
                    {
                        var collide_with_box = SphereToOBB(Sphere.GlobalPosition, sphere_radius, shape.GlobalTransform, box.Size);
                        if (collide_with_box) ChangeAlbedo(collide_with_box, Colors.Orange);
                    }
                }
            }

            if (child is Path3D p)
            {
                foreach (var path_child in p.GetChildren())
                {
                    if (path_child is MeshInstance3D mesh)
                    {
                        if (mesh.Mesh is BoxMesh box_mesh)
                        {
                            var collide_with_aabb = SphereToAABB(Sphere.GlobalPosition, sphere_radius, mesh.GlobalPosition, box_mesh.Size);
                            if (collide_with_aabb) ChangeAlbedo(collide_with_aabb, Colors.Magenta);
                        }
                    }
                }
            }
        }
        
        if (SphereToConvexHull(Sphere.GlobalPosition))
        {
            ChangeAlbedo(true, Colors.Cyan);
        }
    }

    private static readonly HashSet<string> _exclude_node_names =
    [
        "SphereBody",
        "CapsuleBody",
        "BoxBody",
        "AABB",
        "Ground",
        "ConvexHullTest",
        "Intersection"
    ];
    public void CreateCSGStaticBodies()
    {
        _tracked_csg_meshes.Clear();
        var hulltest = PhysBodyTrackerData.Create(TestConvexHullShape.GlobalTransform, TestConvexHullShape.Shape);
        _tracked_csg_meshes.Add(hulltest);

        // foreach (var child in GetChildren(true))
        // {
        //     if (child is StaticBody3D && !_exclude_node_names.Contains(child.Name.ToString()))
        //     {
        //         child.QueueFree();
        //     }
        // }
        // foreach (var child in GetChildren())
        // {
        //     if (child is Path3D path)
        //     {
        //         foreach (var path_child in path.GetChildren())
        //         {
        //             if (path_child is CsgPolygon3D poly && poly.Mode == CsgPolygon3D.ModeEnum.Path)
        //             {
        //                 var dat = poly.GetMeshes();
        //                 poly.GlobalPosition = Vector3.Zero;
        //                 var transform = (Transform3D)dat[0];
        //                 var mesh = (ArrayMesh)dat[1];
        //                 var static_body =  new StaticBody3D();
        //                 var mesh_instance = new MeshInstance3D
        //                 {
        //                     Mesh = mesh,
        //                     MaterialOverride = new StandardMaterial3D
        //                     {
        //                         Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        //                         AlbedoColor = new Color(1, 0.2f, 0.2f, 0.8f),
        //                     }
        //                 };
                        
        //                 // var collision_shape = new CollisionShape3D
        //                 // {
        //                 //     Shape = mesh.CreateConvexShape(true,true)
        //                 // };
                        
        //                 //var pts = ((ConvexPolygonShape3D)collision_shape.Shape).Points;
        //                 var decom_settings = new MeshConvexDecompositionSettings
        //                 {
        //                     MaxConvexHulls = 32
        //                 };
        //                 static_body.AddChild(mesh_instance);
        //                 AddChild(static_body);
        //                 static_body.GlobalTransform = transform;
        //                 mesh_instance.CreateMultipleConvexCollisions(decom_settings);

        //                 Callable.From(()=>{
                            
                            
        //                     //static_body.AddChild(collision_shape);
        //                     //static_body.AddChild(test_mesh_instance);
        //                     // Callable.From(()=>
        //                     // {
        //                         // visualize collision shapes

        //                         // var pts = mesh_instance.Mesh.CreateMultipleConvex(true,true);
        //                         // ConvexPolygonShape3D

        //                         // var amesh = new ArrayMesh();

        //                         // var trans_verts = ((CsgConvexHullData)phys_tracker).TransformedPoints;
        //                         // Vector3[] vertices = new Vector3[trans_verts.Length];
        //                         // for (int i=0;i<trans_verts.Length;i++)
        //                         // {
        //                         //     vertices[i] = new Vector3(trans_verts[i].X,trans_verts[i].Y,trans_verts[i].Z);
        //                         // }

        //                         // var arrays = new Godot.Collections.Array();
        //                         // arrays.Resize((int)Mesh.ArrayType.Max);
        //                         // arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        //                         // amesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        //                         // var test_mesh_instance = new MeshInstance3D() {
        //                         //     Mesh=amesh,
        //                         //     MaterialOverride = new StandardMaterial3D
        //                         //     {
        //                         //         AlbedoColor = new Color(1,0,0,0.5f),
        //                         //         Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        //                         //     }
        //                         // };
        //                         // static_body.AddChild(test_mesh_instance);

        //                         var counter = 0;
        //                         foreach (var child_shape in mesh_instance.GetChildren(true))
        //                         {
        //                             if (child_shape is CollisionShape3D shape)
        //                             {
        //                                 GD.Print(shape.Name, ++counter);
        //                                 var phys_tracker = PhysBodyTrackerData.Create(shape.GlobalTransform, shape.Shape);
        //                                 _tracked_csg_meshes.Add(phys_tracker);
        //                             }
        //                         }
        //                         foreach (var child_shape in mesh_instance.GetChildren(true)) child_shape.QueueFree();
        //                     // }).CallDeferred();
        //                 }).CallDeferred();
        //             }
        //             else if (path_child is StaticBody3D s)
        //             {
        //                 path_child.QueueFree();
        //             }
        //         }
        //     }
        // }
    }

    public void GenerateMeshesAlongPath()
    {
        foreach (var child in GetChildren())
        {
            if (child is Path3D path)
            {
                var path_interval = path.GetNodeOrNull<CsgPolygon3D>("CSGPolygon3D")?.PathInterval ?? 1.0f;
                // TestPath.Curve.BakeInterval = 1.0f;
                //AabbsList.Clear();
                //AabbsList.AddRange(GeneratePathAABB(path));
                var oobslist = GeneratePathOBBs(path, path_interval);
                foreach (var (transform, size) in oobslist)
                {
                    //GD.Print(aabb.Position, aabb.Size);
                    var box = new MeshInstance3D
                    {
                        Mesh = new BoxMesh
                        {
                            Size = size, // aabb.Size,
                            Material = new StandardMaterial3D
                            {
                                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                                AlbedoColor = new Color(1, 1, 0.2f, 0.5f),
                            }
                        }
                    };
                    path.AddChild(box);
                    box.GlobalTransform = transform;//transform.Translated(transform.Basis*size*0.5f);
                    box.GlobalPosition = transform*(size*new Vector3(-1,1,-1)*0.5f);
                    //box.GlobalPosition = aabb.GetCenter();
                // ResourceSaver.Save((ArrayMesh)box.Mesh, $"res://enemies/bullets/test/test_box{aabb}.mesh");
                }
            }
        }
    }

    private void AddNodeAABBMesh(Node3D node)
    {
        var totalAabb = GetNodeAabb(node);

        if (totalAabb != default)
        {
            var box_node = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Material = new StandardMaterial3D
                    {
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(1, 0.2f, 0.2f, 0.8f),
                    }
                }
            };
            AddChild(box_node);
            box_node.Name = "AabbVisualizer";

            ((BoxMesh)box_node.Mesh).Size = totalAabb.Size;
            box_node.GlobalPosition = totalAabb.GetCenter();
        }
    }

    private void AabbVisualizer()
    {
        foreach (var child in GetChildren())
        {
            if (child is StaticBody3D body)
            {
                AddNodeAABBMesh(body);
            }
            else if (child is Path3D path)
            {
                foreach (var path_child in path.GetChildren())
                {
                    if (path_child is CsgPolygon3D poly)
                    {
                        AddNodeAABBMesh(poly);
                    }
                }
            }
        }
    }

    private static Aabb GetNodeAabb(Node3D node)
    {
        Aabb totalAabb = default;
        bool foundShape = false;

        foreach (var pc in node.GetChildren(true))
        {
            if (pc is Control) continue;

            if (pc is MeshInstance3D vis_instance)
            {
                var aabb = vis_instance.GetAabb();
                if (!foundShape)
                {
                    totalAabb = aabb;
                    foundShape = true;
                }
                else 
                {
                    totalAabb = totalAabb.Merge(aabb);
                }
            }
        }

        return  node.GlobalTransform*totalAabb;
    }

    private void ChangeAlbedo(bool colliding, Color color = default)
    {
        if (Sphere.MaterialOverride is StandardMaterial3D material)
        {
            material.AlbedoColor = colliding ? (color == default ? Colors.Lime : color) : Colors.Red;
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
            //var angle_to = prev_dir.AngleTo(next_dir);

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

    private static List<Aabb> GeneratePathAABB(Path3D path)
    {   
        path.Curve.BakeInterval = 20.0f;
        var points = path.Curve.GetBakedPoints();
        if (points.Length<=1) return [];
        var base_box_extents = new Vector3(_path_mesh_dimensions.X, _path_mesh_dimensions.Y, 1);

        var aabbs = new List<Aabb>();

        void add_aabb(Vector3 start_pos, Vector3 dir, float len)
        {
            var span_scale = new Vector3(1,1,len);
            // negative directions must be offset
            if (dir == Vector3.Left)
            {
                start_pos += Vector3.Left*len;
            }
            else if (dir == Vector3.Forward)
            {
                start_pos += Vector3.Forward*len;
            }
            var aabb = new Aabb(
                start_pos,
                AbsVec(Basis.LookingAt(dir, Vector3.Up)*(base_box_extents*span_scale))
            );
            //aabb.Position = aabb.GetCenter();
            aabbs.Add(aabb);
        }
        
        if (points.Length>1) 
        {
            var prev_dir = SnapToAxis(points[1] - points[0]);
            var current_length = 0f;
            var start_pos = path.GlobalPosition+points[0];
            // foreach (var point in points) GD.Print(point);
            for (var i=0; i<points.Length-1; i++)
            {
                var next_dir = SnapToAxis(points[i+1] - points[i]);

                if (next_dir != prev_dir)
                {
                    if (prev_dir != Vector3.Zero) add_aabb(start_pos, prev_dir, current_length);
                    current_length = 0f;
                    start_pos = path.GlobalPosition+points[i];
                }

                current_length += (points[i+1] - points[i]).Length();
                prev_dir = next_dir;

                if (i==points.Length-2 && prev_dir != Vector3.Zero) add_aabb(start_pos, prev_dir, current_length);
            }
        }

        return aabbs;
    }

    public static Vector3 AbsVec(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.X), Mathf.Abs(v.Y), Mathf.Abs(v.Z));
    }

    public static Vector3 SnapToAxis(Vector3 dir)
    {
        if (dir == Vector3.Zero) return Vector3.Zero;

        dir = dir.Normalized();

        float x = Mathf.Abs(dir.X);
        float y = Mathf.Abs(dir.Y);
        float z = Mathf.Abs(dir.Z);

        if (x > y && x > z)
            return new Vector3(Mathf.Sign(dir.X), 0, 0);
        else if (y > x && y > z)
            return new Vector3(0, Mathf.Sign(dir.Y), 0);
        else
            return new Vector3(0, 0, Mathf.Sign(dir.Z));
    }

    public bool SphereToConvexHull(Vector3 sphere_centre)
    {
        var transform = Transform3D.Identity.Translated(sphere_centre);
        foreach (var body_data in _tracked_csg_meshes)
        {
            var transformed_bullet_collision_box = new System.Numerics.Vector3[_sphere_convex_shape_verts.Length];
            for (int i=0;i<_sphere_convex_shape_verts.Length;i++)
            {
                var box_vert = transform*(_sphere_convex_shape_verts[i]*BulletRadius);
                transformed_bullet_collision_box[i] = new System.Numerics.Vector3(
                    box_vert.X,
                    box_vert.Y,
                    box_vert.Z);

            }
            return OpenGJKSharp.OpenGJKSharp.HasCollision(transformed_bullet_collision_box, ((CsgConvexHullData)body_data).TransformedPoints);
        }
        return false;
    }

    public static bool SphereToSphere(Vector3 sphere1, float radius1, Vector3 sphere2, float radius2)
    {
        return sphere1.DistanceSquaredTo(sphere2) <= (radius1 + radius2)*(radius1 + radius2);
    }

    public static bool SphereToCapsule( 
        Vector3 sphereCenter,
        float sphereRadius,
        Transform3D capsuleTransform,
        float capsuleRadius,
        float capsuleHeight)
    {
        var half_cyl_height = (capsuleHeight - 2f*capsuleRadius)/2f;
        var worldTop = capsuleTransform * new Vector3(0, half_cyl_height, 0);
        var worldBottom = capsuleTransform * new Vector3(0, -half_cyl_height, 0);
        float totalRadius = sphereRadius + capsuleRadius*capsuleTransform.Basis.Scale.X; // assume uniform scaling
        return sphereCenter.DistanceSquaredTo(ClosestPointOnLineSegment(worldBottom, worldTop, sphereCenter)) <= totalRadius * totalRadius;
    }

    public static bool SphereToOBB(Vector3 sphereCenter,float sphereRadius,Transform3D boxTransform, Vector3 boxSize)
    {
        // Transform sphere into the box’s local space
        var inv = boxTransform.AffineInverse();
        var localSphere = inv*sphereCenter;
        var local_radius = inv.Basis.Scale.X * sphereRadius; // assume uniform scaling
        var halfExtents = boxSize * 0.5f;

        // Clamp to box bounds in local space
        float x = Math.Clamp(localSphere.X, -halfExtents.X, halfExtents.X);
        float y = Math.Clamp(localSphere.Y, -halfExtents.Y, halfExtents.Y);
        float z = Math.Clamp(localSphere.Z, -halfExtents.Z, halfExtents.Z);
        return localSphere.DistanceSquaredTo(new Vector3(x, y, z)) <= local_radius * local_radius;
    }

    public static bool SphereToAABB(Vector3 sphereCenter, float sphereRadius, Vector3 boxCenter, Vector3 boxSize)
    {
        var halfExtents = boxSize * 0.5f;
        var min = boxCenter - halfExtents;
        var max = boxCenter + halfExtents;

        // Clamp sphere center to box
        float x = Math.Clamp(sphereCenter.X, min.X, max.X);
        float y = Math.Clamp(sphereCenter.Y, min.Y, max.Y);
        float z = Math.Clamp(sphereCenter.Z, min.Z, max.Z);

        return sphereCenter.DistanceSquaredTo(new Vector3(x, y, z)) <= sphereRadius * sphereRadius;
    }

    public static bool IntersectRayAABB(Vector3 p, Vector3 d, Aabb a, out float tmin, out Vector3 q)
    {
        tmin = 0.0f;
        float tmax = float.MaxValue;
        q = Vector3.Zero;

        for (int i = 0; i < 3; i++)
        {
            float dirComponent = d[i];
            float originComponent = p[i];
            float min = a.Position[i];
            float max = a.Position[i] + a.Size[i];

            if (Mathf.Abs(dirComponent) < Mathf.Epsilon)
            {
                // Ray is parallel to slab
                if (originComponent < min || originComponent > max)
                    return false;
            }
            else
            {
                float ood = 1.0f / dirComponent;
                float t1 = (min - originComponent) * ood;
                float t2 = (max - originComponent) * ood;

                if (t1 > t2)
                {
                    (t2, t1) = (t1, t2);
                }

                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;

                if (tmin > tmax)
                    return false;
            }
        }

        q = p + d * tmin;
        return true;
    }


    private static Vector3 ClosestPointOnLineSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float abLengthSquared = ab.LengthSquared();

        if (abLengthSquared == 0f)
            return a;

        float t = (point - a).Dot(ab) / abLengthSquared;
        t = Math.Clamp(t, 0f, 1f);
        return a + ab * t;
    }




}

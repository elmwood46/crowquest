using Godot;
using System;

public class PhysBodyTrackerData
{
    public enum PhysBodyTrackerShape
    {
        CsgConvexHull,
        Sphere,
        Box,
        Capsule
    }

    public Transform3D GlobalTransform;
    public PhysBodyTrackerShape Type;
    public float[] Args;

    protected PhysBodyTrackerData(Transform3D global_transform, Shape3D shape)
    {
        GlobalTransform = global_transform;
        Type = shape switch
        {
            SphereShape3D => PhysBodyTrackerShape.Sphere,
            BoxShape3D => PhysBodyTrackerShape.Box,
            CapsuleShape3D => PhysBodyTrackerShape.Capsule,
            ConvexPolygonShape3D => PhysBodyTrackerShape.CsgConvexHull,
            _ => throw new ArgumentException($"Unsupported shape type: {shape.GetType()}"),
        };
        Args = Type switch
        {
            PhysBodyTrackerShape.Sphere => [((SphereShape3D)shape).Radius],
            PhysBodyTrackerShape.Box => [((BoxShape3D)shape).Size.X, ((BoxShape3D)shape).Size.Y, ((BoxShape3D)shape).Size.Z],
            PhysBodyTrackerShape.Capsule => [((CapsuleShape3D)shape).Radius, ((CapsuleShape3D)shape).Height],
            PhysBodyTrackerShape.CsgConvexHull => [],
            _ => throw new ArgumentException($"Unsupported shape type: {shape}"),
        };
    }

    public static PhysBodyTrackerData Create(Transform3D global_transform, Shape3D shape)
    {
        return shape switch
        {
            SphereShape3D => new PhysBodyTrackerData(global_transform, shape),
            BoxShape3D => new PhysBodyTrackerData(global_transform, shape),
            CapsuleShape3D => new PhysBodyTrackerData(global_transform, shape),
            ConvexPolygonShape3D convex => new CsgConvexHullData(global_transform, convex),
            _ => throw new ArgumentException($"Unsupported shape type: {shape.GetType()}"),
        };
    }
}

public class CsgConvexHullData : PhysBodyTrackerData
{
    public System.Numerics.Vector3[] TransformedPoints;

    public CsgConvexHullData(Transform3D global_transform, ConvexPolygonShape3D shape)
        : base(global_transform, shape)
    {
        TransformedPoints = new System.Numerics.Vector3[shape.Points.Length];
        for (int i = 0; i < shape.Points.Length; i++)
        {
            var pt = global_transform*shape.Points[i];
            TransformedPoints[i] = new System.Numerics.Vector3(pt.X, pt.Y, pt.Z);
        }
    }
}
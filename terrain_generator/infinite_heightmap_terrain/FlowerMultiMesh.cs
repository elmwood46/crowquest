using Godot;
using System;

public partial class FlowerMultiMesh : MultiMeshInstance3D
{
    public static Vector2 BladeWidth {get;set;} = new(0.9f,2.0f);
    public static Vector2 BladeHeight {get;set;} = new(0.9f,2.0f);
    public static Vector2 SwayYawDegrees {get;set;} = new(0.0f,0.5f); 
    public static Vector2 SwayPitchDegrees {get;set;} = new(0.2f,0.4f);

    public static Vector2 SwayYawRadians => new(Mathf.DegToRad(SwayYawDegrees.X), Mathf.DegToRad(SwayYawDegrees.Y));
    public static Vector2 SwayPitchRadians => new(Mathf.DegToRad(SwayPitchDegrees.X), Mathf.DegToRad(SwayPitchDegrees.Y));
}
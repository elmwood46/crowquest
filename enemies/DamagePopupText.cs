using Godot;
using System;

public partial class DamagePopupText : Node3D
{
    [Export] public double Duration = 1d;

    [Export] public int DamageValue = 0;
    [Export] public Vector3 TargScale = Vector3.One;
    public Vector3 _base_scale;

    public override void _Ready()
    {
        // Set the label text
        var label = GetNode<Label3D>("Anchor/Label3D");
        _base_scale = label.Scale;
        label.Scale = Vector3.Zero; // Start with scale zero for the tween effect
        label.Text = DamageValue.ToString();
        TargScale = _base_scale * TargScale; // Scale the target scale by the base scale

        // Create tween
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Linear);
        tween.SetEase(Tween.EaseType.In);

        // Tween the Y position of the anchor
        var anchor = GetNode<Node3D>("Anchor");
        var yoffset = 0.0f + Random.Shared.NextSingle() * 0.5f;
        anchor.Position = new Vector3(anchor.Position.X - 0.2f + Random.Shared.NextSingle() * 0.4f, yoffset, anchor.Position.Z - 0.2f + Random.Shared.NextSingle() * 0.4f);
        tween.TweenProperty(anchor, "position:y", 5.0f + yoffset, Duration);
        var bounce_tween = CreateTween();
        bounce_tween.SetTrans(Tween.TransitionType.Bounce);
        bounce_tween.SetEase(Tween.EaseType.In);
        bounce_tween.TweenProperty(label, "scale", TargScale, Duration * 0.5f);

        // In parallel, fade out the label
        tween.TweenProperty(label, "scale", Vector3.Zero, 0.5f);
        tween.Parallel().TweenProperty(label, "modulate:a", 0.0f, 0.5f);
        tween.Parallel().TweenProperty(label, "outline_modulate:a", 0.0f, 0.5f);


        // Queue free when finished
        tween.Finished += QueueFree;
    }
}
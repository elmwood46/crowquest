using Godot;
using System;

public partial class Mouse : Node2D
{
    [Export] public Sprite2D MouseSprite;
    [Export] public Sprite2D MouseShadowSprite;
    private static readonly CompressedTexture2D _mouse_hand_point = GD.Load<CompressedTexture2D>("res://cards/cursor-sprites/hand_point.png");
    private static readonly CompressedTexture2D _mouse_hand_drag = GD.Load<CompressedTexture2D>("res://cards/cursor-sprites/hand_closed.png");
    private static readonly CompressedTexture2D _mouse_hand_open = GD.Load<CompressedTexture2D>("res://cards/cursor-sprites/hand_open.png");
    private Vector2 _base_scale;
    public static Mouse Instance { get; private set; } = null;

    public override void _Ready()
    {
        Instance = this;
        Input.MouseMode = Input.MouseModeEnum.ConfinedHidden;
        MouseSprite.Texture = _mouse_hand_point;
        _base_scale = MouseSprite.Scale;
    }

    public override void _PhysicsProcess(double delta)
    {
        var is_left_clicking  = Input.IsActionPressed("LeftClick");
        GlobalPosition = GlobalPosition.Lerp(GetGlobalMousePosition(), 22f * (float)delta);
        MouseSprite.RotationDegrees = Mathf.Lerp(MouseSprite.RotationDegrees, is_left_clicking ? -CardManager.MAX_CARD_ROTATION : 0f, 16f * (float)delta);
        MouseSprite.Scale = MouseSprite.Scale.Lerp(is_left_clicking ? _base_scale * 0.875f : _base_scale, 16f * (float)delta);

        if (CardManager.NodeBeingDragged != null)
        {
            MouseSprite.Texture = _mouse_hand_drag;
        }
        else
        {
            MouseSprite.Texture = is_left_clicking ? _mouse_hand_open : _mouse_hand_point;
        }

        MouseShadowSprite.Texture = MouseSprite.Texture;
        MouseShadowSprite.Position = CardManager.CARD_SHADOW_OFFSET.Rotated(Rotation);
    }
}

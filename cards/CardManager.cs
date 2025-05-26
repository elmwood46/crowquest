using Godot;
using System;

public partial class CardManager
{
    public static Node NodeBeingDragged { get; set; } = null;
    public const float MAX_CARD_ROTATION = 12.5f;
    public static readonly Vector2 CARD_SHADOW_OFFSET = new(-4f, 4f);
    public static bool CanDragCard(Card c)
    {
        return c.IsSelectable && (NodeBeingDragged == null || NodeBeingDragged == c);
    }
}

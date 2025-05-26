using Godot;
using System;

public partial class Card : Control
{
    [Export] public Sprite2D CardSprite;
    [Export] public Label CardLabel;
    [Export] public AudioStream CardSelectedSound;
    [Export] public AudioStream CardHoverSound;

    public bool IsSelectable = true;
    private bool _mouse_over = false;
    private bool _is_dragged = false;
    private Sprite2D _card_shadow_sprite;
    private Vector2 _target_scale;
    private Vector2 _base_card_sprite_scale;
    private Tween _scale_tween;
    private Vector2 _previous_position;
    private CardTypeEnum _card_type = CardTypeEnum.Damage;
    public string CardTitle { get; private set; }
    public string CardDescription { get; set; } = "No description provided.";

    public enum CardTypeEnum
    {
        MoveSpeed,
        Health,
        Damage,
        LanternRange,
        LanternEnergy
    }

    public override void _Ready()
    {
        MouseEntered += () => { _mouse_over = true; AudioManager.TryPlayAtPcPos(CardHoverSound); };
        MouseExited += () => { _mouse_over = false; };
        _card_shadow_sprite = GetNode<Sprite2D>("%CardShadow");
        _base_card_sprite_scale = CardSprite.Scale;
        _target_scale = _base_card_sprite_scale;

        CustomMinimumSize = CardSprite.Texture.GetSize() * _base_card_sprite_scale;
        Size = CustomMinimumSize;
        CardSprite.Position = CustomMinimumSize / 2f;
        CardLabel.Text = _card_type.ToString();
        CardTitle = CardLabel.Text;
    }

    public override void _PhysicsProcess(double delta)
    {
        DragLogic((float)delta);
    }

    private void DragLogic(float delta)
    {
        // set shadow offset
        _card_shadow_sprite.Position = CardManager.CARD_SHADOW_OFFSET.Rotated(Rotation);
        if ((_mouse_over || _is_dragged) && CardManager.CanDragCard(this))
        {
            CardSprite.ZIndex = 100;
            CardLabel.ZIndex = 101;

            if (Input.IsActionPressed("LeftClick") && IsSelectable)
            {
                TweenCardSpriteScale(_base_card_sprite_scale * 1.3f);
                GlobalPosition = GlobalPosition.Lerp(GetGlobalMousePosition() - Size / 2f, 22f * delta);
                _is_dragged = true;
                if (CardManager.NodeBeingDragged != this) AudioManager.TryPlayAtPcPos(CardSelectedSound);
                CardManager.NodeBeingDragged = this;
                CardRotationOnDrag(delta);
                
            }
            else
            {
                TweenCardSpriteScale(_base_card_sprite_scale * 1.1f);
                ResetCardRotation(delta);
                _is_dragged = false;
                if (CardManager.NodeBeingDragged == this)
                {
                    CardManager.NodeBeingDragged = null;
                }
            }
            return;
        }
        else
        {
            if (CardManager.NodeBeingDragged == this)
            {
                CardManager.NodeBeingDragged = null;
            }
        }

        CardSprite.ZIndex = 0;
        CardLabel.ZIndex = 0;

        ResetCardRotation(delta);
        TweenCardSpriteScale(_base_card_sprite_scale);
    }

    public void SetType(CardTypeEnum type)
    {
        _card_type = type;
        CardLabel.Text = _card_type.ToString();
        CardTitle = GetCardTitle(_card_type);
        CardDescription = GetCardDescription(_card_type);
    }

    private void TweenCardSpriteScale(Vector2 new_scale)
    {
        if (new_scale.IsEqualApprox(_target_scale)) { _target_scale = new_scale; return; }

        _scale_tween?.Kill();
        _scale_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Bounce);
        _scale_tween.TweenProperty(CardSprite, "scale", new_scale, 0.125f);

        _target_scale = new_scale;
    }

    private void CardRotationOnDrag(float delta)
    {
        var desired_rotation = Mathf.Clamp((GlobalPosition - _previous_position).X * 0.85f, -CardManager.MAX_CARD_ROTATION, CardManager.MAX_CARD_ROTATION);
        CardSprite.RotationDegrees = Mathf.Lerp(CardSprite.RotationDegrees, desired_rotation, 12.0f * delta);
        _previous_position = GlobalPosition;
    }

    private void ResetCardRotation(float delta)
    {
        CardSprite.RotationDegrees = Mathf.Lerp(CardSprite.RotationDegrees, 0, 22.0f * delta);
    }

    public static string GetCardDescription(CardTypeEnum type)
    {
        return type switch
        {
            CardTypeEnum.MoveSpeed => "Increases your movement speed by 2%.",
            CardTypeEnum.Health => "Increases your maximum health by 20.",
            CardTypeEnum.Damage => "Increases damage output by 10%",
            CardTypeEnum.LanternRange => "Increases the range of your lantern by 0.1 metres.",
            CardTypeEnum.LanternEnergy => "Increases the brightness of your lantern by 10%.",
            _ => "No description provided."
        };
    }

    public static string GetCardTitle(CardTypeEnum type)
    {
        return type switch
        {
            CardTypeEnum.MoveSpeed => "Move Speed",
            CardTypeEnum.Health => "Health",
            CardTypeEnum.Damage => "Damage",
            CardTypeEnum.LanternRange => "Lantern Range",
            CardTypeEnum.LanternEnergy => "Lantern Energy",
            _ => "Unknown Card"
        };
    }

    public void ApplyCardEffect()
    {
        if (_card_type == CardTypeEnum.MoveSpeed)
        {
            // Implement move speed effect
            GD.Print("Move Speed effect applied.");
            Player.Instance.MoveSpeed *= 1.02f; // Example effect, assuming Player.Instance has a MoveSpeed property
        }
        else if (_card_type == CardTypeEnum.Health)
        {
            // Implement health effect
            GD.Print("Health effect applied.");
            Player.Instance.MaxHealth += 20; // Example effect, assuming Player.Instance has a MaxHealth property
        }
        else if (_card_type == CardTypeEnum.Damage)
        {
            // Implement damage effect
            GD.Print("Damage effect applied.");
        }
        else if (_card_type == CardTypeEnum.LanternRange)
        {
            // Implement lantern range effect
            GD.Print("Lantern Range effect applied.");
            Player.Instance.PlayerLight.OmniRange += 0.1f;
        }
        else if (_card_type == CardTypeEnum.LanternEnergy)
        {
            // Implement lantern range effect
            GD.Print("Lantern Energy effect applied.");
            Player.Instance.PlayerLightMaxEnergy *= 1.1f;
        }
        else
        {
            GD.Print("Unknown card _card_type, no effect applied.");
        }
    }
}
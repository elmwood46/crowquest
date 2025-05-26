using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class LevelUpCardSelection : Control
{
    [Signal] public delegate void CardSelectedEventHandler(Card selectedCard);
    [Signal] public delegate void FinishedEventHandler();

    [Export] public Card[] Cards { get; set; }
    [Export] public ColorRect SelectionRectVisible;
    [Export] public PanelContainer CardInfoPanel;
    [Export] public AudioStream LevelUpSelectSound;

    private float _selection_rect_opacity = 0.0f; // Opacity for the selection rectangle
    private Tween _selection_rect_tween;
    private Tween _card_info_panel_tween;
    private const float TWEEN_DURATION = 0.1f; // Duration for the fade-in effect
    private const float SELECTION_RECT_OPACITY_TARGET = 0.5f; // Target opacity for the selection rectangle
    private float _selection_rect_targ_position;
    private float _selection_rect_base_position;
    private bool _disabled_card_selection = true;
    private bool _is_hovering_selection = false;
    private bool _showing_info_text = false;

    public override void _Ready()
    {
        CardInfoPanel.Modulate = new Color(CardInfoPanel.Modulate, 0.0f); // Hide the card info panel initially

        // generate random card upgrades
        var rand_cards_list = new List<Card.CardTypeEnum>(Enum.GetValues(typeof(Card.CardTypeEnum)).Cast<Card.CardTypeEnum>());

        var arr_len = Cards.Length;
        var size = GetViewport().GetVisibleRect().Size;
        var padding = 2.0f * Cards[0].Size * Cards[0].Scale; // Padding for the cards
        var padded_size = size - padding; // Padding for the cards
        // Initialize the level-up card selection UI
        for (int i = 0; i < arr_len; i++)
        {
            var card = Cards[i];
            card.IsSelectable = false;
            card.GlobalPosition = GlobalPosition
                + new Vector2(
                    padding.X - card.Size.X * card.Scale.X / 2 + i * padded_size.X / arr_len,
                    GetViewport().GetVisibleRect().Size.Y + card.Size.Y * card.Scale.Y * 0.5f); // Position cards vertically offscreen

            // assign card a random type, no duplicates
            var idx = Random.Shared.Next(rand_cards_list.Count);
            card.SetType(rand_cards_list[idx]);
            rand_cards_list.RemoveAt(idx);
        }

        SelectionRectVisible.Modulate = new Color(SelectionRectVisible.Modulate, _selection_rect_opacity);
        _selection_rect_base_position = GlobalPosition.Y + GetViewport().GetVisibleRect().Size.Y;
        _selection_rect_targ_position = _selection_rect_base_position - SelectionRectVisible.Size.Y;
        SelectionRectVisible.GlobalPosition = new Vector2(GlobalPosition.X, _selection_rect_base_position); // Position selection rect offscreen

        var targ_y = GlobalPosition.Y + GetViewport().GetVisibleRect().Size.Y / 3f - Cards[0].Size.Y * Cards[0].Scale.Y / 2f; // Target Y position for the cards
        var intro_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);

        var pc = GetNode<PanelContainer>("PanelContainer");
        pc.Position = new Vector2(pc.Position.X, pc.Size.Y*2f); // Position panel offscreen
        intro_tween.TweenProperty(pc, "position:y", Mathf.Epsilon, 0.5f);
        intro_tween.TweenProperty(pc, "position:y", 0.0f, 0.001f);
        foreach (var card in Cards)
        {
            intro_tween.Parallel().TweenProperty(card, "global_position:y", targ_y, 0.5f);
        }

        intro_tween.Finished += () =>
        {
            _disabled_card_selection = false;
            // After the intro animation, enable selection
            foreach (var card in Cards)
            {
                card.GlobalPosition = new Vector2(card.GlobalPosition.X, targ_y);
                card.IsSelectable = true;
            }
            intro_tween.Kill();
        };
    }

    public override void _Process(double delta)
    {
        if (!_disabled_card_selection) SelectCardLogic();
    }

    private void SelectCardLogic()
    {
        // info panel
        if (CardManager.NodeBeingDragged is Card c && !_showing_info_text)
        {
            _showing_info_text = true;
            CardInfoPanel.GetNode<Label>("MarginContainer/Label").Text = "\"" + c.CardTitle + "\"" + ": " + c.CardDescription;
            _card_info_panel_tween?.Kill();
            _card_info_panel_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Linear);
            _card_info_panel_tween.TweenProperty(CardInfoPanel, "modulate:a", 1.0f, TWEEN_DURATION);
        }
        else if (CardManager.NodeBeingDragged == null)
        {
            if (_showing_info_text)
            {
                _card_info_panel_tween?.Kill();
                _card_info_panel_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Linear);
                _card_info_panel_tween.TweenProperty(CardInfoPanel, "modulate:a", 0f, TWEEN_DURATION);
            }
            _showing_info_text = false;
        }

        // bottom panel
        if (CardManager.NodeBeingDragged is Card
                    && (Mouse.Instance.GlobalPosition.Y + Cards[0].Size.Y * Cards[0].Scale.Y * 0.5f) < _selection_rect_targ_position)
        {
            // Card is selected, perform the level-up logic
            if (!_is_hovering_selection)
            {
                _selection_rect_tween?.Kill();
                _selection_rect_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Linear).Parallel();
                _selection_rect_tween.TweenProperty(SelectionRectVisible, "global_position:y", _selection_rect_targ_position, TWEEN_DURATION);
                _selection_rect_tween.Parallel().TweenProperty(SelectionRectVisible, "modulate:a", SELECTION_RECT_OPACITY_TARGET, TWEEN_DURATION);

            }
            _is_hovering_selection = true;
        }
        else
        {
            if (_is_hovering_selection)
            {
                _selection_rect_tween?.Kill();
                _selection_rect_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Linear).Parallel();
                _selection_rect_tween.TweenProperty(SelectionRectVisible, "global_position:y", _selection_rect_base_position, TWEEN_DURATION);
                _selection_rect_tween.Parallel().TweenProperty(SelectionRectVisible, "modulate:a", 0f, TWEEN_DURATION);
            }
            _is_hovering_selection = false;
        }

        Card selected_card = null;
        foreach (var card in Cards)
        {
            if (card.IsSelectable && card.GlobalPosition.Y + card.Size.Y * card.Scale.Y >= _selection_rect_targ_position)
            {
                // Card is within the selection rectangle
                _disabled_card_selection = true;
                EmitSignal(SignalName.CardSelected, card);
                AudioManager.TryPlayAtPcPos(LevelUpSelectSound);
                selected_card = card;

                var outro_tween = CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
                outro_tween.TweenProperty(this, "modulate:a", 0f, 1.0f);

                outro_tween.Finished += () =>
                {
                    EmitSignal(SignalName.Finished);
                    outro_tween.Kill();
                };

                break;
            }
        }

        if (_disabled_card_selection)
        {
            var tweener = CreateTween().SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
            tweener.Finished += tweener.Kill;

            foreach (var card in Cards)
            {
                card.IsSelectable = false;
                var sprite2d = new Sprite2D()
                {
                    Texture = card.CardSprite.Texture,
                    Scale = card.CardSprite.Scale,
                    GlobalPosition = card.CardSprite.GlobalPosition,
                    ZIndex = card.CardSprite.ZIndex+1 // Ensure the sprite is above the card
                };

                if (card == selected_card) sprite2d.Modulate = new Color(.9f, .9f, 0.0f, 1.0f); // Highlight selected card in yellow
                else sprite2d.Modulate = new Color(1.0f, 0.0f, 0.0f, 1.0f); // Highlight unselected cards in red
                AddChild(sprite2d);

                tweener.Parallel().TweenProperty(sprite2d, "modulate:a", 0f, 0.6f);
                tweener.Parallel().TweenProperty(card.CardSprite, "modulate:a", 0f, 0.9f);
                tweener.Parallel().TweenProperty(card.CardSprite, "scale", Vector2.Zero, 0.9f);
                tweener.Parallel().TweenProperty(sprite2d, "scale", sprite2d.Scale * 4f, 0.9f);
            }
        }
    }
}

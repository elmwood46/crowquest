using Godot;
using System;

[GlobalClass]
[Tool]
public partial class Door : Node3D
{
    [Signal] public delegate void GenerateOnOpenEventHandler();
    [Signal] public delegate void DoorOpenedEventHandler();
    [Signal] public delegate void DoorClosedEventHandler();

    [Export] public AnimationPlayer AnimationPlayer { get; set; }
    [Export] public InteractableComponent Interactable { get; set; }

    private bool _is_locked = false;
    private bool _opened_first_time = false;

    private Tween _door_lock_tween;

    public override void _Ready()
    {
        AnimationPlayer.AnimationFinished += (anim_name) =>
        {
            if (anim_name == "DoorOpen" || anim_name == "DoorOpenInwards")
            {
                if (!_opened_first_time)
                {
                    EmitSignal(SignalName.GenerateOnOpen);
                    _opened_first_time = true;
                }
            }
        };

        Interactable.Interacted += () =>
        {
            if (!_is_locked)
            {
                OpenDoor();
                Interactable.QueueFree();
            }
        };
    }

    public void ForceDoorAlreadyOpen()
    {
        Interactable.QueueFree();
        _opened_first_time = true; // skip the "first time opened" signal
        OpenDoor();
    }

    private void OpenDoor()
    {
        if (!_opened_first_time)
        {
            EmitSignal(SignalName.GenerateOnOpen);
            _opened_first_time = true;
        }
        var dir_to = Player.Instance.GlobalPosition - GlobalPosition;
        var transform = GlobalBasis * Vector3.Forward;

        AnimationPlayer.Play(dir_to.Dot(transform) < 0 ? "DoorOpen" : "DoorOpenInwards");
    }

    private void CloseDoor(bool lock_door_on_close = false)
    {
        AnimationPlayer.Play("DoorClose", customSpeed: 3f);
        var tween = CreateTween();
        tween.TweenProperty(GetNode<MeshInstance3D>("Door"), "rotation_degrees", new Vector3(0, 0, 0), 0.43333333)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Elastic);
        tween.Finished += () =>
        {
            if (lock_door_on_close)
            {
                _is_locked = true;
                if (Interactable != null && IsInstanceValid(Interactable)) Interactable.HoverText = "Door is locked";
                TweenDoorColor();
            }

            EmitSignal(SignalName.DoorClosed);
            tween.Kill();
        };
    }

    private void TweenDoorColor()
    {
        _door_lock_tween?.Kill();
        var door_mesh = GetNode<MeshInstance3D>("Door");
        _door_lock_tween = CreateTween();
        var color = _is_locked ? new Color(1, 0, 0) : new Color(1, 1, 1);
        _door_lock_tween.TweenProperty(door_mesh, "mesh/surface_0/material/albedo_color", color, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
    }

    public void LockDoor()
    {
        CloseDoor(true);
    }

    public void UnlockDoor()
    {
        OpenDoor();
        _is_locked = false;
        Interactable.HoverText = InteractableComponent.InteractButtonName;
        TweenDoorColor();
    }
}

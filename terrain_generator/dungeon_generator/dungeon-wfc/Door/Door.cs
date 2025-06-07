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
    [Export] public float DoorOpenSpeedScale = 2.0f;

    private bool _is_locked = false;
    private bool _opened_first_time = false;

    private Tween _door_lock_tween;

    private static StandardMaterial3D _baseDoorMaterial = GD.Load<StandardMaterial3D>("res://terrain_generator/dungeon_generator/dungeon-wfc/Door/door_base_material.tres");
    private static StandardMaterial3D _basePadlockSurf0Material = GD.Load<StandardMaterial3D>("res://terrain_generator/dungeon_generator/dungeon-wfc/Door/door_padlock_surf_0_mat.tres");
    private static StandardMaterial3D _basePadlockSurf1Material = GD.Load<StandardMaterial3D>("res://terrain_generator/dungeon_generator/dungeon-wfc/Door/door_padlock_surf_1_mat.tres");
    private static StandardMaterial3D _door_override_material = _baseDoorMaterial.Duplicate() as StandardMaterial3D;
    private static StandardMaterial3D _padlock_override_surf0_material = _basePadlockSurf0Material.Duplicate() as StandardMaterial3D;
    private static StandardMaterial3D _padlock_override_surf1_material = _basePadlockSurf1Material.Duplicate() as StandardMaterial3D;

    public override void _Ready()
    {
        ResetMaterialsToBase();

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

        Interactable.HoverText = "Open";
    }

    public void ForceDoorAlreadyOpen()
    {
        Interactable.QueueFree();
        _opened_first_time = true; // skip the "first time opened" signal
        OpenDoor();
    }

    private void OpenDoor()
    {
        if (_is_locked) return;

        if (!_opened_first_time)
        {
            EmitSignal(SignalName.GenerateOnOpen);
            _opened_first_time = true;
        }
        var dir_to = Player.Instance.GlobalPosition - GlobalPosition;
        var transform = GlobalBasis * Vector3.Forward;

        AnimationPlayer.Play(dir_to.Dot(transform) < 0 ? "DoorOpen" : "DoorOpenInwards", customSpeed: DoorOpenSpeedScale);
    }

    private void CloseDoor(bool lock_door_on_close = false)
    {
        var door_static = GetNode<StaticBody3D>("Door/StaticBody");
        door_static.CollisionMask = 0u;
        door_static.CollisionLayer = 0u;

        AnimationPlayer.Play("DoorClose", customSpeed: 3f);
        var tween = CreateTween();
        tween.TweenProperty(GetNode<MeshInstance3D>("Door"), "rotation_degrees", new Vector3(0, 0, 0), 0.43333333)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Elastic);
        tween.Finished += () =>
        {
            door_static.SetCollisionLayerValue(1, true);
            door_static.SetCollisionMaskValue(1, true);
            if (lock_door_on_close)
            {
                _is_locked = true;
                SetMaterialsToDuplicates();
                if (IsInstanceValid(Interactable)) Interactable.HoverText = "Door is locked.";

                TweenDoorColor();
            }

            EmitSignal(SignalName.DoorClosed);
            tween.Kill();
        };
    }

    private void TweenDoorColor()
    {
        _door_lock_tween?.Kill();
        var door_mesh_material = GetNode<MeshInstance3D>("Door").MaterialOverride;
        _door_lock_tween = CreateTween();
        var color = _is_locked ? new Color(1, 0, 0) : new Color(1, 1, 1);
        var lock_alpha = _is_locked ? 1f : 0f;

        _door_lock_tween.Parallel().TweenProperty(door_mesh_material, "albedo_color", color, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);

        var lock_mesh = GetNode<MeshInstance3D>("Padlock");
        var mesh_mat_0 = lock_mesh.GetSurfaceOverrideMaterial(0);
        var mesh_mat_1 = lock_mesh.GetSurfaceOverrideMaterial(1);

        _door_lock_tween.Parallel().TweenProperty(mesh_mat_0, "albedo_color:a", lock_alpha, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);
        _door_lock_tween.TweenProperty(mesh_mat_1, "albedo_color:a", lock_alpha, 0.5)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Expo);

        _door_lock_tween.Finished += () =>
        {
            if (!_is_locked) ResetMaterialsToBase();
        };
    }

    public void LockDoor()
    {
        CloseDoor(true);
    }

    private void SetMaterialsToDuplicates()
    {
        GetNode<MeshInstance3D>("Door").MaterialOverride = _door_override_material;
        GetNode<MeshInstance3D>("Padlock").SetSurfaceOverrideMaterial(0, _padlock_override_surf0_material);
        GetNode<MeshInstance3D>("Padlock").SetSurfaceOverrideMaterial(1, _padlock_override_surf1_material);
    }

    private void ResetMaterialsToBase()
    {
        GetNode<MeshInstance3D>("Door").MaterialOverride = null;
        GetNode<MeshInstance3D>("Padlock").SetSurfaceOverrideMaterial(0, null);
        GetNode<MeshInstance3D>("Padlock").SetSurfaceOverrideMaterial(1, null);
    }

    public void UnlockDoorAndOpen()
    {
        _is_locked = false;
        if (_opened_first_time) OpenDoor();
        Interactable.HoverText = "Open";
        TweenDoorColor();
    }
}

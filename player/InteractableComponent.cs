using Godot;
using System;
using System.Collections.Generic;

public partial class InteractableComponent : Node
{
	[Signal] public delegate void InteractedEventHandler();
	private Dictionary<CharacterBody3D, ulong> _charactersHovering = [];
	[Export] public string HoverText = "";

	public static readonly string InteractButtonName = "(E)";

	public void Interact()
	{
		EmitSignal(SignalName.Interacted);
	}

	public void HoverCursor(CharacterBody3D c) {
		_charactersHovering[c] = Engine.GetProcessFrames();
	}

	// public CharacterBody3D GetCharacterHoveredByCurCamera() {
	// 	foreach (CharacterBody3D c in _charactersHovering.Keys) {
	// 		var cur_cam = GetViewport()?.GetCamera3D();
	// 		if (c.FindChildren("*","Camera3D").Contains(cur_cam)) {
	// 			return c;
	// 		}
	// 	}
	// 	return null;
	// }

	public override void _Process(double delta) {
		foreach (CharacterBody3D c in _charactersHovering.Keys) {
			if (Engine.GetProcessFrames() - _charactersHovering[c] > 1) {
				_charactersHovering.Remove(c);
			}
		}
	}
}
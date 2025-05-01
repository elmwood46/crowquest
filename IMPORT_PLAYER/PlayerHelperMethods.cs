using Godot;
using System;

#nullable enable

public partial class PlayerIMPORT : CharacterBody3D
{
    public void HandleCrouch(float delta) {
        var wasCrouched = _is_crouched;
        if (Input.IsActionPressed("Crouch")) {
            _is_crouched = true;
        }
        else if (_is_crouched && !TestMove(GlobalTransform, new Vector3(0, CROUCH_TRANSLATE, 0))) {
            _is_crouched = false;
        }

        var tryCrouchJump = 0f;
        if (wasCrouched != _is_crouched && !IsOnFloor() && !_snappedToStairsLastFrame) {
            tryCrouchJump = _is_crouched ? CROUCH_JUMP_BOOST : -CROUCH_JUMP_BOOST;
        }
        if (tryCrouchJump != 0) {
            var result = new KinematicCollision3D();
            TestMove(GlobalTransform, new Vector3(0, tryCrouchJump, 0), result);
            Position += new Vector3(0,result.GetTravel().Y,0);
        }
    
        HeadCrouched.Position = _is_crouched ? new Vector3(0, -CROUCH_TRANSLATE, 0) : Vector3.Zero;
        ((CapsuleShape3D)CollisionShape.Shape).Height = _is_crouched ? _standing_height-CROUCH_TRANSLATE : _standing_height;
        CollisionShape.Position = new Vector3(0, ((CapsuleShape3D)CollisionShape.Shape).Height/2, 0);
    }

    public void AddRecoil(float pitch, float yaw) {
        TargetRecoil.X += pitch;
        TargetRecoil.Y += yaw;
    }
    public Vector2 GetCurrentRecoil() {
        return CurrentRecoil;
    }
    public void UpdateRecoil(float delta) {
        TargetRecoil = TargetRecoil.Lerp(Vector2.Zero, RECOIL_RECOVER_SPEED * delta);
        var prevRecoil=CurrentRecoil;
        CurrentRecoil = CurrentRecoil.Lerp(TargetRecoil, RECOIL_APPLY_SPEED * delta);
        var recoilDifference = CurrentRecoil - prevRecoil;

        Head.RotateY(recoilDifference.Y);
        Camera.RotateX(recoilDifference.X);
        Camera.Rotation = new Vector3(Mathf.Clamp(Camera.Rotation.X, -Mathf.Pi/2, Mathf.Pi/2),Camera.Rotation.Y,Camera.Rotation.Z);
        _cameraXRotation = Camera.Rotation.X;
    }

    public void UpdateAnimations() {
        if (!IsOnFloor() && !_snappedToStairsLastFrame) {
            _stateMachinePlayback.Travel("MidJump");
            return;
        }

         // get vector of players movement relative to player model
        var relVel = Head.GlobalBasis.Inverse() * (Velocity * new Vector3(1,0,1)) / GetMoveSpeed();
        var relVelXZ = new Vector2(relVel.X, -relVel.Z);

        if (Input.IsActionPressed("Sprint")) {
            _stateMachinePlayback.Travel("RunBlendSpace2D");
            _animationTree.Set("parameters/RunBlendSpace2D/blend_position", relVelXZ);
        }
        else {
            _stateMachinePlayback.Travel("WalkBlendSpace2D");
            _animationTree.Set("parameters/WalkBlendSpace2D/blend_position", relVelXZ);
        }
    }

    public float GetMoveSpeed() {
        if (Input.IsActionPressed("Sprint")) return SprintSpeed;
        return WalkSpeed;
    }

    #region holding rigid body
    private float _hold_counter = 0.0f;
    private float _pickup_time = 0.5f;
    public void HoldRigidBody() {
        if (_held_object != null) return;
		// confirms the first collider is the player character body; if not, something is wrong 
		if (ShapeCast.GetCollisionCount() > 0 && ShapeCast.GetCollider(0) != this)
			return;

        if (ShapeCast.GetCollisionCount() == 0)
        {
            _hold_counter = 0.0f;
            return;
        }

		for (int i = 0; i < ShapeCast.GetCollisionCount(); i++) {
            if (ShapeCast.GetCollider(i) is RigidBody3D r)
            {
                // if (!IsInstanceValid(r) || r.Freeze || r.Mass > MAX_PICKUP_MASS || r.IsQueuedForDeletion()) continue;
                // if (r.GetParent().GetParent() is DestructibleMesh d && d.IsBroken()) continue;
                // if (_hold_counter < _pickup_time) _hold_counter ++;
                // else 
                // {
                //     _hold_counter = 0.0f;
                //     _held_object = r;
                //     GD.Print("picked up ", _held_object);
                //     r.LinearVelocity = Vector3.Zero;
                //     r.AngularVelocity = Vector3.Zero;
                //     //r.Freeze = true;
                //     //r.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
                // }
            }
		}
		return;
    }

    public static bool ObjectIsHeldRigidBody(RigidBody3D obj)
    {
        return Instance._held_object == obj;
    }

    public static void SetHeldObjectToNull()
    {
        Instance._held_object = null;
    }

    public void ReleaseHeldRigidBody()
    {
        if (_held_object == null) return;
        if (_held_object.IsQueuedForDeletion()) return;
        _held_object.Freeze = false;
        _held_object = null;
    }

        // TODO holding rigid body
    public void UpdateHeldRigidBody() {
        if (_held_object == null) return;
        if (_held_object.IsQueuedForDeletion()) return;
        if (_held_object.GlobalPosition.DistanceSquaredTo(Head.GlobalPosition) > 13.0f) {
            _held_object = null;
            return;
        }
        var targ_position = Camera.GlobalTransform * new Vector3(0, 0, -2.0f);
        var dist = targ_position.DistanceTo(_held_object.GlobalPosition);
        var vel_mag = 20.0f*Mathf.Max(1.0f, 5.0f*dist)/Mathf.Max(1.0f,_held_object.Mass);
        //_held_object.ApplyForce(5.0f*dist*(targ_position - _held_object.GlobalPosition));
        _held_object.LinearVelocity = vel_mag*(targ_position - _held_object.GlobalPosition);
        
        var dir = targ_position.DirectionTo(Head.GlobalPosition);
        var targ_rotation = new Basis(Quaternion.FromEuler(Vector3.Back.SignedAngleTo(dir, Vector3.Up) * Vector3.Up));
        var curr_rotation = _held_object.Transform.Basis;
        _held_object.AngularVelocity = 2.0f*CalcAngularVelocity(curr_rotation, targ_rotation);

        //_held_object.Basis = new Basis(targ_rotation);
        //_held_object.GlobalPosition = _held_object.GlobalPosition.Lerp(targ_position, 0.2f);
    }

    public static Vector3 CalcAngularVelocity(Basis fromBasis, Basis toBasis)
    {
        Quaternion q1 = fromBasis.GetRotationQuaternion();
        Quaternion q2 = toBasis.GetRotationQuaternion();
        
        // Quaternion that transforms q1 into q2
        Quaternion qt = q2 * q1.Inverse();
        
        // Angle from quaternion
        float angle = 2.0f * Mathf.Acos(qt.W);

        // Ensure we use the representation with the smallest angle
        if (angle > Mathf.Pi)
        {
            qt = new Quaternion(-qt.X, -qt.Y, -qt.Z, -qt.W);
            angle = Mathf.Tau - angle;
        }

        // Prevent divide by zero
        if (angle < 0.0001f)
            return Vector3.Zero;

        // Axis from quaternion
        Vector3 axis = new Vector3(qt.X, qt.Y, qt.Z) / Mathf.Sqrt(1.0f - qt.W * qt.W);
        
        return axis * angle;
    }
    #endregion

	public InteractableComponent? GetInteractableComponentAtShapecast() {
		// confirms the first collider is the player character body; if not, something is wrong 
		if (ShapeCast.GetCollisionCount() > 0 && ShapeCast.GetCollider(0) != this)
			return null;

		for (int i = 0; i < ShapeCast.GetCollisionCount(); i++) {
            if (ShapeCast.GetCollider(i) is not Node collider) continue;
            foreach (var child in collider.GetChildren(true)) {
                if (child is InteractableComponent interactable) {
                    return interactable;
                }
            }
		}
		return null;
	}

	public void UpdateViewAndWorldModelMasks() {
		SetCullLayerRecursive(GetNode<Node3D>("%HandWorldModel"), WORLD_MODEL_LAYER, false);
        SetCullLayerRecursive(GetNode<Node3D>("%WorldModel"), WORLD_MODEL_LAYER, false);
		SetCullLayerRecursive(GetNode<Node3D>("%HandViewModel"), VIEW_MODEL_LAYER, true);
		Camera.SetCullMaskValue(WORLD_MODEL_LAYER, false); // hide the world model layer
		//Camera.SetCullMaskValue(VIEW_MODEL_LAYER, false); // hide the view model layer -- e.g. for mirrors and other cameras
	}

    // Recursive method to process all child nodes
    private static void SetCullLayerRecursive(Node node, int cull_layer, bool disableShadows)
    {
        // Iterate over the current node's children
        foreach (Node child in node.GetChildren())
        {
            // Example: If it's a VisualInstance3D, modify properties or do other logic
            if (child is VisualInstance3D visual)
            {
                // Set Layer Mask (example action)
                visual.SetLayerMaskValue(1, false);  // Disable layer 1 (example)
                visual.SetLayerMaskValue(cull_layer, true);   // Enable layer 2 (example)
            }

			if (disableShadows && child is GeometryInstance3D g) {
				g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			}

            // If the child has its own children, recursively process them
            SetCullLayerRecursive(child, cull_layer, disableShadows);
        }
    }

    public void AlignWorldModelToLookDir() {
        var worldModel = GetNode<Node3D>("%WorldModel");
        worldModel.Rotation = new Vector3(0, Head.Rotation.Y, 0);
    }

    public void CheckForCoins()
    {
        var overlap = CoinPickupArea.GetOverlappingBodies();
        foreach (var obj in overlap)
        {
            if (obj is Coin coin && !coin.ActivatePickup)
            {
                coin.ActivatePickup = true;
            }
        }
    }

    public static void AddMoney(int amount)
    {
        Instance._money += amount;
    }

	public void PushAwayRigidBodies() {
		for (int i=0; i<GetSlideCollisionCount();i++) {
			var c = GetSlideCollision(i);
            var collider = c.GetCollider();
            //if ( collider is Chunk) GD.Print($"collided with chunk {collider}");
            if (collider is not Coin && collider is RigidBody3D r) {
				var push_dir = -c.GetNormal();
				var veldiff = Velocity.Dot(push_dir) - r.LinearVelocity.Dot(push_dir);
				veldiff = Mathf.Max(veldiff, 0f);
				var massratio = Mathf.Min(1f,Mass/r.Mass);
				push_dir.Y = 0f;
				var push_force = massratio * PushForce;
				r.ApplyImpulse(push_dir * push_force * veldiff, c.GetPosition() - r.GlobalPosition);
			}
		}
	}

	private void SaveCameraPosForSmoothing() {
		if (_savedCameraGlobalPos==_cameraPosReset) {
			_savedCameraGlobalPos = CameraSmooth.GlobalPosition;
		}
	}

	private void ResetCameraSmooth(float delta) {
		if (_savedCameraGlobalPos==_cameraPosReset) return;
		CameraSmooth.GlobalPosition = new Vector3 (CameraSmooth.GlobalPosition.X,_savedCameraGlobalPos.Y, CameraSmooth.GlobalPosition.Z);
		CameraSmooth.Position = new Vector3(CameraSmooth.Position.X,Mathf.Clamp(CameraSmooth.Position.Y, -0.7f, 0.7f),CameraSmooth.Position.Z);
		var move_amount = Mathf.Max(Velocity.Length() * delta, WalkSpeed/2 * delta);
		CameraSmooth.Position = new Vector3(CameraSmooth.Position.X,Mathf.Lerp(CameraSmooth.Position.Y, 0f, move_amount),CameraSmooth.Position.Z);
		if (CameraSmooth.Position.Y == 0f) {
			_savedCameraGlobalPos = _cameraPosReset;
		}
	}

    /// <summary>
    /// Add camera shake amount between 0 and 1, adds as a proportion of max camera shake.
    /// </summary>
    /// <param name="amount">The percentage (between 0 and 1) of max camera shake to add. A value of 0.2 adds 20% camera shake.</param>
    public void AddCameraShake(float amount) 
    {
        amount = Mathf.Clamp(amount, 0, 1);
        _camera_shake_amount = Mathf.Clamp((float)_camera_shake_amount+amount*MaxCameraShake, 0, MaxCameraShake);
    }

    public void DoCameraShake()
    {
        if (_camera_shake_amount <= 0) return;
        var shake = new Vector3((float)GD.RandRange(-_camera_shake_amount, _camera_shake_amount), (float)GD.RandRange(-_camera_shake_amount, _camera_shake_amount), (float)GD.RandRange(-_camera_shake_amount, _camera_shake_amount));
        CameraShake.Position = shake;
        _camera_shake_amount = Mathf.Max(_camera_shake_amount - CameraShakeDecay, 0);
        if (_camera_shake_amount == 0) CameraShake.Position = Vector3.Zero;
    }

	private bool SnapUpStairsCheck(float delta)
	{
		if (!(IsOnFloor() || _snappedToStairsLastFrame)) return false;
		if (Velocity.Y > 0 || (Velocity * new Vector3(1,0,1)).Length() == 0) return false;

		var expectedMoveMotion = Velocity * new Vector3(1, 0, 1) * delta;
		var stepPosWithClearance = GlobalTransform.Translated(expectedMoveMotion + new Vector3(0, MAX_STEP_HEIGHT * 1.2f, 0));

		var downCheckResult = new KinematicCollision3D();
		if (TestMove(stepPosWithClearance, new Vector3(0, -MAX_STEP_HEIGHT * 1.2f, 0), downCheckResult) && (downCheckResult.GetCollider() is StaticBody3D))
		{
			var stepHeight = (stepPosWithClearance.Origin + downCheckResult.GetTravel() - GlobalPosition).Y;
			if (stepHeight > MAX_STEP_HEIGHT || stepHeight <= 0.01 || (downCheckResult.GetPosition() - GlobalPosition).Y> MAX_STEP_HEIGHT) return false;

			StairsAheadRay.GlobalPosition = downCheckResult.GetPosition() + new Vector3(0, MAX_STEP_HEIGHT, 0) + expectedMoveMotion.Normalized() * 0.1f;
			StairsAheadRay.ForceRaycastUpdate();

			if (StairsAheadRay.IsColliding() && !IsSurfaceTooSteep(StairsAheadRay.GetCollisionNormal()))
			{
				SaveCameraPosForSmoothing();
				GlobalPosition = stepPosWithClearance.Origin + downCheckResult.GetTravel();
				ApplyFloorSnap();
				_snappedToStairsLastFrame = true;
				return true;
			}
		}

		return false;
	}

	private void SnapDownToStairsCheck() {
		var didSnap = false;
		StairsBelowRay.ForceRaycastUpdate();
		var floorBelow = StairsBelowRay.IsColliding() && !IsSurfaceTooSteep(StairsBelowRay.GetCollisionNormal());
		var wasOnFloorLastFrame = Engine.GetPhysicsFrames() == _lastFrameOnFloor;
		if (!IsOnFloor() && Velocity.Y <= 0 && (wasOnFloorLastFrame || _snappedToStairsLastFrame) && floorBelow)
		{
			var bodyTestResult = new KinematicCollision3D();
			if (TestMove(GlobalTransform, new Vector3(0, -MAX_STEP_HEIGHT, 0), bodyTestResult))
			{
				SaveCameraPosForSmoothing();
				var translateY = bodyTestResult.GetTravel().Y;
				Position += new Vector3(0, translateY, 0);
				ApplyFloorSnap();
				didSnap = true;
			}
		}
		_snappedToStairsLastFrame = didSnap;
	}

    private void PlayFootstepSound(bool force_play = false)
    {
        GroundCheckRay.ForceRaycastUpdate();
        var floorBelow = GroundCheckRay.IsColliding() && !IsSurfaceTooSteep(GroundCheckRay.GetCollisionNormal());
        if ((floorBelow && Velocity.LengthSquared() > 0.2f) || force_play)
        {
            var collider = GroundCheckRay.GetCollider();

            var _footstep_sound = AudioManager.FootstepSounds["default"][Random.Shared.Next(0, AudioManager.FootstepSounds["default"].Count)];
            _footstep_sound_player.VolumeDb = 0.0f; // set to different values for different sounds (grass sounds are too loud)

            // change footstep depending on chunk block surface
            // if (collider is Chunk)
            // {
            //     var global_pos = GroundCheckRay.GetCollisionPoint()-GroundCheckRay.GetCollisionNormal()*ChunkManager.VOXEL_SCALE*0.5f;

            //     var res = AudioManager.GetFootstepSoundAndVolFromBlockPosition(global_pos);

            //     if (res.Item1 == null)
            //     {
            //         GD.Print("Didn't play footstep, Chunk not found in cache");
            //         return;
            //     }

            //     _footstep_sound = res.Item1;
            //     _footstep_sound_player.VolumeDb = res.Item2;
            // }

            _footstep_sound_player.Stream = _footstep_sound;
            _footstep_sound_player.Play();
        }
    }

	private bool IsSurfaceTooSteep(Vector3 normal)
	{
		return normal.AngleTo(Vector3.Up) > FloorMaxAngle;
	}

	private bool HandleLadderPhysics(float delta) { // move around on ladder and return true if on ladder
		// Keep track of whether already on ladder
        bool wasClimbingLadder = (_curLadderClimbing != null) && _curLadderClimbing.OverlapsBody(this);
        if (!wasClimbingLadder)
        {
            _curLadderClimbing = null;
            foreach (Node node in GetTree().GetNodesInGroup("ladder_area3d"))
            {
                if (node is Area3D ladder && ladder.OverlapsBody(this))
                {
                    _curLadderClimbing = ladder;
                    break;
                }
            }
        }

        if (_curLadderClimbing == null)
            return false;

        // Set up variables
        Transform3D ladderTransform = _curLadderClimbing.GlobalTransform;
        Vector3 posRelToLadder = ladderTransform.AffineInverse() * GlobalPosition;

        float forwardMove = Input.GetActionStrength("Forward") - Input.GetActionStrength("Back");
        float sideMove = Input.GetActionStrength("Right") - Input.GetActionStrength("Left");

        Vector3 ladderForwardMove = ladderTransform.AffineInverse().Basis * 
                                    GetViewport().GetCamera3D().GlobalTransform.Basis * 
                                    new Vector3(0, 0, -forwardMove);

        Vector3 ladderSideMove = ladderTransform.AffineInverse().Basis * 
                                 GetViewport().GetCamera3D().GlobalTransform.Basis * 
                                 new Vector3(sideMove, 0, 0);

        // Strafe velocity
        float ladderStrafeVel = ClimbSpeed * (ladderSideMove.X + ladderForwardMove.X);

        // Climb velocity
        float ladderClimbVel = ClimbSpeed * -ladderSideMove.Z;
        float upWish = new Vector3(0, 1, 0).Rotated(new Vector3(1, 0, 0), Mathf.DegToRad(-45))
                                          .Dot(ladderForwardMove);
        ladderClimbVel += ClimbSpeed * upWish;

        // Dismount logic
        bool shouldDismount = false;

        if (!wasClimbingLadder)
        {
            bool mountingFromTop = posRelToLadder.Y > _curLadderClimbing.GetNode<Node3D>("TopOfLadder").Position.Y;
            if (mountingFromTop)
            {
                if (ladderClimbVel > 0) {
					//GD.Print("dismounting from ladderClimbVel > 0 (mounting from top)");
					shouldDismount = true;
				}
            }
            else
            {
                if ((ladderTransform.AffineInverse().Basis * _wish_dir).Z >= 0) {
					//GD.Print("dismounting from ladderTransform.AffineInverse().Basis * _wish_dir).Z >= 0");
                    shouldDismount = true;
				}
            }

            if (posRelToLadder.Z > 0.1f) {
				//GD.Print("dismounting from Mathf.Abs(posRelToLadder.Z) > 0.1f");
				shouldDismount = true;
			}
        }

        if (IsOnFloor() && ladderClimbVel <= 0) {
			//GD.Print("dismounting from floor and no climb vel");
			shouldDismount = true;
		}

		GD.Print(ladderClimbVel);
		//GD.Print("currLadder ", _curLadderClimbing);
		//GD.Print("shoulddismount ", shouldDismount);

        if (shouldDismount)
        {
            _curLadderClimbing = null;
            return false;
        }

        // Jump off ladder mid-climb
        if (wasClimbingLadder && Input.IsActionJustPressed("Jump"))
        {
            Velocity = _curLadderClimbing.GlobalTransform.Basis.Z * JumpVelocity * 1.5f;
            _curLadderClimbing = null;
            return false;
        }

        Velocity = ladderTransform.Basis * new Vector3(ladderStrafeVel, ladderClimbVel, 0);

        // Snap player onto ladder
        posRelToLadder.Z = 0;
        GlobalPosition = ladderTransform * posRelToLadder;

		PushAwayRigidBodies();
        MoveAndSlide();
        return true;
	}

    private static Vector3 Headbob(float time) {
        var pos = Vector3.Zero;
        pos.Y = Mathf.Sin(time * BOB_FREQ) * BOB_AMP;
        pos.X = Mathf.Cos(time * BOB_FREQ / 2) * BOB_AMP;
        return pos;
    }

    public void TakeDamage(int damage, DamageType damageType) {
        if (IsDead) return;
        if (_iFramesTimer.IsStopped()) {
            _iFramesTimer.WaitTime = _iFramesWaitTime;
            _iFramesTimer.Start();
            CurrentHealth -= damage;
            _damage_vignette_ratio = 1.0f;//Mathf.Clamp(5.0f*damage/MaxHealth,0.0f,1.0f);
            if (CurrentHealth <= 0) {
                CurrentHealth = 0;
                SetCollisionMaskValue(1,false);
                SetCollisionLayerValue(1,false);
                CameraDeathRB.SetCollisionLayerValue(1,true);
                CameraDeathRB.SetCollisionMaskValue(1,true);                
                Camera.Reparent(CameraDeathRB);
                //WeaponManager.Instance.CurrentWeapon = WeaponManager.NullWeapon.Duplicate() as WeaponResource;
                GD.Print("PlayerIMPORT died");
            }
        }
    }

    public bool IsDead => CurrentHealth == 0;

    public void UpdateDamageVignette() {
        _min_damage_vignette_ratio = CurrentHealth < MaxHealth * 0.25f  ? 0.5f * (1.0f - CurrentHealth/MaxHealth) : 0.0f;
        if (CurrentHealth == 0) _min_damage_vignette_ratio = 1.0f;
        _damage_vignette_ratio = Mathf.Lerp(_damage_vignette_ratio, _min_damage_vignette_ratio, 0.1f);
        ((ShaderMaterial)DamageVignette.GetActiveMaterial(0)).Set("shader_parameter/damage_ratio", _damage_vignette_ratio);
    }
}
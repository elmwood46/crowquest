using Godot;
using System;

public partial class NavigationTest : CharacterBody3D
{
    public const float MAX_VIEW_RANGE = 48.0f;
    [Export] public Node3D HeadPosition;
    [Export] public Label DebugLabel;
    private NavigationAgent3D _agent;
    private Timer _random_walk_dir_timer = new(){WaitTime = 1, Autostart = false, OneShot = true};
    private bool _activated = false;
    private bool _run_from_player = true;
    private float _pathing_speed_mult = 1.5f;
    private float _debug_run_duration = 5.0f;
    private enum MoveState
    {
        RUN_FROM_PLAYER,
        PATH_TO_PLAYER,
        NAIVE_CHASE,
        SWITCH_STATES
    }
    private double _phys_secs = 0.0;
    private double _state_timer = 0.0;
    private double _delta_window = 0.1;
    private double _random_timer_wait_time = 1.0;
    private Vector3 _prev_pos = Vector3.Zero;
    private Vector2 _random_walk_dir = Vector2.Zero;
    private MoveState _move_state = MoveState.SWITCH_STATES;
    private static readonly int _num_rays = 3; // number of rays to check for player visibility

    public void ActivateAngent(bool activate)
    {
        _activated = !_activated;
    }

    public void TeleportToPlayer()
    {
        if (Player.Instance != null && IsInstanceValid(Player.Instance))
        {
            GlobalPosition = Player.Instance.GlobalPosition+Vector3.Up*3.0f;;
        }
    }

    public override void _Ready()
    {
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _agent.SetNavigationMap(GetWorld3D().NavigationMap);
        _agent.Radius = 1.0f;
        AddChild(_random_walk_dir_timer);
        _random_walk_dir_timer.Timeout += () =>
        {
            _state_timer = 0.0f;
        };
        _prev_pos = GlobalPosition;
    }

    public override void _Process(double delta)
    {

    }

    public override void _PhysicsProcess(double delta)
    {
       if (!_activated) return;
       float speed = 12f;
       if (_move_state == MoveState.PATH_TO_PLAYER) speed *= _pathing_speed_mult;

        var falling_vel = Velocity.Y;

        // falling
        if (!IsOnFloor())
        {
            falling_vel -= 9.81f * (float)delta;
        }

        _state_timer += delta;
        CheckForStuck(delta);
        ChangeMoveState();
        CalculateMoveDir(delta, out var dir);

        SetDebugLabelText();
        
        var dirvel = dir * speed;
        Velocity = falling_vel*Vector3.Up+dirvel;
        MoveAndSlide();
    }

    private Vector3 CurveAroundObstacles(in Vector3 dir)
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition+dir*2.0f);
        query.Exclude = [GetRid()];
        query.CollideWithBodies = true;

        var result = spaceState.IntersectRay(query);
        if (result.Count == 0) return dir;

        // check 6 angles for a clear path
        for (var i=0; i<6;i++)
        {
            var flip = Mathf.Pow(-1, i);
            var ang = (1+i/2)*(float)Math.PI/4.0f;
            var newdir = dir.Rotated(Vector3.Up, flip*ang);

            query = PhysicsRayQueryParameters3D.Create(GlobalPosition, GlobalPosition+newdir*2.0f);
            query.Exclude = [GetRid()];
            query.CollideWithBodies = true;

            result = spaceState.IntersectRay(query);
            if (result.Count == 0) return newdir;
        }

        return Vector3.Zero;
    }

    private bool IsPlayerVisible()
    {
        if (!IsPlayerInRange(MAX_VIEW_RANGE)) return false;
        for (int i=0; i<_num_rays; i++)
        {
            if (Player.Instance != null && IsInstanceValid(Player.Instance))
            {
                var spaceState = GetWorld3D().DirectSpaceState;

                Vector3 origin = GlobalPosition, end = Player.Instance.GlobalPosition+Vector3.Up*(i+0.5f)*Player.PLAYER_HEIGHT/_num_rays;
                var dirToPlayer = (end - origin).Normalized();
                var query = PhysicsRayQueryParameters3D.Create(origin, end+dirToPlayer*2.0f);
                query.Exclude = [GetRid()];
                query.CollideWithBodies = true;

                var result = spaceState.IntersectRay(query);

                if (result.Count > 0)
                {
                    var res_obj = result["collider"].AsGodotObject();
                    //GD.Print($"Ray {i}: {res_obj}");
                    if (res_obj != null && res_obj is not StaticBody3D && res_obj is Player)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void SetDebugLabelText()
    {
        if (DebugLabel != null && IsInstanceValid(DebugLabel))
        {
            DebugLabel.Text = $"Move State: {_move_state}\n" +
                              $"Player Visible: {IsPlayerVisible()}\n" +
                              $"Random_Walk_Timer stopped: {_random_walk_dir_timer.IsStopped()}\n" +
                              $"T = {_random_walk_dir_timer.WaitTime}";
        }
    }

    private void ChangeMoveState()
    {
        if (_move_state != MoveState.SWITCH_STATES) return;

        _state_timer = 0.0f;

        if (IsPlayerVisible())
        {
            _move_state = MoveState.NAIVE_CHASE;
        }
        else
        {
            _agent.SetTargetPosition(Player.Instance.GlobalPosition);
            _agent.GetNextPathPosition();
            _move_state = MoveState.PATH_TO_PLAYER;
        }
    }

    public void SetDebugRunTimer()
    {
        var run_timer = new Timer(){
            WaitTime = _debug_run_duration,
            Autostart = false,
            OneShot = true
        };
        run_timer.Timeout += () =>
        {
            _run_from_player = false;
            run_timer.QueueFree();
        };
        _move_state = MoveState.RUN_FROM_PLAYER;
        _run_from_player = true;
        AddChild(run_timer);
        run_timer.Start();
    }

    public void SetDebugRunDuration(float duration)
    {
        _debug_run_duration = duration;
    }

    private void CalculateMoveDir(double delta, out Vector3 move_dir)
    {
        // if we got stuck on something, random walk
        if (!_random_walk_dir_timer.IsStopped())
        {
            move_dir = new Vector3(_random_walk_dir.X, 0.0f, _random_walk_dir.Y);
            return;
        }

        if (_move_state == MoveState.RUN_FROM_PLAYER)
        {
            move_dir = -GetYDirectionToPlayer();
            move_dir = CurveAroundObstacles(move_dir);
            if (!_run_from_player)
            {
                _move_state = MoveState.SWITCH_STATES;
            }
        }
        else if (_move_state == MoveState.PATH_TO_PLAYER)
        {
            if (_agent.IsNavigationFinished())
            {
                GD.Print("Finished path to player");
                _move_state = MoveState.SWITCH_STATES;
                move_dir = Player.Instance.GlobalPosition - GlobalPosition;
                move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
                return;
            }

            // check for state switch
            if (Mathf.RoundToInt(_state_timer*1000)%100 == 0) // every 0.1 seconds
            {   
                if (IsPlayerVisible())
                {
                    move_dir = Player.Instance.GlobalPosition - GlobalPosition;
                    move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
                    _move_state = MoveState.NAIVE_CHASE;
                    _state_timer=0;
                    return;
                }
            }

            // recalculate path every 1 seconds
            if (_state_timer > 1.0) 
            {
                _state_timer -= 1.0;
                _agent.SetTargetPosition(Player.Instance.GlobalPosition);
            }

            move_dir = _agent.GetNextPathPosition() - GlobalPosition;
            move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
        }
        else if (_move_state == MoveState.NAIVE_CHASE)
        {
            if (!IsPlayerVisible()) _move_state = MoveState.SWITCH_STATES;
            move_dir = Player.Instance.GlobalPosition - GlobalPosition;
            move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
        }
        else
        {
            move_dir = Vector3.Zero;
        }
    }

    private void CheckForStuck(double delta)
    {
        if (!_random_walk_dir_timer.IsStopped()) return;

        _delta_window = _move_state switch
        {
            MoveState.PATH_TO_PLAYER => 0.1,
            MoveState.NAIVE_CHASE => 0.1,
            _ => 0.1
        };

        _random_timer_wait_time = _move_state switch
        {
            MoveState.PATH_TO_PLAYER => 0.1,
            MoveState.NAIVE_CHASE => 0.1,
            _ => 0.1
        };

        // recalibrate when stuck
        _phys_secs += delta;
        if (_phys_secs > _delta_window)
        {
            _phys_secs -= _delta_window;
            if (_prev_pos.DistanceSquaredTo(GlobalPosition) <= 0.01f)
            {
                GD.Print("Stuck, recalibrating");
                ResetRandomWalkDir();
                _phys_secs=0;
            }
            _prev_pos = GlobalPosition;
        }
    }

    private void ResetRandomWalkDir()
    {
        _random_walk_dir_timer.Stop();
        _random_walk_dir = new Vector2(Random.Shared.NextSingle() * 2 - 1, Random.Shared.NextSingle() * 2 - 1).Normalized();
        _random_walk_dir_timer.WaitTime = _random_timer_wait_time; // 1 seconds random walk
        _random_walk_dir_timer.Start();
    }

    private Vector3 GetYDirectionToPlayer()
    {
        return new Vector3(GlobalPosition.X, 0.0f, GlobalPosition.Z).DirectionTo(new Vector3(Player.Instance.GlobalPosition.X, 0.0f, Player.Instance.GlobalPosition.Z));
    }

    private bool IsPlayerInRange(float range)
    {
        return Player.Instance.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= range*range;
    }
}

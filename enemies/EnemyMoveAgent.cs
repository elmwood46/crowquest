using Godot;
using System;

public partial class EnemyMoveAgent : Node
{
    private Enemy _parent;
    private NavigationAgent3D _agent;
    private Timer _random_walk_dir_timer = new(){WaitTime = 1, Autostart = false, OneShot = true};
    private bool _activated = true;
    private bool _run_from_player = true;
    private float _pathing_speed_mult = 1.5f; // speed when moving along path
    private float _debug_run_duration = 5.0f;
    public enum MoveState
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
    public MoveState State {get;private set;} = MoveState.SWITCH_STATES;
    public void ToggleAgentActive() => _activated = !_activated;

    public override void _Ready()
    {
        _parent = GetParent<Enemy>();
        if (_parent == null)
        {
            throw new Exception("EnemyMoveAgent must be a child of an Enemy node.");
        }

        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _agent.SetNavigationMap(_parent.GetWorld3D().NavigationMap);
        _agent.Radius = 1.0f;
        AddChild(_random_walk_dir_timer);
        _random_walk_dir_timer.Timeout += () =>
        {
            _state_timer = 0.0f;
        };
        _prev_pos = _parent.GlobalPosition;
    }

    /// <summary>
    /// This function is called in the _PhysicsProcess of the parent node.
    /// It sets the parent velocity.
    /// It handles the movement logic of the enemy agent.
    /// </summary>
    /// <param name="delta"></param>
    public void GetMovementVector(double delta, out Vector3 dir, out float speed_mult)
    {
       dir = Vector3.Zero;
       speed_mult = 1f;
       if (!_activated) return;
       float speed = _parent.Speed;
       if (State == MoveState.PATH_TO_PLAYER) speed *= _pathing_speed_mult;

        _state_timer += delta;
        CheckForStuck(delta);
        ChangeMoveState();
        CalculateMoveDir(out dir);
        speed_mult = speed;
    }

    public void TeleportToPlayer()
    {
        if (Player.Instance != null && IsInstanceValid(Player.Instance))
        {
            _parent.GlobalPosition = Player.Instance.GlobalPosition+Vector3.Up*3.0f;;
        }
    }

    private Vector3 CurveAroundObstacles(in Vector3 dir)
    {
        var spaceState = _parent.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(_parent.GlobalPosition, _parent.GlobalPosition+dir*2.0f);
        query.Exclude = [_parent.GetRid()];
        query.CollideWithBodies = true;

        var result = spaceState.IntersectRay(query);
        if (result.Count == 0) return dir;

        // check 6 angles for a clear path
        for (var i=0; i<6;i++)
        {
            var flip = Mathf.Pow(-1, i);
            var ang = (1+i/2)*(float)Math.PI/4.0f;
            var newdir = dir.Rotated(Vector3.Up, flip*ang);

            query = PhysicsRayQueryParameters3D.Create(_parent.GlobalPosition, _parent.GlobalPosition+newdir*2.0f);
            query.Exclude = [_parent.GetRid()];
            query.CollideWithBodies = true;

            result = spaceState.IntersectRay(query);
            if (result.Count == 0) return newdir;
        }

        return Vector3.Zero;
    }
    private void ChangeMoveState()
    {
        if (State != MoveState.SWITCH_STATES) return;

        _state_timer = 0.0f;

        if (_parent.IsPlayerVisible())
        {
            State = MoveState.NAIVE_CHASE;
        }
        else
        {
            _agent.SetTargetPosition(Player.Instance.GlobalPosition);
            _agent.GetNextPathPosition();
            State = MoveState.PATH_TO_PLAYER;
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
        State = MoveState.RUN_FROM_PLAYER;
        _run_from_player = true;
        AddChild(run_timer);
        run_timer.Start();
    }

    public void SetDebugRunDuration(float duration)
    {
        _debug_run_duration = duration;
    }

    private void CalculateMoveDir(out Vector3 move_dir)
    {
        // if we got stuck on something, random walk
        if (!_random_walk_dir_timer.IsStopped())
        {
            move_dir = new Vector3(_random_walk_dir.X, 0.0f, _random_walk_dir.Y);
            return;
        }

        if (State == MoveState.RUN_FROM_PLAYER)
        {
            move_dir = -_parent.XZDirectionToPlayer();
            move_dir = CurveAroundObstacles(move_dir);
            if (!_run_from_player)
            {
                State = MoveState.SWITCH_STATES;
            }
        }
        else if (State == MoveState.PATH_TO_PLAYER)
        {
            if (_agent.IsNavigationFinished())
            {
                //GD.Print("Finished path to player");
                State = MoveState.SWITCH_STATES;
                move_dir = Player.Instance.GlobalPosition - _parent.GlobalPosition;
                move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
                return;
            }

            // check for state switch
            if (Mathf.RoundToInt(_state_timer*1000)%100 == 0) // every 0.1 seconds
            {   
                if (_parent.IsPlayerVisible())
                {
                    move_dir = Player.Instance.GlobalPosition - _parent.GlobalPosition;
                    move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
                    State = MoveState.NAIVE_CHASE;
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

            move_dir = _agent.GetNextPathPosition() - _parent.GlobalPosition;
            move_dir = (move_dir*new Vector3(1,0,1)).Normalized();
        }
        else if (State == MoveState.NAIVE_CHASE)
        {
            if (!_parent.IsPlayerVisible()) State = MoveState.SWITCH_STATES;
            move_dir = Player.Instance.GlobalPosition - _parent.GlobalPosition;
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

        _delta_window = State switch
        {
            MoveState.PATH_TO_PLAYER => 0.1,
            MoveState.NAIVE_CHASE => 0.1,
            _ => 0.1
        };

        _random_timer_wait_time = State switch
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
            if (_prev_pos.DistanceSquaredTo(_parent.GlobalPosition) <= 0.01f)
            {
                //GD.Print("Stuck, recalibrating");
                ResetRandomWalkDir();
                _phys_secs=0;
            }
            _prev_pos = _parent.GlobalPosition;
        }
    }

    private void ResetRandomWalkDir()
    {
        _random_walk_dir_timer.Stop();
        _random_walk_dir = new Vector2(Random.Shared.NextSingle() * 2 - 1, Random.Shared.NextSingle() * 2 - 1).Normalized();
        _random_walk_dir_timer.WaitTime = _random_timer_wait_time; // 1 seconds random walk
        _random_walk_dir_timer.Start();
    }
}

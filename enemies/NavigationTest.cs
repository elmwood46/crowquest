using Godot;
using System;

public partial class NavigationTest : CharacterBody3D
{
    private NavigationAgent3D _agent;
    private Timer _random_walk_dir_timer = new(){WaitTime = 1, Autostart = false, OneShot = true};
    private bool _activated = false;

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
        AddChild(_random_walk_dir_timer);
    }

    public override void _Process(double delta)
    {
        if (Player.Instance != null && IsInstanceValid(Player.Instance)) _agent.SetTargetPosition(Player.Instance.GlobalPosition);
        if (_agent.IsNavigationFinished()) {
            //GD.Print("finished navigation");
        }
    }


    private double _phys_secs = 0.0;
    private Vector3 _prev_pos = Vector3.Zero;
    private Vector2 _random_walk_dir = Vector2.Zero;
    public override void _PhysicsProcess(double delta)
    {
       // if (_agent.IsNavigationFinished()) return;
       if (!_activated) return;

        var vel = Velocity;

        if (!IsOnFloor())
        {
            vel.Y -= 9.81f * (float)delta;
        }

        // check for getting stuck; trigger random walk
        _phys_secs += delta;
        if (_phys_secs > 0.2)
        {
            _phys_secs -= 0.2;
            if (_prev_pos.DistanceSquaredTo(GlobalPosition) < 1.0f)
            {
                _random_walk_dir_timer.Stop();
                _random_walk_dir = new Vector2(Random.Shared.NextSingle() * 2 - 1, Random.Shared.NextSingle() * 2 - 1).Normalized();
                _random_walk_dir_timer.WaitTime = Random.Shared.NextDouble() + 0.001; // 1 seconds random walk
                _random_walk_dir_timer.Start();
            }
            _prev_pos = GlobalPosition;
        }

        Vector3 dir;
        if (!_random_walk_dir_timer.IsStopped())
        {
            dir = new Vector3(_random_walk_dir.X, 0.0f, _random_walk_dir.Y).Normalized();
        }
        else 
        {
            var nextPos = _agent.GetNextPathPosition();
            dir = (nextPos - GlobalPosition).Normalized()* new Vector3(1,0,1);
        }

        float speed = 10.0f;
        var dirvel = dir * speed;
        Velocity = vel.Y*Vector3.Up+dirvel;
        MoveAndSlide();
    }

    public Vector3 GetYDirectionToPlayer()
    {
        return new Vector3(GlobalPosition.X, 0.0f, GlobalPosition.Z).DirectionTo(new Vector3(Player.Instance.GlobalPosition.X, 0.0f, Player.Instance.GlobalPosition.Z));
    }

    public bool IsPlayerInRange(float range)
    {
        return Player.Instance.GlobalPosition.DistanceSquaredTo(GlobalPosition) <= range*range;
    }
}

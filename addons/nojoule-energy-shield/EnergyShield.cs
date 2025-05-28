using Godot;
using System;
using System.Collections.Generic;

public partial class EnergyShield : RigidBody3D
{
    private const int MaxImpacts = 10;

    [Export] public MeshInstance3D ShieldMesh { get; set; }
    [Export] public CollisionShape3D ShieldCollisionShape { get; set; }
    [Export] public Curve AnimationCurve { get; set; }
    [Export] public float AnimTime { get; set; } = 4.0f;
    [Export] public Vector3 ShieldOrigin { get; set; } = new Vector3(0.0f, 0.5f, 0.0f);
    [Export] public bool SplitFrontBack { get; set; } = false;
    [Export] public bool HandleInputEvents { get; set; } = true;
    [Export] public bool BodyEnteredImpact { get; set; } = false;
    [Export] public bool BodyShapeEnteredImpact { get; set; } = false;
    [Export] public int Health { get; set; } = 100;
    [Export] public int MaxHealth { get; set; } = 100;
    [Export] public Color ShieldMaxColor { get; set; } = new Color(0.0f, 1.0f, 0.0f, 1.0f);
    [Export] public Color ShieldMinColor { get; set; } = new Color(1.0f, 0.0f, 0.0f, 1.0f);
    
    public bool IsActive => Visible && Health > 0;

    public float ShieldRadius => ShieldCollisionShape.Scale.X * ((SphereShape3D)ShieldCollisionShape.Shape).Radius;

    private int _currentImpact = 0;
    private readonly bool[] _animate = new bool[MaxImpacts];
    private readonly float[] _elapsedTime = new float[MaxImpacts];
    private readonly Vector3[] _impactOrigin = new Vector3[MaxImpacts];
    private Vector3 _origin_generate = Vector3.Zero;

    private float _generateTime = 1.0f;
    private bool _collapsed = false;
    private bool _generatingOrCollapsing = false;

    private ShaderMaterial _material;

    private Timer _regen_delay_timer;
    private const double RegenTime = 10d; // Cooldown to regenerate shield after collapse
    private const float RegenAmount = 0.2f; // Percent of shield to regenerate after collapse 

    private Timer _recharge_delay_timer; // Timer for recharging shield after impact
    private const double RechargeTime = 2.0f; // Time to recharge shield after impact

    private float _recharge_rate = 5.0f; // points of shield per second
    private float _recharge_amount = 0.0f;
    public static readonly Vector3 ShieldOffset = new(0.0f, 0.5f, 0.0f);

    public override void _Ready()
    {
        

        Health = MaxHealth;
        _material = ShieldMesh.GetActiveMaterial(0) as ShaderMaterial;

        // recharge timer - shield recharges HP after taking damage. This is the delay.
        _recharge_delay_timer = new Timer() { OneShot = true, WaitTime = RechargeTime };
        AddChild(_recharge_delay_timer);

        // regen timer - shield regenerates itself after a delay
        _regen_delay_timer = new Timer() { OneShot = true, WaitTime = RegenTime };
        _regen_delay_timer.Timeout += () =>
        {
            GD.Print("Energy Shield: Regenerating shield health");
            InitalizeShield(Mathf.RoundToInt(RegenAmount * MaxHealth));
        };
        AddChild(_regen_delay_timer);

        if (!Engine.IsEditorHint() && SplitFrontBack && _material.NextPass != null)
        {
            _material.NextPass = null;
            ShieldMesh.SetSurfaceOverrideMaterial(0, _material.Duplicate() as ShaderMaterial);
            _material = ShieldMesh.GetActiveMaterial(0) as ShaderMaterial;

            var backShader = GD.Load<Shader>("res://addons/nojoule-energy-shield/shield_back.gdshader");
            var frontShader = GD.Load<Shader>("res://addons/nojoule-energy-shield/shield_front.gdshader");

            _material.Shader = backShader;
            var frontMat = _material.Duplicate() as ShaderMaterial;
            frontMat.Shader = frontShader;
            _material.NextPass = frontMat;
        }

        BodyEntered += (body) =>
        {
            if (BodyEnteredImpact && body is Enemy e)
            {
                Impact(e.GlobalPosition);
                if (Health <= 1) CollapseFrom(e.GlobalPosition);
            }
        };

        // start with shield collapsed until initialized
        Visible = false;
        _collapsed = true;
        _generatingOrCollapsing = false;
        FreezeMode = FreezeModeEnum.Static;
        SetCollisionLayerValue(9, false); // Disable collision layer 9
        SetCollisionMaskValue(9, false); // Disable collision mask 9
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        GravityScale = 0f;
        LinearVelocity = GlobalPosition.Lerp(Player.Instance.GlobalPosition + ShieldOffset, 0.25f);
        if (LinearVelocity.IsZeroApprox()) LinearVelocity = Vector3.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        var deltaF = (float)delta;
        var _prev_glob_pos = ShieldOrigin;
        ShieldOrigin = GlobalPosition;
        var _delta_glob_pos = ShieldOrigin - _prev_glob_pos;
        for (var i=0;i<_impactOrigin.Length;i++)
        {
            _impactOrigin[i] += _delta_glob_pos;
        }
        UpdateMaterial("_origin_impact", _impactOrigin);

        _origin_generate += _delta_glob_pos;
        UpdateMaterial("_origin_generate", _origin_generate);

        RechargeShieldHealth(deltaF);

        // change shield color based on health
        if (!_collapsed) UpdateMaterial("_color_shield", ShieldMinColor.Lerp(ShieldMaxColor, (float)Health / MaxHealth));

        UpdateGenerateAndCollapseProperties(deltaF);

        RippleShieldImpacts(deltaF);
    }

    private void RechargeShieldHealth(float delta)
    {
        if (Health < MaxHealth && _recharge_delay_timer.IsStopped() && _regen_delay_timer.IsStopped())
        {
            _recharge_amount += delta * _recharge_rate;
            while (_recharge_amount >= 1f)
            {
                _recharge_amount -= 1f;
                Health++;
            }
        }
    }

    private void UpdateGenerateAndCollapseProperties(float delta)
    {
        // generate or collapse the shield
        if (_generatingOrCollapsing && _generateTime <= 1.0f)
        {
            _generateTime += delta;
            //ShieldCollisionShape.Scale = Mathf.Max(0.1f, _collapsed ? _generateTime : 1.0f - _generateTime) * Vector3.One;
            UpdateMaterial("_time_generate", _generateTime);
        }
        else if (_generatingOrCollapsing)
        {
            // if _collapsed == true (thus generating), we are now fully generated
            // else if _collapsed == false (thus collapsing), we are now fully collapsed
            _collapsed = !_collapsed;

            _generatingOrCollapsing = false;

            //ShieldCollisionShape.Scale = _collapsed ? Vector3.One*0.1f : Vector3.One;
        }
    }

    private void RippleShieldImpacts(float delta)
    {
        // do impactsd
        bool anyUpdate = false;
        var timeImpacts = new List<float>();
        for (int i = 0; i < MaxImpacts; i++)
        {
            if (_animate[i])
            {
                anyUpdate = true;
                if (_elapsedTime[i] < AnimTime)
                {
                    float t = _elapsedTime[i] / AnimTime;
                    timeImpacts.Add(AnimationCurve.Sample(t));
                    _elapsedTime[i] += delta;
                }
                else
                {
                    timeImpacts.Add(0.0f);
                    _elapsedTime[i] = 0.0f;
                    _animate[i] = false;
                }
            }
            else
            {
                timeImpacts.Add(0.0f);
            }
        }
        if (anyUpdate)
        {
            UpdateMaterial("_time_impact", timeImpacts.ToArray());
        }
    }

    // ================================================================================================================== 
    // Public methods for external control of the shield
    // ================================================================================================================== 

    public void InitalizeShield(int shield_health)
    {
        FreezeMode = FreezeModeEnum.Kinematic;
        Health = shield_health;
        Visible = true;

        SetCollisionLayerValue(9, true);
        SetCollisionMaskValue(9, true);
        _collapsed = true;
        _generatingOrCollapsing = false;
        GenerateFrom(ShieldOrigin+Vector3.Up);
    }

    public void DamageEnergyShieldAtPos(int damage, DamageTypeFlagEnum damageType, Vector3 pos)
    {
        // If the shield is not active, we do not take damage
        if (!IsActive) return;

        // If the shield is active, we take damage
        Health -= damage;

        if (Health <= 0)
        {
            Health = 0;
            _recharge_delay_timer.Stop();
            _regen_delay_timer.Stop();
            _regen_delay_timer.Start(RegenTime);

            FreezeMode = FreezeModeEnum.Static;
            Visible = false;
            SetCollisionLayerValue(9, false);
            SetCollisionMaskValue(9, false);
            _collapsed = false;
            _generatingOrCollapsing = false;
            CollapseFrom(pos);
        }
        else
        {
            Impact(pos);
            _recharge_delay_timer.Start(RechargeTime);
        }
    }

    private void UpdateMaterial(string name, Variant value)
    {
        _material.SetShaderParameter(name, value);
        if (!Engine.IsEditorHint() && SplitFrontBack && _material.NextPass is ShaderMaterial nextPass)
        {
            nextPass.SetShaderParameter(name, value);
        }
    }

    public void Generate()
    {
        if (_generatingOrCollapsing || !_collapsed)
            return;

        GenerateFrom(ShieldOrigin);
        UpdateMaterial("_relative_origin_generate", true);
    }

    public void GenerateFrom(Vector3 pos)
    {
        if (_generatingOrCollapsing || !_collapsed) return;

        _generatingOrCollapsing = true;
        _generateTime = 0.0f;
        UpdateMaterial("_relative_origin_generate", false);
        UpdateMaterial("_collapse", false);
        _origin_generate = pos;
        UpdateMaterial("_origin_generate", _origin_generate);
        UpdateMaterial("_time_generate", _generateTime);
    }

    public void Collapse()
    {
        if (_generatingOrCollapsing || _collapsed)
            return;

        CollapseFrom(ShieldOrigin);
        UpdateMaterial("_relative_origin_generate", true);
    }

    public void CollapseFrom(Vector3 pos)
    {
        if (_generatingOrCollapsing || _collapsed)
            return;

        _generatingOrCollapsing = true;
        _generateTime = 0.0f;
        UpdateMaterial("_relative_origin_generate", false);
        UpdateMaterial("_collapse", true);
        _origin_generate = pos;
        UpdateMaterial("_origin_generate", _origin_generate);
        UpdateMaterial("_time_generate", _generateTime);
    }

    public void Impact(Vector3 pos)
    {
        _animate[_currentImpact] = true;
        _elapsedTime[_currentImpact] = 0.0f;
        _impactOrigin[_currentImpact] = pos;

        UpdateMaterial("_origin_impact", _impactOrigin);

        var timeImpacts = new List<float>();
        for (int i = 0; i < MaxImpacts; i++)
        {
            if (_animate[i] && _elapsedTime[i] < AnimTime)
            {
                float t = _elapsedTime[i] / AnimTime;
                timeImpacts.Add(AnimationCurve.Sample(t));
            }
            else
            {
                timeImpacts.Add(0.0f);
                _elapsedTime[i] = 0.0f;
                _animate[i] = false;
            }
        }

        UpdateMaterial("_time_impact", timeImpacts.ToArray());

        _currentImpact = (_currentImpact + 1) % MaxImpacts;
    }
}
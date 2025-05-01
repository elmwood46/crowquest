using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class EnemyComputeShaderManager : Node3D
{
    public static readonly RDShaderFile ShaderFile = ResourceLoader.Load<RDShaderFile>("res://enemies/enemy_logic.glsl"); 

    const int WRAPAROUND = 100;

    [Export] public bool DebugLog = false;
    [Export] public int NumEnemies = 1000;
    private readonly List<Vector3> _boid_pos = new();
    private readonly List<Vector3> _boid_vel = new();
    private Vector2I _image_size;
    private Image _boid_data;
    private ImageTexture _boid_data_texture;
    private Texture2Drd _boid_data_texture_rd;

    [ExportCategory("Enemy Logic Args")]
    [Export(PropertyHint.Range, "0,50")] public float FriendRadius = 10.0f;
    [Export(PropertyHint.Range, "0,50")] public float AvoidRadius = 5.0f;
    [Export(PropertyHint.Range, "0,100")] public float MinVel = 0f;
    [Export(PropertyHint.Range, "0,100")] public float MaxVel = 75.0f;
    [Export(PropertyHint.Range, "0,100")] public float AlignmentFactor = 10.0f;
    [Export(PropertyHint.Range, "0,100")] public float CohesionFactor = 1.0f;
    [Export(PropertyHint.Range, "0,100")] public float SeparationFactor = 20.0f;

    private Vector3 _boidScale = new(0.5f, 0.5f, 0.5f);
    [Export] public Vector3 BoidScale
    {
        get => _boidScale;
        set
        {
            _boidScale = value;
            /*if (IsInsideTree())
                (BoidParticles.ProcessMaterial as ShaderMaterial)?.SetShaderParameter("scale", _boidScale);*/
        }
    }

    [ExportCategory("Other")]
    private bool _pause = false;
    [Export] public bool Pause
    {
        get => _pause;
        set => _pause = value;
    }

    // GPU Variables
    private RenderingDevice _rd;
    private Rid _boid_compute_shader;
    private Rid _boid_pipeline;
    private Godot.Collections.Array<RDUniform> _bindings;
    private Rid _uniform_set;
    private Rid _boid_pos_buffer;
    private Rid _boid_vel_buffer;
    private Rid _params_buffer;
    private RDUniform _params_uniform;
    private Rid _boid_data_buffer;

    private bool _skipped_first_physics_step = false;

    public static EnemyComputeShaderManager Instance {get; private set;}

    public override void _Ready()
    {
        // DEBUG set mouse mode enum
        //Input.MouseMode = Input.MouseModeEnum.Captured;
        Instance = this;

        var ceil_boids = (int)Mathf.Ceil(Mathf.Sqrt(NumEnemies));
        _image_size = new Vector2I(ceil_boids*2,ceil_boids);

        RegenerateBoids();
        if (DebugLog)
        {
            for (int i = 0; i < _boid_pos.Count; i++)
            {
                GD.Print($"Boid: {i} Pos: {_boid_pos[i]} Vel: {_boid_vel[i]}");
            }
        }

        _boid_data = Image.CreateEmpty(_image_size.X, _image_size.Y, false, Image.Format.Rgbah);


        _boid_data_texture = ImageTexture.CreateFromImage(_boid_data);
        
        //(BoidParticles.ProcessMaterial as ShaderMaterial).SetShaderParameter("boid_data", _boid_data_texture);
        
        RenderingServer.CallOnRenderThread(Callable.From(() => SetupComputeShader()));
        RenderingServer.CallOnRenderThread(Callable.From(() => UpdateBoidsGpu(0.0f)));
    }

    private void RegenerateBoids() 
    {
        _boid_pos.Clear();
        _boid_vel.Clear();
        for (int i = 0; i < NumEnemies; i++)
        {
            _boid_pos.Add(new Vector3(Random.Shared.NextSingle() * WRAPAROUND, Random.Shared.NextSingle() * WRAPAROUND, Random.Shared.NextSingle() * WRAPAROUND));
            _boid_vel.Add(new Vector3((Random.Shared.NextSingle() * 2 - 1) * MaxVel, (Random.Shared.NextSingle() * 2 - 1) * MaxVel, (Random.Shared.NextSingle() * 2 - 1) * MaxVel));
        }
    }

    private void UpdateBoidsGpu(float delta)
    {
        var paramsBufferBytes = GenerateParameterBuffer(delta);
        var posBufferBytes = Vector3ArrayToBytes(_boid_pos.ToArray());
        _rd.BufferUpdate(_params_buffer, 0, (uint)paramsBufferBytes.Length, paramsBufferBytes);
        _rd.BufferUpdate(_boid_pos_buffer, 0, (uint)posBufferBytes.Length, posBufferBytes);
        RunComputeShader(_boid_pipeline);
    }

    private void RunComputeShader(Rid pipeline)
    {
        var computeList = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(computeList, pipeline);
        _rd.ComputeListBindUniformSet(computeList, _uniform_set, 0);
        _rd.ComputeListDispatch(computeList, (uint)Math.Ceiling(NumEnemies / 1024.0), 1, 1);
        _rd.ComputeListEnd();
    }

    private void UpdateDataTexture()
    {
        var boid_data_image_buffer = _rd.TextureGetData(_boid_data_buffer, 0);
        _boid_data.SetData(_image_size.X, _image_size.Y, false, Image.Format.Rgbah, boid_data_image_buffer);
        _boid_data_texture.Update(_boid_data);
    }

    private void SetupComputeShader()
    {
        _rd = RenderingServer.GetRenderingDevice();
        var shaderSpirv = ShaderFile.GetSpirV();
        _boid_compute_shader = _rd.ShaderCreateFromSpirV(shaderSpirv);
        _boid_pipeline = _rd.ComputePipelineCreate(_boid_compute_shader);

        _boid_pos_buffer = GenerateVec3FloatBuffer(_boid_pos.ToArray());
        var boidPosUniform = GenerateUniform(_boid_pos_buffer, RenderingDevice.UniformType.StorageBuffer, 0);

        _boid_vel_buffer = GenerateVec3FloatBuffer(_boid_vel.ToArray());
        var boidVelUniform = GenerateUniform(_boid_vel_buffer, RenderingDevice.UniformType.StorageBuffer, 1);

        var paramsBufferBytes = GenerateParameterBuffer(0);
        _params_buffer = _rd.StorageBufferCreate((uint)paramsBufferBytes.Length, paramsBufferBytes);
        _params_uniform = GenerateUniform(_params_buffer, RenderingDevice.UniformType.StorageBuffer, 2);

        var fmt = new RDTextureFormat
        {
            Width = (uint)_image_size.X,
            Height = (uint)_image_size.Y,
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanCopyFromBit
        };

        var view = new RDTextureView();
        _boid_data_buffer = _rd.TextureCreate(fmt, view, new Godot.Collections.Array<byte[]>{_boid_data.GetData()});
        _boid_data_texture_rd = new()
        {
            TextureRdRid = _boid_data_buffer
        };
        var boidDataBufferUniform = GenerateUniform(_boid_data_buffer, RenderingDevice.UniformType.Image, 3);

        _bindings = new Godot.Collections.Array<RDUniform> { boidPosUniform, boidVelUniform, _params_uniform, boidDataBufferUniform };
        _uniform_set = _rd.UniformSetCreate(_bindings, _boid_compute_shader, 0);
    }

    private Rid GenerateVec3FloatBuffer(Vector3[] data)
    {
        var dataBufferBytes = Vector3ArrayToBytes(data);
        return _rd.StorageBufferCreate((uint)dataBufferBytes.Length, dataBufferBytes);
    }

    private static byte[] Vector3ArrayToBytes(Vector3[] data)
    {
        var floats = ConvertVector3ArrayToFloatArray(data);
        var dataBufferBytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, dataBufferBytes, 0, dataBufferBytes.Length);
        return dataBufferBytes;
    }

    private static float[] ConvertVector3ArrayToFloatArray(Vector3[] vectors)
    {
        float[] floatArray = new float[vectors.Length * 3]; // Each Vector3 has 3 floats

        for (int i = 0; i < vectors.Length; i++)
        {
            floatArray[i * 3] = vectors[i].X;
            floatArray[i * 3 + 1] = vectors[i].Y;
            floatArray[i * 3 + 2] = vectors[i].Z;
        }

        return floatArray;
    }

    private Rid GenerateIntBuffer(int size)
    {
        var dataBufferBytes = new byte[size * sizeof(int)];
        return _rd.StorageBufferCreate((uint)dataBufferBytes.Length, dataBufferBytes);
    }

    private static RDUniform GenerateUniform(Rid dataBuffer, RenderingDevice.UniformType type, int binding)
    {
        var dataUniform = new RDUniform
        {
            UniformType = type,
            Binding = binding
        };
        dataUniform.AddId(dataBuffer);
        return dataUniform;
    }

    private byte[] GenerateParameterBuffer(float delta)
    {
        var float_arr = new float[]
        {
            NumEnemies,
            _image_size.X,
            _image_size.Y,
            Player.Instance.GlobalPosition.X,
            Player.Instance.GlobalPosition.Y,
            Player.Instance.GlobalPosition.Z,
            FriendRadius,
            AvoidRadius,
            MinVel,
            MaxVel,
            AlignmentFactor,
            CohesionFactor,
            SeparationFactor,
            delta,
        };
        var dataBufferBytes = new byte[float_arr.Length * sizeof(float)];
        Buffer.BlockCopy(float_arr, 0, dataBufferBytes, 0, dataBufferBytes.Length);
        return dataBufferBytes;
    }

    private void FreeRids()
    {
        _rd.Sync();
        _rd.FreeRid(_uniform_set);
        _rd.FreeRid(_boid_data_buffer);
        _rd.FreeRid(_params_buffer);
        _rd.FreeRid(_boid_pos_buffer);
        _rd.FreeRid(_boid_vel_buffer);
        _rd.FreeRid(_boid_pipeline);
        _rd.FreeRid(_boid_compute_shader);
        _rd.Free();
    }
    
    #region retrieve data from texture
    public static Vector3 GetEnemyVelocityFromTexture(int idx)
    {
        if (idx >= Instance.NumEnemies || idx < 0) throw new Exception($"Index {idx} out of range");
        var pixelPos = new Vector2I(idx*2 % Instance._image_size.X, idx / Instance._image_size.Y);
        var vel = Instance._boid_data.GetPixel(pixelPos.X+1, pixelPos.Y);
        return new Vector3(vel.R, vel.G, vel.B);
    }

    public static void SetEnemyPosition(int idx, Vector3 pos)
    {
        if (idx >= Instance.NumEnemies || idx < 0) throw new Exception($"Index {idx} out of range");
        Instance._boid_pos[idx] = pos;
    }
    #endregion


    #region physics process and exit tree
    public override void _ExitTree()
    {
        RenderingServer.CallOnRenderThread(new Callable(this,MethodName.FreeRids));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_skipped_first_physics_step) _rd.Sync();
        else _skipped_first_physics_step = true;

        GetWindow().Title = $"Boids: {NumEnemies}, FPS: {Engine.GetFramesPerSecond()}";
		RenderingServer.CallOnRenderThread(Callable.From(() => UpdateBoidsGpu((float)delta)));
        UpdateDataTexture();
    }
    #endregion
}

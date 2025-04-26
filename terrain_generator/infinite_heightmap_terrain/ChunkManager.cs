using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

public partial class ChunkManager : Node
{
    [Export] public bool Enabled = true;
    [Export] public int RenderDistance = 5;
    [Export (PropertyHint.Range, "1,32,1")] public int ChunkSize
    {
        get => _chunkSize;
        set {
            _chunkSize = value;
            UpdateAllChunks();
        }
    }
    [Export (PropertyHint.Range, "1.0,100.0,1.0")] public float MaxHeight
    {
        get => _max_height;
        set {
            _max_height = value;
            UpdateAllChunks();
        }
    }
    [Export (PropertyHint.Range, "0.03125,2,0.03125")] public float PlaneResolution
    {
        get => _planeResolution;
        set {
            _planeResolution = value;
            UpdateAllChunks();
        }
    }
    [Export] public Noise NoiseTexture { get; set; }

    public float InverseResolution => 1.0f / _planeResolution;
    private int _chunkSize = 32;
    private float _max_height = 0.0f; //20.0f;
    private float _planeResolution = 0.25f;

    private readonly ConcurrentDictionary<ChunkPlane, Vector2I> _chunkToPosition = new();
	private readonly ConcurrentDictionary<Vector2I, ChunkPlane> _positionToChunk = new();
    private readonly List<ChunkPlane> _chunks = [];
    private static readonly PackedScene _chunk_scene = GD.Load<PackedScene>("res://terrain_generator/infinite_heightmap_terrain/chunk_plane.tscn");

    private Vector3 _playerPosition;
	private readonly object _playerPositionLock = new();

    public static ChunkManager Instance {get; private set;}

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        if (!Enabled) return;

        Instance = this;

		lock (_playerPositionLock)
		{
			_playerPosition = Player.Instance != null ? Player.Instance.GlobalPosition : Vector3.Zero;
		}
        var playerchunk = new Vector2I(
            Mathf.FloorToInt(_playerPosition.X / ChunkSize),
            Mathf.FloorToInt(_playerPosition.Z / ChunkSize)
        );

		var half_rd = Mathf.FloorToInt(RenderDistance / 2f);
		for (int x = 0; x < RenderDistance; x++)
		{
			for (int z=0; z < RenderDistance; z++)
            {
                var chunk = _chunk_scene.Instantiate<ChunkPlane>();
                CallDeferred(Node.MethodName.AddChild, chunk);
                _chunks.Add(chunk);
                var pos = new Vector2I(playerchunk.X  + x - half_rd, playerchunk.Y + z - half_rd);
                chunk.CallDeferred(nameof(ChunkPlane.SetChunkPosition),pos);
			}
		}

        new Thread(new ThreadStart(ThreadProcess)){IsBackground = true, Priority = ThreadPriority.BelowNormal}.Start();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player.Instance != null)
        {
            lock (_playerPositionLock)
            {
                _playerPosition = Player.Instance.GlobalPosition;
            }
        }
    }

    public void UpdateAllChunks()
    {
        foreach (var chunk in _chunks)
        {
            chunk.UpdateMeshAndCollisionShape();
        }
    }

	public static void UpdateChunkPosition(ChunkPlane chunk, Vector2I currentPosition, Vector2I previousPosition)
	{
		if (Instance._positionToChunk.TryGetValue(previousPosition, out var chunkAtPosition) && chunkAtPosition == chunk)
		{
			Instance._positionToChunk.TryRemove(previousPosition, out _);
		}

		Instance._chunkToPosition[chunk] = currentPosition;
		Instance._positionToChunk[currentPosition] = chunk;
	}

    public static float GetNoiseHeight(Vector2 point)
    {
        return Instance.MaxHeight*(Instance.NoiseTexture.GetNoise2D(point.X,point.Y)+1)/2;
    }

    public void ThreadProcess()
    {
        while (IsInstanceValid(this))
        {
            int playerChunkX, playerChunkZ;
            lock(_playerPositionLock)
            {
                playerChunkX = Mathf.FloorToInt(_playerPosition.X / ChunkSize);
                playerChunkZ = Mathf.FloorToInt(_playerPosition.Z / ChunkSize);
            }

            foreach (var chunk in _chunks)
            {
                if (!_chunkToPosition.TryGetValue(chunk, out var chunkPosition)) continue;

                var chunkX = chunkPosition.X;
                var chunkZ = chunkPosition.Y;

                var newChunkX = Mathf.PosMod(chunkX - playerChunkX + RenderDistance / 2, RenderDistance) + playerChunkX - RenderDistance / 2;
                var newChunkZ = Mathf.PosMod(chunkZ - playerChunkZ + RenderDistance / 2, RenderDistance) + playerChunkZ - RenderDistance / 2;

                if (newChunkX != chunkX || newChunkZ != chunkZ)
                {
                    if (_positionToChunk.ContainsKey(chunkPosition))
                    {
                        _positionToChunk.TryRemove(chunkPosition, out _);
                    }

                    var newPosition = new Vector2I(newChunkX, newChunkZ);

                    _chunkToPosition[chunk] = newPosition;
                    _positionToChunk[newPosition] = chunk;

                    chunk.CallDeferred(nameof(ChunkPlane.SetChunkPosition), newPosition);
                }
            }
            Thread.Sleep(100);
        }
    }
}
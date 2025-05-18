using Godot;
using System;
using System.Collections.Generic;

public partial class AudioStreamQueue(int channels = 8, AudioBus bus = AudioBus.Master) : Node3D
{
    private int _num_channels = channels;
    private AudioBus _bus = bus;

    private Queue<AudioStreamPlayer3D> _available = new();
    private readonly Queue<(AudioStream,Vector3, float)> _queued_sounds = new();

    public override void _Ready()
    {
        // Create the pool of AudioStreamPlayer nodes.
        for (int i = 0; i < _num_channels; i++)
        {
            var player = new AudioStreamPlayer3D();
            AddChild(player);
            player.Bus = _bus.ToString();
            _available.Enqueue(player);
            player.Finished += () =>
            {
                EnqueuePlayer(player);
                string key_to_remove = null;
                foreach (var (str, tracked) in AudioManager.TrackedPlayers)
                {
                    if (tracked.Equals(player))
                    {
                        key_to_remove = str;
                        break;
                    }
                }
                if (key_to_remove != null)
                {
                    AudioManager.TrackedPlayers.Remove(key_to_remove);
                }
            };
        }
    }
    
    public void EnqueuePlayer(AudioStreamPlayer3D player)
    {
        if (player == null) return;
        if (_available.Contains(player)) return;
        player.Bus = _bus.ToString();
        _available.Enqueue(player);
    }

    public void QueueSound(AudioStream stream, Vector3? position = null, float volumedb = 0.0f)
    {
        if (Player.Instance == null && position == null) return;
        var pos = position ?? Player.Instance.GlobalPosition;
        _queued_sounds.Enqueue((stream, pos, volumedb));
    }

    public AudioStreamPlayer3D PlaySoundIfAvailable(AudioStream stream, Vector3? position = null, float volumedb = 0.0f, float pitch_scale = 1.0f)
    {
        if (Player.Instance == null && position == null) return null;
        if (_available.Count > 0)
        {
            var audio_player = _available.Dequeue();
            audio_player.VolumeDb = volumedb;
            audio_player.PitchScale = pitch_scale;
            audio_player.GlobalPosition = Player.GetCameraPosition() + 0.1f * ((position ?? Vector3.Zero) - Player.GetCameraPosition());// (position ?? Vector3.Zero) + Player.GetCameraPosition();

            audio_player.Stream = stream;
            audio_player.Play();
            return audio_player;
            // GD.Print("playing sound on queue: ", this, "available channels: ", _available.Count);
            // GD.Print("stream: ", stream, "position: ", audio_player.Position, "volume: ", volumedb);
        }
        return null;
        // else GD.Print("no available channels to play sound on queue: ", this, "available channels: ", _available.Count);
    }

    public override void _Process(double delta)
    {
        // Play a queued sound if any players are _available.
        if (_queued_sounds.Count > 0 && _available.Count > 0)
        {
            var player = _available.Dequeue();
            var (stream, pos, volumedb) = _queued_sounds.Dequeue();
            player.Stream = stream;
            player.GlobalPosition = pos;
            player.VolumeDb = volumedb;
            player.Play();
        }
    }
}
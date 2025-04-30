using Godot;
using System;
using System.Collections.Generic;

// consider looking into the "bus" feature of audio streams if you encounter difficulties with audio playback
// https://docs.godotengine.org/en/stable/tutorials/audio/audio_buses.html

// enum string names must match bus names in godot Audio tab 
public enum AudioBus
{
    Coins,
    Enemies,
    Misc,
    Footsteps,
    Master
}

public partial  class AudioManager : Node
{
    public static readonly Dictionary<AudioBus, AudioStreamQueue> keyValuePairs = new()
    {
        {AudioBus.Coins, new(8, AudioBus.Coins)},
        {AudioBus.Misc, new(8, AudioBus.Misc)},
        {AudioBus.Enemies, new(16, AudioBus.Enemies)},
        {AudioBus.Footsteps, new(16, AudioBus.Footsteps)},
        {AudioBus.Master, new(64, AudioBus.Master)}
    };
    public static readonly Vector3 FootstepWaitTimes = new(0.35f,0.25f,0.5f); // walk speed, sprint speed, crouch speed (time is delay between footsteps in seconds)
    public static readonly Dictionary<string,List<AudioStream>> FootstepSounds = new()
    {
        {
            "default",
            new()
            {
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 1.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 2.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 3.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 4.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 5.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 6.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 7.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 8.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 9.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 10.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 11.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 12.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 13.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 14.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 15.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 16.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 17.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 18.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 19.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 20.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 21.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 22.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/default/Audio 23.ogg")
            }
        },
        {
            "grass",
            new()
            {
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 1.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 2.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 3.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 4.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 5.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 6.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 7.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 8.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/Audio 9.ogg")
	        }
        },
	    {
            "grass_2",
            new()
            {
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-01.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-02.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-03.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-04.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-05.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-06.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-07.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-08.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-09.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-11.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-12.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/grass/grass_step_2-13.ogg"),
	        }
        },
	    {
            "ice",
            new()
            {
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/ice/Audio 1.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/ice/Audio 2.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/ice/Audio 3.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/ice/Audio 4.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/ice/Audio 5.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/ice/Audio 6.ogg")
	        }
        },
        {
            "stone",
            new()
            {
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 1.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 2.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 3.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 4.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 5.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 6.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 7.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 8.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 9.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 10.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 11.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 12.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 13.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 14.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/stone/Audio 15.ogg")
            }
        },
        {
            "wood",
            new()
            {
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/wood/Audio 1.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/wood/Audio 2.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/wood/Audio 3.ogg"),
                ResourceLoader.Load<AudioStream>("res://audio/footsteps/wood/Audio 4.ogg")
            }
        }
    };

    public static void TryPlayAtPlayer(AudioStream stream)
    {
        if (Player.Instance != null) keyValuePairs[AudioBus.Master].PlaySoundIfAvailable(stream, Player.Instance.GlobalPosition);
    }

    public static void TryPlay(AudioStream stream, AudioBus key = AudioBus.Master, Vector3? position = null, float volumedb = 0.0f)
    {
        if (keyValuePairs.TryGetValue(key, out AudioStreamQueue value))
        {
            value.PlaySoundIfAvailable(stream, position, volumedb);
        }
    }

    public static void QueueSound(AudioStream stream, AudioBus key = AudioBus.Master, Vector3? position = null, float volumedb = 0.0f)
    {
        if (keyValuePairs.TryGetValue(key, out AudioStreamQueue value))
        {
            value.QueueSound(stream, position, volumedb);
        }
    }

    public static (AudioStream,float) GetFootstepSoundAndVolFromBlockPosition(Vector3 global_pos)
    {
        throw new NotImplementedException();
        // var chunkpos = ChunkManager.GlobalPositionToChunkPosition(global_pos);

        // if (!ChunkManager.Instance.BLOCKCACHE.TryGetValue(chunkpos, out var chunk)) return (null, 0.0f);
        
        // var _footstep_sound = FootstepSounds["default"][Random.Shared.Next(0, FootstepSounds["default"].Count)];
        // var blockpos = ChunkManager.GlobalPositionToPaddedBlockPosition(global_pos);
        // var idx = ChunkManager.BlockIndex(blockpos);
        // var block_id = ChunkManager.GetBlockID(chunk[idx]);
        // var volume_db = 0.0f;

        // if (ChunkManager.GetBlockSpecies(block_id) == BlockSpecies.Grass || ChunkManager.GetBlockSpecies(block_id) == BlockSpecies.Leaves)
        // {
        //     volume_db = -10.0f;
        //     if (Random.Shared.NextSingle() < 0.5f)
        //         _footstep_sound = FootstepSounds["grass"][Random.Shared.Next(0, FootstepSounds["grass"].Count)];
        //     else _footstep_sound = FootstepSounds["grass_2"][Random.Shared.Next(0, FootstepSounds["grass_2"].Count)];
        // }
        // if (block_id == BlockManager.BlockID("Stone"))
        // {
        //     _footstep_sound = FootstepSounds["stone"][Random.Shared.Next(0, FootstepSounds["stone"].Count)];
        // }
        // if (block_id == BlockManager.BlockID("Trunk"))
        // {
        //     _footstep_sound = FootstepSounds["wood"][Random.Shared.Next(0, FootstepSounds["wood"].Count)];
        // }
        // return (_footstep_sound,volume_db);   
    }
}
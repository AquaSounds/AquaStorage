using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Flac;
using NAudio.Vorbis;

namespace AquaStorage.Helpers;

public static class AudioFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".ogg", ".flac"
    };

    public static bool IsAudioFile(string path)
    {
        var ext = Path.GetExtension(path);
        return Extensions.Contains(ext);
    }

    public static WaveStream CreateReader(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".ogg" => new VorbisWaveReader(filePath),
            ".flac" => new FlacReader(filePath),
            _ => new AudioFileReader(filePath), // .wav, .mp3
        };
    }
}

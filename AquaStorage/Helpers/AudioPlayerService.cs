using System;
using System.Threading;
using NAudio.Wave;

namespace AquaStorage.Helpers;

public sealed class AudioPlayerService : IDisposable
{
    private readonly object _lock = new();
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioFile;

    public string? CurrentPath { get; private set; }
    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;

    private const int MaxRetries = 3;
    private const int RetryDelayMs = 20;

    /// <summary>
    /// Stop the current playback (if any) and immediately play the given file.
    /// No fade-out — preserves the new file's transient attack.
    /// If the same file is already playing, does nothing.
    /// Retries on device-busy when switching rapidly between files.
    /// </summary>
    public void Play(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        lock (_lock)
        {
            if (IsPlaying && string.Equals(CurrentPath, filePath, StringComparison.OrdinalIgnoreCase))
                return;

            Cleanup();
            Thread.Sleep(50);

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    _audioFile = new AudioFileReader(filePath);
                    _waveOut = new WaveOutEvent
                    {
                        DesiredLatency = 30,
                        NumberOfBuffers = 1024,
                        Volume = 0.2f
                    };
                    _waveOut.Init(_audioFile);
                    _waveOut.Play();
                    CurrentPath = filePath;
                    return;
                }
                catch
                {
                    Cleanup();
                    if (attempt < MaxRetries)
                        Thread.Sleep(RetryDelayMs);
                    else
                        throw;
                }
            }
        }
    }

    /// <summary>
    /// Stop playback and release all resources.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            Cleanup();
        }
    }

    private void Cleanup()
    {
        try { _waveOut?.Stop(); } catch { }
        try { _waveOut?.Dispose(); } catch { }
        try { _audioFile?.Dispose(); } catch { }
        _waveOut = null;
        _audioFile = null;
        CurrentPath = null;
    }

    public void Dispose()
    {
        Stop();
    }
}

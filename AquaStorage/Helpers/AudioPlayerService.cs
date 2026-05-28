using System;
using System.Diagnostics;
using System.Threading;
using NAudio.Wave;

namespace AquaStorage.Helpers;

public sealed class AudioPlayerService : IDisposable
{
    private readonly object _lock = new();
    private WaveOutEvent? _waveOut;
    private WaveStream? _audioFile;

    private double _seekTarget;
    private readonly Stopwatch _seekTimer = new();
    private bool _seekPending;

    private double _volumeDb = -6;
    public double VolumeDb
    {
        get => _volumeDb;
        set
        {
            _volumeDb = Math.Clamp(value, -60, 0);
            lock (_lock)
            {
                if (_waveOut != null)
                    _waveOut.Volume = DbToLinear(_volumeDb);
            }
        }
    }

    private static float DbToLinear(double db) => (float)Math.Pow(10, db / 20.0);

    public string? CurrentPath { get; private set; }
    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
    public bool HasFile => _audioFile != null;

    public double TotalTime
    {
        get { lock (_lock) { return _audioFile?.TotalTime.TotalSeconds ?? 0; } }
    }

    public double CurrentTime
    {
        get
        {
            lock (_lock)
            {
                if (_audioFile == null) return 0;

                if (_seekPending)
                {
                    double elapsed = IsPlaying ? _seekTimer.Elapsed.TotalSeconds : 0;
                    double manual = _seekTarget + elapsed;

                    // Switch back to device once it catches up
                    if (_waveOut != null && IsPlaying)
                    {
                        var fmt = _waveOut.OutputWaveFormat;
                        double device = (double)_waveOut.GetPosition() / fmt.AverageBytesPerSecond;
                        if (Math.Abs(device - manual) < 0.1)
                        {
                            _seekPending = false;
                            return device;
                        }
                    }
                    return manual;
                }

                if (_waveOut == null) return 0;
                var f = _waveOut.OutputWaveFormat;
                return (double)_waveOut.GetPosition() / f.AverageBytesPerSecond;
            }
        }
    }

    public double PlaybackPosition
    {
        get
        {
            lock (_lock)
            {
                if (_audioFile == null) return 0;
                double total = _audioFile.TotalTime.TotalSeconds;
                return total > 0 ? Math.Clamp(CurrentTime / total, 0, 1) : 0;
            }
        }
    }

    public void Seek(double fraction)
    {
        lock (_lock)
        {
            if (_audioFile == null || _waveOut == null) return;
            bool wasPlaying = _waveOut.PlaybackState == PlaybackState.Playing;
            if (wasPlaying) _waveOut.Stop();
            double target = fraction * _audioFile.TotalTime.TotalSeconds;
            _audioFile.CurrentTime = TimeSpan.FromSeconds(target);
            _seekTarget = target;
            _seekPending = true;
            _seekTimer.Restart();
            if (wasPlaying) _waveOut.Play();
        }
    }

    private const int MaxRetries = 3;
    private const int RetryDelayMs = 20;

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
                    _audioFile = AudioFormats.CreateReader(filePath);
                    _waveOut = new WaveOutEvent
                    {
                        DesiredLatency = 30,
                        NumberOfBuffers = 1024,
                        Volume = DbToLinear(_volumeDb)
                    };
                    _waveOut.Init(_audioFile);
                    _waveOut.Play();
                    CurrentPath = filePath;

                    _seekTarget = 0;
                    _seekPending = true;
                    _seekTimer.Restart();
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

    public void Stop()
    {
        lock (_lock) { Cleanup(); }
    }

    private void Cleanup()
    {
        try { _waveOut?.Stop(); } catch { }
        try { _waveOut?.Dispose(); } catch { }
        try { _audioFile?.Dispose(); } catch { }
        _waveOut = null;
        _audioFile = null;
        CurrentPath = null;
        _seekPending = false;
        _seekTarget = 0;
        _seekTimer.Reset();
    }

    public void Dispose() { Stop(); }
}

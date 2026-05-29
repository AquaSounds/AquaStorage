using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AquaStorage.Helpers;
using AquaStorage.Services;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AquaStorage.Views;

public partial class WaveForm : UserControl
{
    private float[]? _leftChannel;
    private float[]? _rightChannel;

    private AudioPlayerService? _player;
    private DispatcherTimer? _positionTimer;
    private double _playbackPosition;
    private bool _isDragging;
    private bool _isHoveringNear;

    // Cached rendering resources
    private StreamGeometry? _leftGeometry;
    private StreamGeometry? _rightGeometry;
    private Pen? _waveformPen;
    private double _cachedW = -1;
    private double _cachedH = -1;

    private const double LineHitZone = 20.0;

    public AudioPlayerService? Player
    {
        get => _player;
        set
        {
            _player = value;
            if (value != null && _leftChannel != null)
                StartPositionTimer();
            else
                StopPositionTimer();
        }
    }

    public WaveForm()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        ClipToBounds = true;
        App.AccentColorChanged += _ => { InvalidateGeometry(); InvalidateVisual(); };
        App.ThemeChanged += () => InvalidateVisual();
    }

    public async void RenderWaveform(string filePath)
    {
        try
        {
            // Try loading from cache first
            var cached = await Task.Run(() => CacheHelper.LoadWaveform(filePath));
            if (cached is { } data)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _leftChannel = data.left;
                    _rightChannel = data.right;
                    _playbackPosition = 0;
                    InvalidateGeometry();
                    InvalidateVisual();
                    if (_player != null) StartPositionTimer();
                });
                return;
            }

            // Cache miss — render and save
            await Task.Run(async () =>
            {
                using var reader = AudioFormats.CreateReader(filePath);
                await LoadProgressive(reader);
            });

            // Save waveform to cache after rendering
            if (_leftChannel != null && _rightChannel != null)
                await Task.Run(() => CacheHelper.SaveWaveform(filePath, _leftChannel, _rightChannel));
        }
        catch
        {
            _leftChannel = null;
            _rightChannel = null;
        }
    }

    public void Clear()
    {
        _leftChannel = null;
        _rightChannel = null;
        _playbackPosition = 0;
        StopPositionTimer();
        InvalidateGeometry();
        InvalidateVisual();
    }

    // ── Progressive loading ──────────────────────────────────────────

    private async Task LoadProgressive(WaveStream reader)
    {
        const int targetPoints = 1200;
        int channels = reader.WaveFormat.Channels;

        long totalFrames = 0;
        try { if (reader.Length > 0) totalFrames = reader.Length / reader.WaveFormat.BlockAlign; } catch { }

        // Short / unknown-length: load in one shot (fast enough)
        if (totalFrames <= 0 || totalFrames <= targetPoints)
        {
            var (left, right) = totalFrames > 0
                ? (reader is ISampleProvider sp
                    ? LoadAllSamples(sp, channels)
                    : LoadAllSamplesBytes(reader, reader.WaveFormat, channels,
                        reader.WaveFormat.BlockAlign, reader.WaveFormat.BlockAlign / channels))
                : LoadUnknown(reader, channels, targetPoints);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _leftChannel = left;
                _rightChannel = right;
                _playbackPosition = 0;
                InvalidateGeometry();
                InvalidateVisual();
                if (_player != null) StartPositionTimer();
            });
            return;
        }

        // Pre-allocate arrays and show immediately (all zeros → flat line)
        float[] leftArr = new float[targetPoints];
        float[] rightArr = new float[targetPoints];

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _leftChannel = leftArr;
            _rightChannel = rightArr;
            _playbackPosition = 0;
            InvalidateGeometry();
            InvalidateVisual();
        });

        // Process on thread pool, report progress to UI
        if (reader is ISampleProvider sampleProvider)
            ProcessSamples(sampleProvider, channels, totalFrames, targetPoints, leftArr, rightArr);
        else
            ProcessBytes(reader, channels, totalFrames, targetPoints, leftArr, rightArr);

        // Final update
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            InvalidateGeometry();
            InvalidateVisual();
            if (_player != null) StartPositionTimer();
        });
    }

    private void ProcessSamples(ISampleProvider provider, int channels,
        long totalFrames, int targetPoints, float[] left, float[] right)
    {
        float step = (float)totalFrames / targetPoints;
        float[] buf = new float[8192];
        int frameIdx = 0;
        int framesPerUpdate = Math.Max(1, (int)(totalFrames / 100));
        int framesSinceUpdate = 0;
        int read;

        while ((read = provider.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < read; i += channels, frameIdx++)
            {
                int bin = (int)(frameIdx / step);
                if (bin >= targetPoints) return;

                left[bin] = buf[i] * 15;
                right[bin] = (channels >= 2 ? buf[i + 1] : buf[i]) * 15;
            }

            framesSinceUpdate += read / channels;
            if (framesSinceUpdate >= framesPerUpdate)
            {
                framesSinceUpdate = 0;
                Dispatcher.UIThread.Post(() => { InvalidateGeometry(); InvalidateVisual(); });
            }
        }
    }

    private void ProcessBytes(WaveStream reader, int channels,
        long totalFrames, int targetPoints, float[] left, float[] right)
    {
        var format = reader.WaveFormat;
        int blockAlign = format.BlockAlign;
        int bytesPerSample = blockAlign / channels;
        float step = (float)totalFrames / targetPoints;
        byte[] buf = new byte[65536 * blockAlign];
        int frameIdx = 0;
        int framesPerUpdate = Math.Max(1, (int)(totalFrames / 100));
        int framesSinceUpdate = 0;
        int read;

        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            int framesInBuf = read / blockAlign;
            for (int f = 0; f < framesInBuf; f++, frameIdx++)
            {
                int bin = (int)(frameIdx / step);
                if (bin >= targetPoints) return;

                int off = f * blockAlign;
                left[bin] = ReadSample(buf, off, format) * 15;
                right[bin] = channels >= 2
                    ? ReadSample(buf, off + bytesPerSample, format) * 15
                    : left[bin];
            }

            framesSinceUpdate += framesInBuf;
            if (framesSinceUpdate >= framesPerUpdate)
            {
                framesSinceUpdate = 0;
                Dispatcher.UIThread.Post(() => { InvalidateGeometry(); InvalidateVisual(); });
            }
        }
    }

    private static (float[], float[]) LoadUnknown(WaveStream reader, int channels, int targetPoints)
    {
        if (reader is ISampleProvider sp)
        {
            var (lBins, rBins) = AccumulateBins(sp, channels, 8192);
            return (DownsampleBins(lBins, targetPoints), DownsampleBins(rBins, targetPoints));
        }
        else
        {
            var format = reader.WaveFormat;
            int blockAlign = format.BlockAlign;
            var (lBins, rBins) = AccumulateBinsBytes(reader, format, channels, blockAlign,
                blockAlign / channels, 8192);
            return (DownsampleBins(lBins, targetPoints), DownsampleBins(rBins, targetPoints));
        }
    }

    // ── Short-file fallbacks (read all, no downsampling) ─────────────

    private static (float[], float[]) LoadAllSamples(ISampleProvider provider, int channels)
    {
        var samples = new List<float>();
        float[] buf = new float[8192];
        int read;
        while ((read = provider.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                samples.Add(buf[i]);
        }
        int frames = samples.Count / channels;
        var l = new float[frames];
        var r = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int idx = f * channels;
            l[f] = samples[idx] * 15;
            r[f] = channels >= 2 ? samples[idx + 1] * 15 : l[f];
        }
        return (l, r);
    }

    private static (float[], float[]) LoadAllSamplesBytes(
        WaveStream reader, WaveFormat format, int channels, int blockAlign, int bytesPerSample)
    {
        using var ms = new System.IO.MemoryStream();
        byte[] buf = new byte[65536];
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            ms.Write(buf, 0, read);

        var allBytes = ms.GetBuffer();
        int totalBytes = (int)ms.Length;
        int totalFrames = totalBytes / blockAlign;
        var l = new float[totalFrames];
        var r = new float[totalFrames];
        for (int f = 0; f < totalFrames; f++)
        {
            int off = f * blockAlign;
            l[f] = ReadSample(allBytes, off, format) * 15;
            r[f] = channels >= 2
                ? ReadSample(allBytes, off + bytesPerSample, format) * 15
                : l[f];
        }
        return (l, r);
    }

    // ── Unknown-length streaming helpers ─────────────────────────────

    private static (List<float> left, List<float> right) AccumulateBins(
        ISampleProvider provider, int channels, int samplesPerBin)
    {
        var left = new List<float>();
        var right = new List<float>();
        float[] buf = new float[8192];
        float lLast = 0, rLast = 0;
        int pos = 0;
        int read;
        while ((read = provider.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < read; i += channels, pos++)
            {
                if (pos >= samplesPerBin)
                {
                    left.Add(lLast);
                    right.Add(rLast);
                    pos = 0;
                }
                lLast = buf[i] * 15;
                rLast = (channels >= 2 ? buf[i + 1] : buf[i]) * 15;
            }
        }
        if (pos > 0)
        {
            left.Add(lLast);
            right.Add(rLast);
        }
        return (left, right);
    }

    private static (List<float> left, List<float> right) AccumulateBinsBytes(
        WaveStream reader, WaveFormat format, int channels, int blockAlign,
        int bytesPerSample, int samplesPerBin)
    {
        var left = new List<float>();
        var right = new List<float>();
        byte[] buf = new byte[65536 * blockAlign];
        float lLast = 0, rLast = 0;
        int pos = 0;
        int read;
        while ((read = reader.Read(buf, 0, buf.Length)) > 0)
        {
            int framesInBuf = read / blockAlign;
            for (int f = 0; f < framesInBuf; f++, pos++)
            {
                if (pos >= samplesPerBin)
                {
                    left.Add(lLast);
                    right.Add(rLast);
                    pos = 0;
                }
                int off = f * blockAlign;
                lLast = ReadSample(buf, off, format) * 15;
                rLast = channels >= 2
                    ? ReadSample(buf, off + bytesPerSample, format) * 15
                    : lLast;
            }
        }
        if (pos > 0)
        {
            left.Add(lLast);
            right.Add(rLast);
        }
        return (left, right);
    }

    private static float[] DownsampleBins(List<float> bins, int target)
    {
        if (bins.Count <= target)
            return bins.ToArray();

        float[] result = new float[target];
        float step = (float)bins.Count / target;
        for (int i = 0; i < target; i++)
            result[i] = bins[(int)(i * step)];
        return result;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => (buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16)) / 8388608f,
            32 when format.Encoding == WaveFormatEncoding.IeeeFloat => BitConverter.ToSingle(buffer, offset),
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => BitConverter.ToInt16(buffer, offset) / 32768f,
        };
    }

    // ── Render ───────────────────────────────────────────────────────

    private void InvalidateGeometry()
    {
        _leftGeometry = null;
        _rightGeometry = null;
        _cachedW = -1;
        _cachedH = -1;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (_leftChannel == null || _rightChannel == null)
            return;

        double w = Bounds.Width;
        double h = Bounds.Height;
        double halfH = h / 2;
        double ampScale = halfH * 0.03;

        // Rebuild geometry cache on size change or data update
        if (_leftGeometry == null || _rightGeometry == null
            || Math.Abs(_cachedW - w) > 0.5 || Math.Abs(_cachedH - h) > 0.5)
        {
            _cachedW = w;
            _cachedH = h;
            double stepX = w / _leftChannel.Length;
            _leftGeometry = BuildGeometry(_leftChannel, stepX, halfH / 2, ampScale);
            _rightGeometry = BuildGeometry(_rightChannel, stepX, halfH + halfH / 2, ampScale);

            var accentColor = Application.Current?.FindResource("AccentPrimary") is Color c ? c : Colors.DodgerBlue;
            _waveformPen = new Pen(new SolidColorBrush(accentColor), 1.0);
        }

        context.DrawGeometry(null, _waveformPen, _leftGeometry);
        context.DrawGeometry(null, _waveformPen, _rightGeometry);

        // Playback position line
        if (_player is { HasFile: true })
        {
            double x = _playbackPosition * w;
            double opacity = (_isHoveringNear || _isDragging) ? 1.0 : 0.5;
            bool isLight = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
            var lineColor = isLight ? Colors.Black : Colors.White;
            var linePen = new Pen(new SolidColorBrush(lineColor, opacity), _isDragging ? 2.0 : 1.5);
            context.DrawLine(linePen, new Point(x, 0), new Point(x, h));
        }
    }

    private static StreamGeometry BuildGeometry(float[] data, double stepX, double midY, double ampScale)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, midY - data[0] * ampScale), false);
            for (int i = 1; i < data.Length; i++)
                ctx.LineTo(new Point(i * stepX, midY - data[i] * ampScale));
        }
        return geometry;
    }

    // ── Position timer & seeking ─────────────────────────────────────

    private void StartPositionTimer()
    {
        StopPositionTimer();
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _positionTimer.Tick += (_, _) =>
        {
            if (_isDragging || _player == null || _leftChannel == null) return;
            _playbackPosition = _player.PlaybackPosition;
            InvalidateVisual();
        };
        _positionTimer.Start();
    }

    private void StopPositionTimer()
    {
        _positionTimer?.Stop();
        _positionTimer = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_player == null || _leftChannel == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!_isHoveringNear) return;

        _isDragging = true;
        UpdateDragPosition(e.GetPosition(this).X);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;

        if (_isDragging)
        {
            UpdateDragPosition(x);
            return;
        }

        double lineX = _playbackPosition * Bounds.Width;
        bool wasHovering = _isHoveringNear;
        _isHoveringNear = Math.Abs(x - lineX) <= LineHitZone;
        if (_isHoveringNear != wasHovering)
            InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;
        _isDragging = false;

        double w = Bounds.Width;
        if (w > 0)
        {
            double fraction = Math.Clamp(e.GetPosition(this).X / w, 0, 1);
            _playbackPosition = fraction;
            _player?.Seek(fraction);
        }
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_isDragging && _isHoveringNear)
        {
            _isHoveringNear = false;
            InvalidateVisual();
        }
    }

    private void UpdateDragPosition(double x)
    {
        double w = Bounds.Width;
        if (w <= 0) return;
        _playbackPosition = Math.Clamp(x / w, 0, 1);
        InvalidateVisual();
    }
}

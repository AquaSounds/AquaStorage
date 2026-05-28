using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AquaStorage.Helpers;
using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace AquaStorage.Views;

public partial class WaveForm : UserControl
{
    private float[]? _leftChannel;
    private float[]? _rightChannel;

    private AudioPlayerService? _player;
    private DispatcherTimer? _positionTimer;
    private double _playbackPosition; // 0..1
    private bool _isDragging;
    private bool _isHoveringNear;

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
    }

    public void RenderWaveform(string filePath)
    {
        try
        {
            using var reader = AudioFormats.CreateReader(filePath);
            var (left, right) = LoadStereoData(reader);
            _leftChannel = left;
            _rightChannel = right;
            _playbackPosition = 0;
            InvalidateVisual();
            if (_player != null) StartPositionTimer();
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
        InvalidateVisual();
    }

    private (float[] left, float[] right) LoadStereoData(WaveStream reader)
    {
        int channels = reader.WaveFormat.Channels;
        var leftSamples = new List<float>();
        var rightSamples = new List<float>();

        if (reader is ISampleProvider sampleProvider)
        {
            float[] buffer = new float[4096];
            int read;
            while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i += channels)
                {
                    leftSamples.Add(buffer[i] * 15);
                    rightSamples.Add((channels >= 2 ? buffer[i + 1] : buffer[i]) * 15);
                }
            }
        }
        else
        {
            var format = reader.WaveFormat;
            int blockAlign = format.BlockAlign;
            byte[] byteBuffer = new byte[4096 * blockAlign];
            int read;
            while ((read = reader.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
            {
                int sampleCount = read / blockAlign;
                for (int i = 0; i < sampleCount; i++)
                {
                    float left = ReadSample(byteBuffer, i * blockAlign, format) * 15;
                    leftSamples.Add(left);
                    float right = channels >= 2
                        ? ReadSample(byteBuffer, i * blockAlign + (blockAlign / channels), format) * 15
                        : left;
                    rightSamples.Add(right);
                }
            }
        }

        const int targetPoints = 1200;
        return (Downsample(leftSamples, targetPoints), Downsample(rightSamples, targetPoints));
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

    private float[] Downsample(List<float> input, int targetPoints)
    {
        if (input.Count <= targetPoints)
            return input.ToArray();

        float[] output = new float[targetPoints];
        float step = input.Count / (float)targetPoints;
        for (int i = 0; i < targetPoints; i++)
            output[i] = input[(int)(i * step)];
        return output;
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
        double stepX = w / _leftChannel.Length;
        double ampScale = halfH * 0.03;

        var accentColor = Application.Current?.FindResource("AccentPrimary") is Color c ? c : Colors.DodgerBlue;
        var pen = new Pen(new SolidColorBrush(accentColor), 1.0);

        DrawChannel(context, _leftChannel, stepX, halfH / 2, ampScale, pen);
        DrawChannel(context, _rightChannel, stepX, halfH + halfH / 2, ampScale, pen);

        // Playback position line
        if (_player is { HasFile: true })
        {
            double x = _playbackPosition * w;
            double opacity = (_isHoveringNear || _isDragging) ? 1.0 : 0.5;
            var lineBrush = new SolidColorBrush(Colors.White, opacity);
            var linePen = new Pen(lineBrush, _isDragging ? 2.0 : 1.5);
            context.DrawLine(linePen, new Point(x, 0), new Point(x, h));
        }
    }

    private void DrawChannel(DrawingContext ctx, float[] data, double stepX, double midY, double ampScale, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var geoCtx = geometry.Open())
        {
            bool first = true;
            for (int i = 0; i < data.Length; i++)
            {
                var pt = new Point(i * stepX, midY - data[i] * ampScale);
                if (first)
                {
                    geoCtx.BeginFigure(pt, false);
                    first = false;
                }
                else
                {
                    geoCtx.LineTo(pt);
                }
            }
        }
        ctx.DrawGeometry(null, pen, geometry);
    }

    #region Position timer & seeking

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

        // Hover detection
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

    #endregion
}

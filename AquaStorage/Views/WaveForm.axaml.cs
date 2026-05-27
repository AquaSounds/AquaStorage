using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NAudio.Wave;
using System.Collections.Generic;

namespace AquaStorage.Views;

public partial class WaveForm : UserControl
{
    private float[]? _leftChannel;
    private float[]? _rightChannel;

    public WaveForm()
    {
        InitializeComponent();
        IsHitTestVisible = true;
    }

    public void RenderWaveform(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            var (left, right) = LoadStereoData(reader);
            _leftChannel = left;
            _rightChannel = right;
            InvalidateVisual();
        }
        catch
        {
            _leftChannel = null;
            _rightChannel = null;
        }
    }

    private (float[] left, float[] right) LoadStereoData(AudioFileReader reader)
    {
        int channels = reader.WaveFormat.Channels;
        var leftSamples = new List<float>();
        var rightSamples = new List<float>();
        float[] buffer = new float[4096];
        int read;

        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i += channels)
            {
                float left = buffer[i] * 15;
                leftSamples.Add(left);
                float right = channels >= 2 ? buffer[i + 1] * 15 : left;
                rightSamples.Add(right);
            }
        }

        const int targetPoints = 1200;
        return (Downsample(leftSamples, targetPoints), Downsample(rightSamples, targetPoints));
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

    public void Clear()
    {
        _leftChannel = null;
        _rightChannel = null;
        InvalidateVisual();
    }
}

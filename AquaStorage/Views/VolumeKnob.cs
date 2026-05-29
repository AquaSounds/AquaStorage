using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace AquaStorage.Views;

public class VolumeKnob : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<VolumeKnob, double>(nameof(Value), -6);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<VolumeKnob, double>(nameof(Minimum), -60);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<VolumeKnob, double>(nameof(Maximum), 0);

    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<VolumeKnob, double>(nameof(Speed), 0.005);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<VolumeKnob, double>(nameof(DefaultValue), -6);

    public event Action<double>? ValueChanged;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    private bool _hover;
    private bool _pressed;
    private bool _skipNextMove;
    private Point _tempPress;

    private readonly Pen _stroke = new();

    private static bool IsLightTheme => Application.Current?.ActualThemeVariant == ThemeVariant.Light;

    public VolumeKnob()
    {
        ClipToBounds = false;
        IsHitTestVisible = true;
        App.AccentColorChanged += _ => InvalidateVisual();
        App.ThemeChanged += () => InvalidateVisual();
    }

    static VolumeKnob()
    {
        AffectsRender<VolumeKnob>(ValueProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double s = Math.Min(availableSize.Width, availableSize.Height - 14);
        if (double.IsNaN(s) || s <= 0) s = 40;
        return new Size(s, s + 14);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var accentColor = Application.Current?.FindResource("AccentPrimary") is Color c ? c : Colors.DodgerBlue;
        _stroke.Brush = new SolidColorBrush(accentColor);

        double vw = Bounds.Width;
        double radius = (Bounds.Height - 14) / 2;
        var center = new Point(vw / 2, Bounds.Height / 2);

        if (_hover || _pressed)
            _stroke.Thickness = 1.5;
        else
            _stroke.Thickness = 1;

        var bodyColor = IsLightTheme ? Color.Parse("#E0E0E0") : Color.Parse("#232323");
        var shadowColor = IsLightTheme ? Color.Parse("#30000000") : Color.Parse("#33000000");
        var dotEdgeBrush = IsLightTheme ? new SolidColorBrush(Colors.Black, 0.2) : new SolidColorBrush(Colors.White, 0.2);
        var textColor = IsLightTheme ? Color.Parse("#CC000000") : Color.FromArgb(160, 255, 255, 255);

        // Shadow
        context.DrawEllipse(new SolidColorBrush(shadowColor), null,
            center, radius * 1.2, radius * 1.2);
        // Body
        context.DrawEllipse(new SolidColorBrush(bodyColor), _stroke, center, radius, radius);

        // Border limits
        const double border = 0.16;
        double percent = (Value - Minimum) / (Maximum - Minimum);
        percent = border + percent * (1 - 2 * border);

        double defaultPercent = (DefaultValue - Minimum) / (Maximum - Minimum);
        defaultPercent = border + defaultPercent * (1 - 2 * border);

        if (_hover || _pressed)
        {
            // Default value dot
            context.DrawEllipse(dotEdgeBrush, null,
                new Point(center.X + radius * 1.5 * -double.Sin(defaultPercent * double.Pi * 2),
                    center.Y + radius * 1.5 * double.Cos(defaultPercent * double.Pi * 2)), 2, 2);
            // Min edge dot
            context.DrawEllipse(dotEdgeBrush, null,
                new Point(center.X + radius * 1.5 * -double.Sin(border * double.Pi * 2),
                    center.Y + radius * 1.5 * double.Cos(border * double.Pi * 2)), 2, 2);
            // Max edge dot
            context.DrawEllipse(dotEdgeBrush, null,
                new Point(center.X + radius * 1.5 * -double.Sin((1 - border) * double.Pi * 2),
                    center.Y + radius * 1.5 * double.Cos((1 - border) * double.Pi * 2)), 2, 2);
        }

        // Indicator dot
        context.DrawEllipse(new SolidColorBrush(bodyColor), new Pen(new SolidColorBrush(accentColor)),
            new Point(
                center.X + radius * -double.Sin(percent * double.Pi * 2),
                center.Y + radius * double.Cos(percent * double.Pi * 2)),
            2, 2);

        // dB label
        if (_hover || _pressed)
        {
            string label = Value >= 0 ? "0 dB" : $"{Value:F0} dB";
            var textBrush = new SolidColorBrush(textColor);
            var ft = new FormattedText(label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Normal),
                10, textBrush);
            context.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y + radius + 2));
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _pressed = true;
        Cursor = new Cursor(StandardCursorType.None);

        var center = new Point(Bounds.Width / 2, (Bounds.Height - 14) / 2);
        var screenPt = this.PointToScreen(center);
        SetCursorPos(screenPt.X, screenPt.Y);
        _tempPress = center;

        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            Value = DefaultValue;
            ValueChanged?.Invoke(Value);
            Cursor = Cursor.Default;
            _pressed = false;
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;
        _pressed = false;
        Cursor = Cursor.Default;

        var center = new Point(Bounds.Width / 2, (Bounds.Height - 14) / 2);
        var screenPt = this.PointToScreen(center);
        SetCursorPos(screenPt.X, screenPt.Y);

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pressed) return;

        if (_skipNextMove)
        {
            _skipNextMove = false;
            _tempPress = e.GetPosition(this);
            return;
        }

        Cursor = new Cursor(StandardCursorType.None);
        var pos = e.GetPosition(this);
        var deltaY = pos.Y - _tempPress.Y;

        Value -= deltaY * Speed * (Maximum - Minimum);
        Value = double.Clamp(Value, Minimum, Maximum);

        ValueChanged?.Invoke(Value);

        _skipNextMove = true;
        var center = new Point(Bounds.Width / 2, (Bounds.Height - 14) / 2);
        var screenPt = this.PointToScreen(center);
        SetCursorPos(screenPt.X, screenPt.Y);
        _tempPress = center;

        InvalidateVisual();
    }

    private static void SetCursorPos(int x, int y)
    {
        if (OperatingSystem.IsWindows())
            Native.SetCursorPos(x, y);
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);
    }
}

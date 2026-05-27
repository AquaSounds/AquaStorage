using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AquaStorage.Views;

public class AquaColorPicker : Control
{
    public static readonly StyledProperty<HslColor> ColorProperty =
        AvaloniaProperty.Register<AquaColorPicker, HslColor>(nameof(Color));

    public HslColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<AquaColorPicker, Color>(nameof(SelectedColor));

    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private const double ColorNum = 16;
    private const double GreyScaleNum = 3;

    private Point _currentMousePosition;

    private Point _positionEffect;
    private double _roundEffect;

    private double _selectedColorScale = 1;
    private bool _isSecondMenu;

    private bool _isPressed;

    private bool _isHueChanging;
    private bool _isSatChanging;
    private bool _isLightChanging;

    public AquaColorPicker()
    {
        ClipToBounds = false;
    }

    private double W => Bounds.Width;
    private double H => Bounds.Height;

    protected override Size MeasureOverride(Size availableSize)
    {
        double s = double.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(s)) s = 200;
        return new Size(s, s);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _currentMousePosition = e.GetPosition(this);

        _positionEffect = (e.GetPosition(this) - new Point(W / 2, H / 2)) / 20.0;
        _roundEffect = (e.GetPosition(this).X - W / 2.0) / W / 10.0;

        if (!_isSecondMenu)
        {
            for (int j = 0; j < GreyScaleNum; j++)
            {
                for (int i = 0; i < ColorNum; i++)
                {
                    var color = new HslColor(1.0, 360 / ColorNum * i, .8 / GreyScaleNum * j + .2, .4 * j / GreyScaleNum + .2).ToRgb();
                    var radius = W / 2 * (.7 / (GreyScaleNum) * j + .3);

                    var colorPosition = (radius - W * .001);
                    var d = .04;
                    if (_currentMousePosition.X > (new Point(
                                                       H / 2 + colorPosition *
                                                       -double.Sin((i + _roundEffect) / ColorNum * Double.Pi * 2),
                                                       H / 2 + colorPosition *
                                                       double.Cos((i + _roundEffect) / ColorNum * Double.Pi * 2)) +
                                                   _positionEffect).X - H * d
                        &&
                        _currentMousePosition.X < (new Point(
                                                       H / 2 + colorPosition *
                                                       -double.Sin((i + _roundEffect) / ColorNum * Double.Pi * 2),
                                                       H / 2 + colorPosition *
                                                       double.Cos((i + _roundEffect) / ColorNum * Double.Pi * 2)) +
                                                   _positionEffect).X + H * d
                        &&
                        _currentMousePosition.Y > (new Point(
                                                       H / 2 + colorPosition *
                                                       -double.Sin((i + _roundEffect) / ColorNum * Double.Pi * 2),
                                                       H / 2 + colorPosition *
                                                       double.Cos((i + _roundEffect) / ColorNum * Double.Pi * 2)) +
                                                   _positionEffect).Y - H * d
                        &&
                        _currentMousePosition.Y < (new Point(
                                                       H / 2 + colorPosition *
                                                       -double.Sin((i + _roundEffect) / ColorNum * Double.Pi * 2),
                                                       H / 2 + colorPosition *
                                                       double.Cos((i + _roundEffect) / ColorNum * Double.Pi * 2)) +
                                                   _positionEffect).Y + H * d
                       )
                    {
                        Color = color.ToHsl();
                    }
                }
            }
        }
        else
        {
            var currentPoint = e.GetPosition(this) - new Point(W / 2, H / 2);
            double currentPosition = Color.H;

            {
                if (currentPoint.X >= 0 && currentPoint.Y < 0)
                {
                    currentPosition = double.Atan((currentPoint.X) / (-currentPoint.Y)) / Double.Pi / 2.0 + 0.5;
                }

                if (currentPoint.X >= 0 && currentPoint.Y >= 0)
                {
                    currentPosition = double.Atan((currentPoint.X) / (-currentPoint.Y)) / Double.Pi / 2.0 + 1.0;
                }

                if (currentPoint.X < 0 && currentPoint.Y >= 0)
                {
                    currentPosition = double.Atan((currentPoint.X) / (-currentPoint.Y)) / Double.Pi / 2.0;
                }

                if (currentPoint.X < 0 && currentPoint.Y < 0)
                {
                    currentPosition = double.Atan((currentPoint.X) / (-currentPoint.Y)) / Double.Pi / 2.0 + 0.5;
                }
                if (Double.Pow(Double.Pow(currentPoint.X, 2) + Double.Pow(currentPoint.Y, 2), .5) > H * .3 && !_isSatChanging && !_isLightChanging)
                {
                    Color = new HslColor(1, currentPosition * 360, SelectedColor.ToHsl().S, SelectedColor.ToHsl().L);
                    if (_isPressed)
                    {
                        _isHueChanging = true;
                        SelectedColor = Color.ToRgb();
                    }
                }
                else
                {
                    //亮度
                    {
                        currentPosition = currentPosition < 0.75 ? currentPosition + 0.25 : currentPosition - 0.75;
                        if (currentPosition * 360 > 45 + 15 &&
                            currentPosition * 360 < 180 + 45 - 15 && !_isHueChanging && !_isSatChanging)
                        {
                            Color = new HslColor(1, SelectedColor.ToHsl().H, SelectedColor.ToHsl().S,
                                .6 * (currentPosition * 360 - (45 + 15)) / 150 + .2);

                            if (_isPressed)
                            {
                                _isLightChanging = true;
                                SelectedColor = Color.ToRgb();
                            }
                        }
                    }

                    //灰度
                    {
                        currentPosition = currentPosition < 0.5 ? currentPosition + 0.5 : currentPosition - 0.5;
                        if (currentPosition * 360 > 45 + 15 &&
                            currentPosition * 360 < 180 + 45 - 15 && !_isHueChanging && !_isLightChanging)
                        {
                            Color = new HslColor(1, SelectedColor.ToHsl().H,
                                ((currentPosition * 360) - (45 + 15)) / 150,
                                SelectedColor.ToHsl().L);

                            if (_isPressed)
                            {
                                _isSatChanging = true;
                                SelectedColor = Color.ToRgb();
                            }
                        }
                    }
                }
            }
        }

        if (Double.Pow(
                Double.Pow(
                    (e.GetPosition(this) - new Point(W / 2, H / 2)).X,
                    2) +
                double.Pow(
                    (e.GetPosition(this) - new Point(W / 2, H / 2)).Y,
                    2), .5) <
            H * .05
           )
        {
            _selectedColorScale = 1.05;
            Color = SelectedColor.ToHsl();
        }
        else
        {
            _selectedColorScale = 1;
        }

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_selectedColorScale > 1)
        {
            _isSecondMenu = !_isSecondMenu;
        }
        else
        {
            if (!_isSecondMenu)
            {
                SelectedColor = Color.ToRgb();
            }
            else
            {
                _isPressed = true;
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPressed = false;
        _isHueChanging = false;
        _isLightChanging = false;
        _isSatChanging = false;
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (W <= 0 || H <= 0) return;

        context.DrawEllipse(Brush.Parse("#232323"), null, new Point(W / 2, H / 2) + _positionEffect / 2.0, H * .5, H * .5);

        if (!_isSecondMenu)
        {
            for (int j = 0; j < GreyScaleNum; j++)
            {
                for (int i = 0; i < ColorNum; i++)
                {
                    var color = new HslColor(1.0, 360 / ColorNum * i, .8 / GreyScaleNum * j + .2, .4 * j / GreyScaleNum + .2).ToRgb();
                    var pen = new Pen
                    {
                        Brush = new SolidColorBrush(color),
                        Thickness = .5
                    };
                    double rotatePosition = i;

                    var radius = H / 2 * (.6 / (GreyScaleNum) * j + .4) +
                                 double.Abs((_positionEffect.X + _positionEffect.Y) / 10.0);
                    var singleRadius = radius / (GreyScaleNum) * (GreyScaleNum - j + 1) * .2 + j * .4;

                    if (color == Color.ToRgb())
                    {
                        pen.Thickness = 10.0;
                    }

                    context.DrawLine(pen,
                        new Point(
                            H / 2 + (radius - singleRadius) *
                            -double.Sin((rotatePosition + _roundEffect) / ColorNum * Double.Pi * 2),
                            H / 2 + (radius - singleRadius) *
                            double.Cos((rotatePosition + _roundEffect) / ColorNum * Double.Pi * 2)) + _positionEffect,
                        new Point(H / 2 + radius * -double.Sin(rotatePosition / ColorNum * Double.Pi * 2),
                            H / 2 + radius * double.Cos(rotatePosition / ColorNum * Double.Pi * 2)) + _positionEffect
                    );
                }
            }
        }
        else
        {
            //外部的圈，调整Hue
            {
                var hueSliderRadius = H * .4 +
                                      double.Abs((_positionEffect.X + _positionEffect.Y) / 10.0);
                var hueSliderSingleRadius = hueSliderRadius * .05;
                for (int i = 0; i < 360; i++)
                {
                    var color = new HslColor(1.0, i, 1, .5).ToRgb();
                    var pen = new Pen
                    {
                        Brush = new SolidColorBrush(color),
                        Thickness = H * .01
                    };

                    double rotatePosition = i;

                    context.DrawLine(pen,
                        new Point(
                            H / 2 + (hueSliderRadius - hueSliderSingleRadius) *
                            -double.Sin((rotatePosition) / 360 * Double.Pi * 2),
                            H / 2 + (hueSliderRadius - hueSliderSingleRadius) *
                            double.Cos((rotatePosition) / 360 * Double.Pi * 2)) + _positionEffect,
                        new Point(H / 2 + hueSliderRadius * -double.Sin(rotatePosition / 360 * Double.Pi * 2),
                            H / 2 + hueSliderRadius * double.Cos(rotatePosition / 360 * Double.Pi * 2)) +
                        _positionEffect
                    );
                }

                //鼠标悬停的color
                var currentColor = new HslColor(1, Color.H, 1, .5).ToRgb();
                var currentHue = currentColor.ToHsl().H;
                context.DrawEllipse(new SolidColorBrush(currentColor), null,
                    new Point(
                        H / 2 + (hueSliderRadius - hueSliderSingleRadius) *
                        -double.Sin(currentHue / 360 * Double.Pi * 2),
                        H / 2 + (hueSliderRadius - hueSliderSingleRadius) *
                        double.Cos(currentHue / 360 * Double.Pi * 2)) + _positionEffect,
                    H * .02, H * .02
                );

                //最终选择的color
                var currentSelectedColor = SelectedColor;
                var currentSelectedHue = currentSelectedColor.ToHsl().H;
                context.DrawEllipse(new SolidColorBrush(currentSelectedColor), null,
                    new Point(
                        H / 2 + (hueSliderRadius + 2 * hueSliderSingleRadius) *
                        -double.Sin(currentSelectedHue / 360 * Double.Pi * 2),
                        H / 2 + (hueSliderRadius + 2 * hueSliderSingleRadius) *
                        double.Cos(currentSelectedHue / 360 * Double.Pi * 2)) + _positionEffect,
                    H * .03, H * .03
                );
            }

            //内部的圈，包含灰度和亮度
            {
                var innerSliderRadius = H * .2;
                {
                    //亮度
                    //0.2 - 0.8
                    {
                        for (int i = 0; i < 150; i++)
                        {
                            var color =
                                new HslColor(1.0, SelectedColor.ToHsl().H, SelectedColor.ToHsl().S, .6 * i / 150.0 + .2).ToRgb();

                            double rotatePosition = -45 + 15 + i;

                            context.DrawEllipse(new SolidColorBrush(color), null,
                                new Point(
                                    H / 2 + (innerSliderRadius) *
                                    -double.Sin((rotatePosition) / 360 * Double.Pi * 2),
                                    H / 2 + (innerSliderRadius) *
                                    double.Cos((rotatePosition) / 360 * Double.Pi * 2)) + _positionEffect * .5,
                                innerSliderRadius * .05, innerSliderRadius * .05
                            );
                        }

                        //渲染悬停的亮度
                        var currentColor = new HslColor(1, SelectedColor.ToHsl().H, SelectedColor.ToHsl().S, Color.L).ToRgb();
                        context.DrawEllipse(new SolidColorBrush(currentColor), null,
                            new Point(
                                H / 2 + (innerSliderRadius - innerSliderRadius * .2) *
                                -double.Sin(((currentColor.ToHsl().L - .2) / .6 * 150 - 45 + 15) / 360 * Double.Pi * 2),
                                H / 2 + (innerSliderRadius - innerSliderRadius * .2) *
                                double.Cos(((currentColor.ToHsl().L - .2) / .6 * 150 - 45 + 15) / 360 * Double.Pi * 2)) + _positionEffect * .5,
                            H * .02, H * .02
                        );
                    }

                    //灰度
                    {
                        for (int i = 0; i < 150; i++)
                        {
                            var color =
                                new HslColor(1.0, SelectedColor.ToHsl().H, i / 150.0, SelectedColor.ToHsl().L).ToRgb();

                            double rotatePosition = 135 + 15 + i;

                            context.DrawEllipse(new SolidColorBrush(color), null,
                                new Point(
                                    H / 2 + (innerSliderRadius) *
                                    -double.Sin((rotatePosition) / 360 * Double.Pi * 2),
                                    H / 2 + (innerSliderRadius) *
                                    double.Cos((rotatePosition) / 360 * Double.Pi * 2)) + _positionEffect * .5,
                                innerSliderRadius * .05, innerSliderRadius * .05
                            );
                        }
                        var currentColor = new HslColor(1, SelectedColor.ToHsl().H, Color.S, SelectedColor.ToHsl().L).ToRgb();
                        context.DrawEllipse(new SolidColorBrush(currentColor), null,
                            new Point(
                                H / 2 + (innerSliderRadius - innerSliderRadius * .05) *
                                -double.Sin((currentColor.ToHsl().S * 150 + 135 + 15) / 360 * Double.Pi * 2),
                                H / 2 + (innerSliderRadius - innerSliderRadius * .05) *
                                double.Cos((currentColor.ToHsl().S * 150 + 135 + 15) / 360 * Double.Pi * 2)) + _positionEffect * .5,
                            H * .02, H * .02
                        );
                    }
                }
            }
        }

        context.DrawEllipse(new SolidColorBrush(SelectedColor), null, new Point(W / 2, H / 2) + _positionEffect / 1.5,
            H * .05 * _selectedColorScale, H * .05 * _selectedColorScale);
    }
}

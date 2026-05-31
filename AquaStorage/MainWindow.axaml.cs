using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AquaStorage.Helpers;
using AquaStorage.Models;

namespace AquaStorage;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        App.BackgroundImageChanged += OnBackgroundImageChanged;
        App.ThemeChanged += UpdateDimColor;
        LoadBackground();
    }

    private void LoadBackground()
    {
        var config = App.GetSettings();
        if (config == null) return;
        string? path = CacheHelper.ResolveBackgroundImage(config.BackgroundImagePath, config.BackgroundImageCache);
        if (path == null) return;
        ApplyBackground(path, config);
    }

    private void OnBackgroundImageChanged(string? cacheName)
    {
        if (cacheName == null)
        {
            BgImage.IsVisible = false;
            BgMask.Opacity = 0;
            return;
        }

        var config = App.GetSettings();
        if (config == null) return;
        string? path = CacheHelper.ResolveBackgroundImage(config.BackgroundImagePath, cacheName);
        if (path == null)
        {
            BgImage.IsVisible = false;
            BgMask.Opacity = 0;
            return;
        }

        UpdateDimColor();
        ApplyBackground(path, config);
    }

    private void UpdateDimColor()
    {
        bool isLight = Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
        BgMask.Fill = new SolidColorBrush(isLight ? Colors.White : Colors.Black);
    }

    private void ApplyBackground(string imagePath, SettingsConfig config)
    {
        try
        {
            BgImage.Source = new Bitmap(imagePath);
            BgImage.IsVisible = true;

            BgImage.Stretch = config.BackgroundFillMode switch
            {
                "Fill" => Stretch.Fill,
                "Uniform" => Stretch.Uniform,
                "Tile" => Stretch.None,
                _ => Stretch.UniformToFill
            };

            BgMask.Opacity = config.BackgroundMask;

            double blur = config.BackgroundBlur;
            if (blur > 0)
            {
                BgImage.Effect = new BlurEffect { Radius = blur * 20 };
            }
            else
            {
                BgImage.Effect = null;
            }
        }
        catch
        {
            BgImage.IsVisible = false;
            BgMask.Opacity = 0;
        }
    }

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
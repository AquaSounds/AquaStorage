using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AquaStorage.Helpers;
using AquaStorage.Models;
using AquaStorage.Services;
using Avalonia.Styling;

namespace AquaStorage;

public partial class App : Application
{
    private const string SettingsConfigKey = "Config/SettingsConfig";
    public static event Action<Color>? AccentColorChanged;
    public static event Action? ThemeChanged;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CacheService.Initialize();
        LoadSettings();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void LoadSettings()
    {
        var config = ConfigHelper.LoadConfig<SettingsConfig>(SettingsConfigKey);
        if (config?.AccentColor is { } hex)
            ApplyAccentColor(Color.Parse(hex));
        if (config?.IsLightTheme == true)
            ApplyTheme(true);
    }

    public static void ApplyAccentColor(Color color)
    {
        if (Current?.Resources.ContainsKey("AccentPrimary") == true)
            Current.Resources["AccentPrimary"] = color;
        AccentColorChanged?.Invoke(color);
    }

    public static void ApplyTheme(bool isLight)
    {
        if (Current != null)
            Current.RequestedThemeVariant = isLight ? ThemeVariant.Light : ThemeVariant.Dark;
        ThemeChanged?.Invoke();
    }
}

using System;
using System.Globalization;
using System.Linq;
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
    public static event Action<string?>? BackgroundImageChanged;

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

        if (!string.IsNullOrEmpty(config?.Language))
        {
            Localizer.SetCulture(new CultureInfo(config.Language));
        }
        else
        {
            var sysCulture = CultureInfo.CurrentUICulture;
            var supported = new[] { "en", "zh-Hans", "zh-Hant", "ja" };
            if (!supported.Contains(sysCulture.Name) &&
                !supported.Contains(sysCulture.TwoLetterISOLanguageName))
            {
                Localizer.SetCulture(new CultureInfo("en"));
            }
        }

        if (config?.AccentColor is { } hex)
            ApplyAccentColor(Color.Parse(hex));

        // Migrate old config
        string mode = config?.ThemeMode ?? "System";
        if (config?.IsLightTheme == true && string.IsNullOrEmpty(config?.ThemeMode))
            mode = "Light";

        ApplyTheme(mode);
        BackgroundImageChanged?.Invoke(config?.BackgroundImageCache);
    }

    public static void ApplyAccentColor(Color color)
    {
        if (Current?.Resources.ContainsKey("AccentPrimary") == true)
            Current.Resources["AccentPrimary"] = color;
        AccentColorChanged?.Invoke(color);
    }

    public static void ApplyTheme(string mode)
    {
        if (Current == null) return;

        ThemeVariant variant = mode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        // Must set variant FIRST so GetSystemBackgroundColor reads the resolved theme
        Current.RequestedThemeVariant = variant;

        Color bgColor = mode switch
        {
            "Light" => Color.Parse("#F3F3F3"),
            "Dark" => Color.Parse("#2B2B2B"),
            _ => GetSystemBackgroundColor()
        };

        Current.Resources["WindowBackgroundBrush"] = new SolidColorBrush(bgColor);

        Current.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        if (mode == "System")
            Current.ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ThemeChanged?.Invoke();
    }

    private static void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (Current == null) return;
        Current.Resources["WindowBackgroundBrush"] = new SolidColorBrush(GetSystemBackgroundColor());
        ThemeChanged?.Invoke();
    }

    private static Color GetSystemBackgroundColor()
    {
        return Current?.ActualThemeVariant == ThemeVariant.Light
            ? Color.Parse("#F3F3F3")
            : Color.Parse("#2B2B2B");
    }

    public static void NotifyBackgroundImageChanged(string? cacheName) =>
        BackgroundImageChanged?.Invoke(cacheName);

    public static SettingsConfig? GetSettings() =>
        ConfigHelper.LoadConfig<SettingsConfig>(SettingsConfigKey);

    public static void SaveSettings(SettingsConfig config) =>
        ConfigHelper.SaveConfig(SettingsConfigKey, config);
}

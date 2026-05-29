using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AquaStorage.Helpers;
using AquaStorage.Models;
using AquaStorage.Services;

namespace AquaStorage;

public partial class App : Application
{
    private const string ThemeConfigKey = "Config/ThemeConfig";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CacheService.Initialize();
        LoadAccentColor();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void LoadAccentColor()
    {
        var config = ConfigHelper.LoadConfig<ThemeConfig>(ThemeConfigKey);
        if (config?.AccentColor is { } hex)
        {
            ApplyAccentColor(Color.Parse(hex));
        }
    }

    public static void ApplyAccentColor(Color color)
    {
        if (Current?.Resources.ContainsKey("AccentPrimary") == true)
            Current.Resources["AccentPrimary"] = color;
    }
}

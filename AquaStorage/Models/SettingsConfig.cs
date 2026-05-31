namespace AquaStorage.Models;

public class SettingsConfig
{
    public string? ConfigPath { get; set; }
    public double? DefaultFontSize { get; set; }
    public long? MaxCacheBytes { get; set; }
    public string? AccentColor { get; set; }

    // Deprecated — migrated to ThemeMode
    public bool IsLightTheme { get; set; }

    /// <summary>"System" | "Dark" | "Light"</summary>
    public string ThemeMode { get; set; } = "System";

    /// <summary>Original file path of background image</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>Cached filename inside Cache/Backgrounds/</summary>
    public string? BackgroundImageCache { get; set; }

    /// <summary>Fill | UniformToFill | Uniform | Tile</summary>
    public string BackgroundFillMode { get; set; } = "UniformToFill";

    /// <summary>Mask opacity 0-1 (black for dark theme, white for light)</summary>
    public double BackgroundMask { get; set; } = 0.5;

    /// <summary>Blur strength 0-1</summary>
    public double BackgroundBlur { get; set; } = 0.0;

    /// <summary>"Dark" | "Light" — control theme when using custom background</summary>
    public string? BackgroundControlTheme { get; set; }

    public string? Language { get; set; }
}

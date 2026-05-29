namespace AquaStorage.Models;

public class SettingsConfig
{
    public string? ConfigPath { get; set; }
    public double? DefaultFontSize { get; set; }
    public long? MaxCacheBytes { get; set; }
    public string? AccentColor { get; set; }
    public bool IsLightTheme { get; set; }
}

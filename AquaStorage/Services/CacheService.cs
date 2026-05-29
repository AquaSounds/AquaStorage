using AquaStorage.Helpers;
using AquaStorage.Models;

namespace AquaStorage.Services;

public static class CacheService
{
    private const string ConfigKey = "Config/CacheConfig";
    private const long DefaultMaxCacheBytes = 512 * 1024L; // 512KB

    public static long? MaxCacheBytes { get; private set; }

    public static void Initialize()
    {
        var config = ConfigHelper.LoadConfig<CacheSettings>(ConfigKey);
        MaxCacheBytes = config?.MaxCacheBytes ?? DefaultMaxCacheBytes;
        CacheHelper.SetMaxCacheBytes(MaxCacheBytes);
    }

    public static void SetMaxCache(long? bytes)
    {
        MaxCacheBytes = bytes;
        CacheHelper.SetMaxCacheBytes(bytes);
        var config = ConfigHelper.LoadConfig<CacheSettings>(ConfigKey) ?? new CacheSettings();
        config.MaxCacheBytes = bytes;
        ConfigHelper.SaveConfig(ConfigKey, config);
    }

    public static void ClearAll() => CacheHelper.ClearAll();

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024 * 1024):0.##}TB";
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):0.##}GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):0.##}MB";
        return $"{bytes / 1024.0:0.##}KB";
    }

    public static long? ParseSize(string input)
    {
        input = input.Trim();
        if (string.IsNullOrEmpty(input)) return null;

        int i = input.Length - 1;
        while (i >= 0 && char.IsLetter(input[i])) i--;
        string unit = input[(i + 1)..];
        string numPart = (i >= 0 ? input[..(i + 1)] : input).TrimEnd();

        if (!double.TryParse(numPart, out double value) || value < 0) return null;

        long multiplier = unit.ToUpperInvariant() switch
        {
            "KB" => 1024L,
            "MB" => 1024L * 1024,
            "GB" => 1024L * 1024 * 1024,
            "TB" => 1024L * 1024 * 1024 * 1024,
            "" => 1024L * 1024,
            _ => 0
        };

        return multiplier > 0 ? (long)(value * multiplier) : null;
    }
}

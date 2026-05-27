using System;
using System.IO;

namespace AquaStorage.Helpers;

public static class ConfigHelper
{
    private const string AppFolderName = "AquaStorage";

    private static string GetRoamingPath()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(roaming, AppFolderName);
    }

    private static string GetFullPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentNullException(nameof(filename));
        return Path.Combine(GetRoamingPath(), filename);
    }

    public static void SaveConfig(string filename, object? content)
    {
        string fullPath = GetFullPath(filename);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonHelper.Serialize(content);
        File.WriteAllText(fullPath, json, System.Text.Encoding.UTF8);
    }

    public static T? LoadConfig<T>(string filename) where T : class
    {
        try
        {
            string fullPath = GetFullPath(filename);
            if (!File.Exists(fullPath)) return default;

            string json = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
            return JsonHelper.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }
}

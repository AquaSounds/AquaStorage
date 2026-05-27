using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AquaStorage.Helpers;

public static class JsonHelper
{
    private static readonly JsonSerializerOptions _defaultOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(object? obj, JsonSerializerOptions? options = null)
    {
        if (obj == null) return "{}";
        return JsonSerializer.Serialize(obj, obj.GetType(), options ?? _defaultOptions);
    }

    public static object? Deserialize(string json, JsonSerializerOptions? options = null)
    {
        try
        {
            return JsonSerializer.Deserialize<dynamic>(json, options ?? _defaultOptions);
        }
        catch
        {
            return null;
        }
    }

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, options ?? _defaultOptions);
        }
        catch
        {
            return default;
        }
    }
}

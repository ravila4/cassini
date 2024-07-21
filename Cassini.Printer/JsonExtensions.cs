using System.Text.Json;

namespace Cassini.Printer;

internal static class JsonExtensions
{
    public static string? GetStringProperty(this JsonElement e, string key)
    {
        if (e.TryGetProperty(key, out var value))
        {
            return value.GetString();
        }

        return null;
    }
    
    public static int? GetIntProperty(this JsonElement e, string key)
    {
        if (e.TryGetProperty(key, out var value))
        {
            return value.GetInt32();
        }

        return null;
    }
    
    public static string[]? GetStringArrayProperty(this JsonElement e, string key)
    {
        if (e.TryGetProperty(key, out var value))
        {
            var array = new string[value.GetArrayLength()];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = value[i].GetString()!;
            }

            return array;
        }

        return null;
    }

    public static JsonElement? MaybeProperty(this JsonElement e, string key)
    {
        if (e.TryGetProperty(key, out var value))
        {
            return value;
        }

        return null;
    }
}
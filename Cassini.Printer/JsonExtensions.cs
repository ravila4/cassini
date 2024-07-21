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

    public static JsonElement? MaybeProperty(this JsonElement e, string key)
    {
        if (e.TryGetProperty(key, out var value))
        {
            return value;
        }

        return null;
    }
}
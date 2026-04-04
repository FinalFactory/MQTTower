using System.Globalization;

namespace MQTTower.Infrastructure.Monitoring;

/// <summary>Rewrites Mosquitto log lines that start with a Unix-seconds prefix into local date/time.</summary>
public static class MosquittoLogParser
{
    /// <summary>Mosquitto uses <c>timestamp: message</c> where timestamp is Unix time in seconds.</summary>
    public static string FormatLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 5)
        {
            return line;
        }

        var epochPart = line.AsSpan(0, colon);
        if (!long.TryParse(epochPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochSec))
        {
            return line;
        }

        // Plausible Unix seconds (roughly 2001–2286 UTC).
        if (epochSec < 1_000_000_000L || epochSec > 9_999_999_999L)
        {
            return line;
        }

        try
        {
            var local = DateTimeOffset.FromUnixTimeSeconds(epochSec).ToLocalTime();
            var rest = colon + 1 < line.Length ? line[(colon + 1)..].TrimStart() : string.Empty;
            return $"{local:yyyy-MM-dd HH:mm:ss}: {rest}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return line;
        }
    }
}

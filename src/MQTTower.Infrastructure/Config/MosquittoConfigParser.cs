using System.Text.RegularExpressions;

namespace MQTTower.Infrastructure.Config;

/// <summary>
/// Minimal parsing of mosquitto.conf for values the app needs (listener port).
/// </summary>
public static partial class MosquittoConfigParser
{
    /// <summary>
    /// Returns the first TCP listener port from <paramref name="configContent"/>, or <paramref name="fallback"/> when none is found.
    /// Mosquitto defaults to 1883 when no <c>listener</c> directive is present.
    /// </summary>
    public static int ParseListenerPort(string configContent, int fallback = 1883)
    {
        if (string.IsNullOrWhiteSpace(configContent))
        {
            return fallback;
        }

        foreach (var line in configContent.Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var m = ListenerLineRegex().Match(trimmed);
            if (m.Success && int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var port)
                && port is > 0 and <= 65535)
            {
                return port;
            }
        }

        return fallback;
    }

    [GeneratedRegex(@"^listener\s+(\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ListenerLineRegex();
}

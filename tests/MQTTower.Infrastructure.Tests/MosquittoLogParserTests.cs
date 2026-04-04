using MQTTower.Infrastructure.Monitoring;

namespace MQTTower.Infrastructure.Tests;

public sealed class MosquittoLogParserTests
{
    [Fact]
    public void FormatLine_rewrites_unix_prefix()
    {
        var raw = "1775322814: mosquitto version 2.0.21 starting";
        var formatted = MosquittoLogParser.FormatLine(raw);
        Assert.StartsWith("20", formatted, StringComparison.Ordinal);
        Assert.Contains("mosquitto version 2.0.21 starting", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("1775322814:", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatLine_leaves_non_mosquitto_lines_unchanged()
    {
        const string plain = "no timestamp here";
        Assert.Equal(plain, MosquittoLogParser.FormatLine(plain));
    }

    [Fact]
    public void FormatLine_leaves_short_prefix_unchanged()
    {
        const string shortEpoch = "123: too short";
        Assert.Equal(shortEpoch, MosquittoLogParser.FormatLine(shortEpoch));
    }
}

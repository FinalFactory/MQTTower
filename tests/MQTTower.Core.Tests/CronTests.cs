using Cronos;
using FluentAssertions;

namespace MQTTower.Core.Tests;

public sealed class CronTests
{
    [Fact]
    public void Standard_cron_parses()
    {
        var expr = CronExpression.Parse("0 * * * *", CronFormat.Standard);
        var next = expr.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc, inclusive: false);
        next.Should().NotBeNull();
    }
}

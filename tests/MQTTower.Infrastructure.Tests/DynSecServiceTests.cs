using FluentAssertions;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Infrastructure.Tests;

public sealed class DynSecServiceTests
{
    [Theory]
    [InlineData("$CONTROL/dynamic-security/v1", "$CONTROL/dynamic-security/v1/#")]
    [InlineData("$CONTROL/dynamic-security/v1/", "$CONTROL/dynamic-security/v1/#")]
    public void ControlResponseSubscriptionFilter_appends_hash_for_mosquitto_replies(string controlTopic, string expected)
    {
        DynSecService.ControlResponseSubscriptionFilter(controlTopic).Should().Be(expected);
    }

    [Fact]
    public void ControlResponseSubscriptionFilter_preserves_existing_wildcard_filter()
    {
        DynSecService.ControlResponseSubscriptionFilter("$CONTROL/dynamic-security/#").Should().Be("$CONTROL/dynamic-security/#");
    }
}

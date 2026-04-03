using FluentAssertions;
using MQTTower.Core;
using MQTTower.Infrastructure.Automation;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Infrastructure.Tests;

public sealed class WatcherEngineTests
{
    [Fact]
    public void IsEligibleForLocalWatcher_null_or_default_local_is_true()
    {
        WatcherEngine.IsEligibleForLocalWatcher(null).Should().BeTrue();
        WatcherEngine.IsEligibleForLocalWatcher(BrokerConstants.DefaultLocalBrokerId).Should().BeTrue();
    }

    [Fact]
    public void IsEligibleForLocalWatcher_other_broker_is_false()
    {
        WatcherEngine.IsEligibleForLocalWatcher(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Topic_pattern_uses_mqtt_wildcard_semantics()
    {
        MqttTopicMatcher.TopicMatches("foo/bar/baz", "foo/#").Should().BeTrue();
        MqttTopicMatcher.TopicMatches("foo/bar/baz", "foo/+/baz").Should().BeTrue();
        MqttTopicMatcher.TopicMatches("foo/bar/baz", "foo/bar/baz").Should().BeTrue();
        MqttTopicMatcher.TopicMatches("foo/bar/baz", "nomatch").Should().BeFalse();
        MqttTopicMatcher.TopicMatches("foo", "bar").Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_empty_condition_is_true()
    {
        WatcherEngine.EvaluateCondition("", "anything").Should().BeTrue();
        WatcherEngine.EvaluateCondition("   ", "x").Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_requires_substring_in_payload()
    {
        WatcherEngine.EvaluateCondition("alarm", "only warnings").Should().BeFalse();
        WatcherEngine.EvaluateCondition("alarm", "there is an ALARM").Should().BeTrue();
    }
}

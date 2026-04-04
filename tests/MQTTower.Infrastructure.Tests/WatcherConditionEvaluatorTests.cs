using FluentAssertions;
using MQTTower.Infrastructure.Automation;
using MQTTower.Infrastructure.Mqtt;

namespace MQTTower.Infrastructure.Tests;

public sealed class WatcherConditionEvaluatorTests
{
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
        WatcherConditionEvaluator.EvaluateCondition("", "anything").Should().BeTrue();
        WatcherConditionEvaluator.EvaluateCondition("   ", "x").Should().BeTrue();
    }

    [Fact]
    public void EvaluateCondition_requires_substring_in_payload()
    {
        WatcherConditionEvaluator.EvaluateCondition("alarm", "only warnings").Should().BeFalse();
        WatcherConditionEvaluator.EvaluateCondition("alarm", "there is an ALARM").Should().BeTrue();
    }
}

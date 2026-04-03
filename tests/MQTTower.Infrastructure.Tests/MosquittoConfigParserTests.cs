using FluentAssertions;
using MQTTower.Infrastructure.Config;

namespace MQTTower.Infrastructure.Tests;

public sealed class MosquittoConfigParserTests
{
    [Fact]
    public void ParseListenerPort_returns_first_listener()
    {
        var content = """
            # comment
            listener 1883
            allow_anonymous true
            """;

        MosquittoConfigParser.ParseListenerPort(content).Should().Be(1883);
    }

    [Fact]
    public void ParseListenerPort_handles_listener_with_bind_address()
    {
        var content = "listener 9001 127.0.0.1";
        MosquittoConfigParser.ParseListenerPort(content).Should().Be(9001);
    }

    [Fact]
    public void ParseListenerPort_empty_uses_fallback()
    {
        MosquittoConfigParser.ParseListenerPort("", 1883).Should().Be(1883);
    }

    [Fact]
    public void ParseListenerPort_no_listener_uses_fallback()
    {
        MosquittoConfigParser.ParseListenerPort("allow_anonymous true\n", 1883).Should().Be(1883);
    }
}

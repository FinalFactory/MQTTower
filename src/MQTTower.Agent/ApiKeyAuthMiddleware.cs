using System.Security.Cryptography;
using System.Text;

namespace MQTTower.Agent;

public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, AgentApiKeyState apiKeyState)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api"))
        {
            await _next(ctx).ConfigureAwait(false);
            return;
        }

        var expected = apiKeyState.CurrentKey;
        if (string.IsNullOrEmpty(expected))
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsync("Agent API key not configured").ConfigureAwait(false);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var key) || string.IsNullOrEmpty(key))
        {
            ctx.Response.StatusCode = 401;
            return;
        }

        var a = Encoding.UTF8.GetBytes(key.ToString());
        var b = Encoding.UTF8.GetBytes(expected);
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
        {
            ctx.Response.StatusCode = 401;
            return;
        }

        await _next(ctx).ConfigureAwait(false);
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Core.TopicExplorer;
using MQTTower.Infrastructure.Config;
using MQTTower.Infrastructure.Hosting;
using MQTTower.Infrastructure.Monitoring;
using MQTTower.Infrastructure.Mqtt;
using MQTTower.Infrastructure.Options;
using MQTTower.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MqttTowerOptions>(builder.Configuration.GetSection(MqttTowerOptions.SectionName));
builder.Services.AddSingleton<IPostConfigureOptions<MqttTowerOptions>, AgentCoLocatedBrokerPostConfigure>();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

var agentSection = builder.Configuration.GetSection(AgentOptions.SectionName);
var certPath = agentSection["CertificatePath"] ?? Path.Combine(AppContext.BaseDirectory, "certs", "agent.pfx");
var certPassword = agentSection["CertificatePassword"] ?? "mqttower-agent-dev";

if (!string.IsNullOrEmpty(certPath) && !File.Exists(certPath) && builder.Configuration.GetValue("Agent:HttpsPort", 5100) > 0)
{
    AgentTlsCertificate.EnsureSelfSignedPfx(certPath, certPassword);
}

var httpsPort = builder.Configuration.GetValue("Agent:HttpsPort", 5100);
var httpPort = builder.Configuration.GetValue("Agent:HttpPort", 0);
var requireClientCert = builder.Configuration.GetValue("Agent:RequireClientCertificate", false);
var tlsCaPath = agentSection["TlsCaCertPath"];
builder.WebHost.ConfigureKestrel(o =>
{
    if (httpsPort > 0)
    {
        o.ListenAnyIP(httpsPort, listenOptions =>
        {
            if (requireClientCert && !string.IsNullOrWhiteSpace(tlsCaPath) && File.Exists(tlsCaPath) && File.Exists(certPath))
            {
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                        certPath,
                        certPassword,
                        X509KeyStorageFlags.DefaultKeySet);
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.ClientCertificateValidation = (certificate, chain, sslPolicyErrors) =>
                    {
                        if (certificate is null)
                        {
                            return false;
                        }

                        try
                        {
                            using var ca = X509CertificateLoader.LoadCertificateFromFile(tlsCaPath);
                            using var clientCert = new X509Certificate2(certificate);
                            using var xchain = new X509Chain();
                            xchain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            xchain.ChainPolicy.CustomTrustStore.Add(ca);
                            xchain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            return xchain.Build(clientCert);
                        }
                        catch
                        {
                            return false;
                        }
                    };
                });
            }
            else
            {
                listenOptions.UseHttps(certPath, certPassword);
            }
        });
    }

    if (httpPort > 0)
    {
        o.ListenAnyIP(httpPort);
    }
});

builder.Services.AddSingleton<MqttConnectionService>();
builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttConnectionService>());
builder.Services.AddSingleton<IMqttSubscriber>(sp => sp.GetRequiredService<MqttConnectionService>());
builder.Services.AddSingleton<BrokerStatsCollector>();
builder.Services.AddSingleton<IBrokerStatsProvider>(sp => sp.GetRequiredService<BrokerStatsCollector>());
builder.Services.AddSingleton<TopicExplorerService>();
builder.Services.AddSingleton<ITopicExplorerService>(sp => sp.GetRequiredService<TopicExplorerService>());
builder.Services.AddSingleton<IDynSecService, DynSecService>();
builder.Services.AddScoped<IBrokerConfigStore, FileBrokerConfigStore>();
builder.Services.AddScoped<IBrokerLogReader, BrokerLogReader>();

builder.Services.AddHostedService<MqttStartupHostedService>();
builder.Services.AddSingleton<AgentWatcherStore>();
builder.Services.AddHostedService<AgentWatcherHostedService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(nameof(AgentRegistrationHostedService), (sp, c) =>
{
    c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHostedService<AgentRegistrationHostedService>();
builder.Services.AddSingleton<AgentApiKeyState>();
builder.Services.AddSingleton<AgentSseLimits>();

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("agent", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 300;
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;
        if (ex is TimeoutException)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "DynSec request timed out. Ensure Mosquitto is running and the dynamic-security plugin is active.",
            }).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error." }).ConfigureAwait(false);
    });
});

app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseRateLimiter();

var startTime = Process.GetCurrentProcess().StartTime;

static string AgentVersion() =>
    Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0";

app.MapGet("/health", (MqttConnectionService m, ILoggerFactory loggerFactory) =>
{
    string? thumb = null;
    var opts = app.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
    var log = loggerFactory.CreateLogger("MQTTower.Agent.Health");
    if (!string.IsNullOrEmpty(opts.CertificatePath) && File.Exists(opts.CertificatePath))
    {
        try
        {
            thumb = AgentTlsCertificate.GetThumbprintSha256(opts.CertificatePath, opts.CertificatePassword ?? string.Empty);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not read TLS certificate thumbprint from {Path}", opts.CertificatePath);
        }
    }

    return Results.Ok(new AgentInfo
    {
        AgentVersion = AgentVersion(),
        BrokerVersion = null,
        Uptime = DateTime.UtcNow - startTime.ToUniversalTime(),
        MqttConnected = m.IsConnected,
        TlsCertThumbprint = thumb,
    });
}).AllowAnonymous();

var api = app.MapGroup("/api").RequireRateLimiting("agent");

api.MapPost("/agent/key", (SetApiKeyBody body, AgentApiKeyState state) =>
{
    state.SetKey(body.ApiKey);
    return Results.Ok();
});

api.MapPut("/watchers/sync", (AgentWatcherStore store, List<TopicWatcher>? body) =>
{
    store.ReplaceAll(body ?? new List<TopicWatcher>());
    return Results.NoContent();
});

api.MapGet("/stats", (IBrokerStatsProvider p) => Results.Ok(p.GetCurrent()));

api.MapPost("/mqtt/publish", async (PublishBody body, IMqttPublisher pub, CancellationToken ct) =>
{
    var bytes = Encoding.UTF8.GetBytes(body.Payload ?? string.Empty);
    await pub.PublishAsync(body.Topic, bytes, body.Qos, body.Retain, ct).ConfigureAwait(false);
    return Results.Ok();
});

api.MapGet("/dynsec/clients", async (IDynSecService d, CancellationToken ct) =>
{
    var list = await d.ListClientsAsync(ct).ConfigureAwait(false);
    return Results.Ok(list.ToList());
});

api.MapPost("/dynsec/clients", async (CreateClientBody b, IDynSecService d, CancellationToken ct) =>
{
    await d.CreateClientAsync(b.Username, b.Password, b.Roles, b.Groups, ct).ConfigureAwait(false);
    return Results.Ok();
});

api.MapDelete("/dynsec/clients/{username}", async (string username, IDynSecService d, CancellationToken ct) =>
{
    await d.DeleteClientAsync(username, ct).ConfigureAwait(false);
    return Results.NoContent();
});

api.MapPut("/dynsec/clients/{username}/enabled", async (string username, SetEnabledBody b, IDynSecService d, CancellationToken ct) =>
{
    await d.SetClientEnabledAsync(username, b.Enabled, ct).ConfigureAwait(false);
    return Results.NoContent();
});

api.MapGet("/dynsec/roles", async (IDynSecService d, CancellationToken ct) =>
{
    var list = await d.ListRolesAsync(ct).ConfigureAwait(false);
    return Results.Ok(list.ToList());
});

api.MapPost("/dynsec/roles", async (CreateRoleBody b, IDynSecService d, CancellationToken ct) =>
{
    var acls = b.Acls is { Count: > 0 } ? b.Acls : Array.Empty<AclEntry>();
    await d.CreateRoleAsync(b.Name, b.Description, acls, ct).ConfigureAwait(false);
    return Results.Ok();
});

api.MapDelete("/dynsec/roles/{name}", async (string name, IDynSecService d, CancellationToken ct) =>
{
    await d.DeleteRoleAsync(name, ct).ConfigureAwait(false);
    return Results.NoContent();
});

api.MapGet("/dynsec/groups", async (IDynSecService d, CancellationToken ct) =>
{
    var list = await d.ListGroupsAsync(ct).ConfigureAwait(false);
    return Results.Ok(list.ToList());
});

api.MapPost("/dynsec/groups", async (CreateGroupBody b, IDynSecService d, CancellationToken ct) =>
{
    var roles = b.RoleNames is { Count: > 0 } ? b.RoleNames : Array.Empty<string>();
    var clients = b.ClientUsernames is { Count: > 0 } ? b.ClientUsernames : Array.Empty<string>();
    await d.CreateGroupAsync(b.Name, b.Description, roles, clients, ct).ConfigureAwait(false);
    return Results.Ok();
});

api.MapDelete("/dynsec/groups/{name}", async (string name, IDynSecService d, CancellationToken ct) =>
{
    await d.DeleteGroupAsync(name, ct).ConfigureAwait(false);
    return Results.NoContent();
});

api.MapGet("/config", async (IBrokerConfigStore s, CancellationToken ct) =>
    Results.Text(await s.ReadAsync(ct).ConfigureAwait(false)));

api.MapPut("/config", async (HttpRequest req, IBrokerConfigStore s, CancellationToken ct) =>
{
    using var reader = new StreamReader(req.Body);
    var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    await s.WriteAsync(text, ct).ConfigureAwait(false);
    return Results.NoContent();
});

api.MapGet("/logs", async (IBrokerLogReader r, int lines, CancellationToken ct) =>
{
    var linesList = await r.GetRecentLinesAsync(lines <= 0 ? 500 : lines, ct).ConfigureAwait(false);
    return Results.Ok(linesList.ToList());
});

api.MapGet("/topics", (ITopicExplorerService t) => Results.Ok(t.GetRoots()));

api.MapPost("/broker/restart", async (IOptions<AgentOptions> opts, CancellationToken ct) =>
{
    var cmd = opts.Value.RestartCommand;
    if (string.IsNullOrWhiteSpace(cmd))
    {
        return Results.StatusCode(501);
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? $"/c {cmd}" : $"-c \"{cmd.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null)
        {
            return Results.Problem("Could not start restart process");
        }

        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

api.MapGet("/stats/stream", async (HttpContext ctx, IBrokerStatsProvider statsProvider, AgentSseLimits limits, CancellationToken ct) =>
{
    if (!await limits.Stats.WaitAsync(0, ct).ConfigureAwait(false))
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    try
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.StartAsync(ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            var json = JsonSerializer.Serialize(statsProvider.GetCurrent());
            await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }
    finally
    {
        limits.Stats.Release();
    }
});

api.MapGet("/logs/stream", async (HttpContext ctx, IBrokerLogReader reader, AgentSseLimits limits, CancellationToken ct) =>
{
    if (!await limits.Logs.WaitAsync(0, ct).ConfigureAwait(false))
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    try
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.StartAsync(ct).ConfigureAwait(false);
        var lastSig = string.Empty;
        while (!ct.IsCancellationRequested)
        {
            var lines = await reader.GetRecentLinesAsync(500, ct).ConfigureAwait(false);
            var sig = string.Join('\n', lines);
            if (!string.Equals(sig, lastSig, StringComparison.Ordinal))
            {
                lastSig = sig;
                var payload = JsonSerializer.Serialize(lines);
                await ctx.Response.WriteAsync($"data: {payload}\n\n", ct).ConfigureAwait(false);
                await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }
    finally
    {
        limits.Logs.Release();
    }
});

api.MapGet("/topics/stream", async (HttpContext ctx, ITopicExplorerService topicSvc, AgentSseLimits limits, CancellationToken ct) =>
{
    if (!await limits.Topics.WaitAsync(0, ct).ConfigureAwait(false))
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    try
    {
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        await ctx.Response.StartAsync(ct).ConfigureAwait(false);
        var lastJson = string.Empty;
        while (!ct.IsCancellationRequested)
        {
            var roots = topicSvc.GetRoots();
            var json = JsonSerializer.Serialize(roots);
            if (json != lastJson)
            {
                lastJson = json;
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }
    finally
    {
        limits.Topics.Release();
    }
});

try
{
    await app.RunAsync().ConfigureAwait(false);
}
finally
{
    Log.CloseAndFlush();
}

internal sealed record SetApiKeyBody(string ApiKey);
internal sealed record PublishBody(string Topic, string? Payload, int Qos, bool Retain);
internal sealed record CreateClientBody(string Username, string Password, List<string>? Roles, List<string>? Groups);
internal sealed record SetEnabledBody(bool Enabled);
internal sealed record CreateRoleBody(string Name, string? Description, IReadOnlyList<AclEntry>? Acls);
internal sealed record CreateGroupBody(string Name, string? Description, IReadOnlyList<string>? RoleNames, IReadOnlyList<string>? ClientUsernames);

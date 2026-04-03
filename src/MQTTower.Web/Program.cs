using Serilog;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Automation;
using MQTTower.Infrastructure.Backup;
using MQTTower.Infrastructure.Config;
using MQTTower.Infrastructure.Data;
using MQTTower.Infrastructure.Hosting;
using MQTTower.Infrastructure.Monitoring;
using MQTTower.Infrastructure.Mqtt;
using MQTTower.Infrastructure.Notifications;
using MQTTower.Infrastructure.Options;
using MQTTower.Infrastructure.Agents;
using MQTTower.Infrastructure.Tasmota;
using MQTTower.Web.Components;
using MQTTower.Web.Hosting;
using MQTTower.Web.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MqttTowerOptions>(builder.Configuration.GetSection(MqttTowerOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration[$"{MqttTowerOptions.SectionName}:DatabasePath"]
    ?? "Data Source=mqttower.db";

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

builder.Services.AddSingleton<MqttConnectionService>();
builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttConnectionService>());
builder.Services.AddSingleton<IMqttSubscriber>(sp => sp.GetRequiredService<MqttConnectionService>());
builder.Services.AddSingleton<BrokerStatsCollector>();
builder.Services.AddSingleton<IBrokerStatsProvider>(sp => sp.GetRequiredService<BrokerStatsCollector>());
builder.Services.AddSingleton<TopicExplorerService>();
builder.Services.AddSingleton<ITopicExplorerService>(sp => sp.GetRequiredService<TopicExplorerService>());

builder.Services.AddSingleton<IDynSecService, DynSecService>();
builder.Services.AddScoped<IMetricStore, EfMetricStore>();
builder.Services.AddScoped<IAuditLog, EfAuditLog>();
builder.Services.AddScoped<IDeviceRegistry, EfDeviceRegistry>();
builder.Services.AddScoped<IDeviceStateTracker, EfDeviceStateTracker>();
builder.Services.AddScoped<IUserService, EfUserService>();
builder.Services.AddScoped<IBrokerConfigStore, FileBrokerConfigStore>();
builder.Services.AddScoped<IBrokerLogReader, BrokerLogReader>();
builder.Services.AddScoped<IBrokerRegistry, EfBrokerRegistry>();
builder.Services.AddScoped<IRegistrationTokenService, EfRegistrationTokenService>();
builder.Services.AddSingleton<AgentGatewayFactory>();
builder.Services.AddSingleton<IBrokerGatewayFactory, AuditingBrokerGatewayFactory>();
builder.Services.AddScoped<BrokerContext>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddSingleton<ITasmotaParser, TasmotaParser>();

builder.Services.AddHttpClient(nameof(NtfyNotifier));
builder.Services.AddHttpClient(nameof(WebhookNotifier));
builder.Services.AddSingleton<INotificationChannel, NtfyNotifier>();
builder.Services.AddSingleton<INotificationChannel, WebhookNotifier>();
builder.Services.AddSingleton<INotificationChannel, SmtpNotifier>();

builder.Services.AddScoped<INotificationRouter, NotificationRouter>();

builder.Services.AddSingleton<CronSchedulerService>();
builder.Services.AddSingleton<ISchedulerService>(sp => sp.GetRequiredService<CronSchedulerService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CronSchedulerService>());

builder.Services.AddSingleton<WatcherEngine>();
builder.Services.AddSingleton<IWatcherService>(sp => sp.GetRequiredService<WatcherEngine>());
builder.Services.AddHostedService<WatcherStartupHostedService>();

builder.Services.AddHostedService<MqttStartupHostedService>();
builder.Services.AddHostedService<MetricsCollectorHostedService>();
builder.Services.AddHostedService<DeviceMonitorHostedService>();
builder.Services.AddHostedService<AdminSeedHostedService>();
builder.Services.AddHostedService<BrokerSeedHostedService>();
builder.Services.AddHostedService<BrokerHealthMonitor>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(o =>
{
    // Do not set FallbackPolicy: it applies to static web assets and the Blazor host, causing 302 to /login
    // for CSS/JS and broken UI. Page auth uses @attribute [Authorize] in Components/Pages/_Imports.razor.
    o.AddPolicy("Admin", p => p.RequireRole(nameof(AppUserRole.Admin)));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, MQTTower.Web.Auth.CookieAuthenticationStateProvider>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("auth", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
        opt.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("registration", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
    return Results.Redirect("/login");
}).AllowAnonymous();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

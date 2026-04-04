using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class WatcherNotifyController : ControllerBase
{
    private readonly INotificationRouter _router;
    private readonly IOptions<MqttTowerOptions> _options;

    public WatcherNotifyController(INotificationRouter router, IOptions<MqttTowerOptions> options)
    {
        _router = router;
        _options = options;
    }

    [HttpPost("watcher-notify")]
    [AllowAnonymous]
    public async Task<IActionResult> Post([FromBody] WatcherNotifyBody body, CancellationToken cancellationToken)
    {
        var secret = _options.Value.WatcherNotifySecret;
        if (string.IsNullOrWhiteSpace(secret) || body.Secret != secret)
        {
            return Unauthorized();
        }

        await _router.DispatchAsync("watcher", body.PayloadJson ?? "{}", cancellationToken).ConfigureAwait(false);
        return Ok();
    }
}

public sealed class WatcherNotifyBody
{
    public string Secret { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

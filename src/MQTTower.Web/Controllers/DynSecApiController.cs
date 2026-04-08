using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DynSecApiController : ControllerBase
{
    private readonly IBrokerRegistry _registry;
    private readonly IBrokerGatewayFactory _gatewayFactory;

    public DynSecApiController(IBrokerRegistry registry, IBrokerGatewayFactory gatewayFactory)
    {
        _registry = registry;
        _gatewayFactory = gatewayFactory;
    }

    [HttpGet("clients")]
    public async Task<IActionResult> Clients([FromQuery] Guid? brokerId, CancellationToken cancellationToken)
    {
        var broker = await ResolveBrokerAsync(brokerId, cancellationToken).ConfigureAwait(false);
        if (broker is null)
        {
            return BadRequest(new { error = "brokerId required or no default broker" });
        }

        var gw = _gatewayFactory.Create(broker);
        try
        {
            var list = await gw.ListClientsAsync(cancellationToken).ConfigureAwait(false);
            return Ok(list.ToList());
        }
        finally
        {
            (gw as IDisposable)?.Dispose();
        }
    }

    [HttpGet("roles")]
    public async Task<IActionResult> Roles([FromQuery] Guid? brokerId, CancellationToken cancellationToken)
    {
        var broker = await ResolveBrokerAsync(brokerId, cancellationToken).ConfigureAwait(false);
        if (broker is null)
        {
            return BadRequest(new { error = "brokerId required or no default broker" });
        }

        var gw = _gatewayFactory.Create(broker);
        try
        {
            var list = await gw.ListRolesAsync(cancellationToken).ConfigureAwait(false);
            return Ok(list.ToList());
        }
        finally
        {
            (gw as IDisposable)?.Dispose();
        }
    }

    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] Guid? brokerId, CancellationToken cancellationToken)
    {
        var broker = await ResolveBrokerAsync(brokerId, cancellationToken).ConfigureAwait(false);
        if (broker is null)
        {
            return BadRequest(new { error = "brokerId required or no default broker" });
        }

        var gw = _gatewayFactory.Create(broker);
        try
        {
            var list = await gw.ListGroupsAsync(cancellationToken).ConfigureAwait(false);
            return Ok(list.ToList());
        }
        finally
        {
            (gw as IDisposable)?.Dispose();
        }
    }

    private async Task<BrokerProfile?> ResolveBrokerAsync(Guid? brokerId, CancellationToken cancellationToken)
    {
        if (brokerId.HasValue)
        {
            return await _registry.GetAsync(brokerId.Value, cancellationToken).ConfigureAwait(false);
        }

        return await _registry.GetDefaultLocalAsync(cancellationToken).ConfigureAwait(false);
    }
}

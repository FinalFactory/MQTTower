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
    private readonly IDynSecService _dynSec;

    public DynSecApiController(IDynSecService dynSec)
    {
        _dynSec = dynSec;
    }

    [HttpGet("clients")]
    public Task<IReadOnlyList<MqttClientInfo>> Clients(CancellationToken cancellationToken) => _dynSec.ListClientsAsync(cancellationToken);

    [HttpGet("roles")]
    public Task<IReadOnlyList<MqttRole>> Roles(CancellationToken cancellationToken) => _dynSec.ListRolesAsync(cancellationToken);

    [HttpGet("groups")]
    public Task<IReadOnlyList<MqttGroup>> Groups(CancellationToken cancellationToken) => _dynSec.ListGroupsAsync(cancellationToken);
}

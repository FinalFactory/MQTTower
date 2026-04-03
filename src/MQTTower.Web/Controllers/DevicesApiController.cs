using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DevicesApiController : ControllerBase
{
    private readonly IDeviceRegistry _devices;

    public DevicesApiController(IDeviceRegistry devices)
    {
        _devices = devices;
    }

    [HttpGet]
    public Task<IReadOnlyList<Device>> List([FromQuery] Guid? brokerId, CancellationToken cancellationToken) =>
        _devices.ListAsync(brokerId, cancellationToken);

    [HttpPost]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Upsert([FromBody] Device device, CancellationToken cancellationToken) => _devices.AddOrUpdateAsync(device, cancellationToken);

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Delete(Guid id, CancellationToken cancellationToken) => _devices.DeleteAsync(id, cancellationToken);
}

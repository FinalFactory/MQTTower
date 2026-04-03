using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class WatchersApiController : ControllerBase
{
    private readonly IWatcherService _watchers;

    public WatchersApiController(IWatcherService watchers)
    {
        _watchers = watchers;
    }

    [HttpGet]
    public Task<IReadOnlyList<TopicWatcher>> List([FromQuery] Guid? brokerId, CancellationToken cancellationToken) =>
        _watchers.ListAsync(brokerId, cancellationToken);

    [HttpPost]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Upsert([FromBody] TopicWatcher watcher, CancellationToken cancellationToken) => _watchers.UpsertAsync(watcher, cancellationToken);

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Delete(Guid id, CancellationToken cancellationToken) => _watchers.DeleteAsync(id, cancellationToken);
}

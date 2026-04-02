using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class SchedulerApiController : ControllerBase
{
    private readonly ISchedulerService _scheduler;

    public SchedulerApiController(ISchedulerService scheduler)
    {
        _scheduler = scheduler;
    }

    [HttpGet]
    public Task<IReadOnlyList<ScheduledTask>> List(CancellationToken cancellationToken) => _scheduler.ListAsync(cancellationToken);

    [HttpPost]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Upsert([FromBody] ScheduledTask task, CancellationToken cancellationToken) => _scheduler.UpsertAsync(task, cancellationToken);

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Delete(Guid id, CancellationToken cancellationToken) => _scheduler.DeleteAsync(id, cancellationToken);
}

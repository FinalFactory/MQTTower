using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class NotificationRulesApiController : ControllerBase
{
    private readonly INotificationRouter _router;

    public NotificationRulesApiController(INotificationRouter router)
    {
        _router = router;
    }

    [HttpGet]
    public Task<IReadOnlyList<NotificationRule>> List(CancellationToken cancellationToken) => _router.ListRulesAsync(cancellationToken);

    [HttpPost]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Upsert([FromBody] NotificationRule rule, CancellationToken cancellationToken) => _router.UpsertRuleAsync(rule, cancellationToken);

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(AppUserRole.Admin))]
    public Task Delete(Guid id, CancellationToken cancellationToken) => _router.DeleteRuleAsync(id, cancellationToken);
}

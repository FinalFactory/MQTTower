using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(AppUserRole.Admin))]
public sealed class BackupController : ControllerBase
{
    [HttpGet("download")]
    public async Task<IActionResult> Download([FromServices] IBackupService backup, CancellationToken cancellationToken)
    {
        var bytes = await backup.CreateBackupArchiveAsync(cancellationToken).ConfigureAwait(false);
        return File(bytes, "application/zip", "mqttower-backup.zip");
    }
}

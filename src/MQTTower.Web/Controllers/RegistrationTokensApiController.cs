using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/admin/registration-tokens")]
[Authorize(Roles = nameof(AppUserRole.Admin))]
public sealed class RegistrationTokensApiController : ControllerBase
{
    private readonly IRegistrationTokenService _tokens;

    public RegistrationTokensApiController(IRegistrationTokenService tokens)
    {
        _tokens = tokens;
    }

    [HttpGet]
    public Task<IReadOnlyList<RegistrationTokenRow>> List(CancellationToken cancellationToken) =>
        _tokens.ListAsync(cancellationToken);

    public sealed class MintRequest
    {
        /// <summary>Optional UTC expiry; null means no expiry.</summary>
        public DateTimeOffset? ExpiresAtUtc { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<MintResponse>> Mint([FromBody] MintRequest? body, CancellationToken cancellationToken)
    {
        var plaintext = await _tokens.MintAsync(body?.ExpiresAtUtc, cancellationToken).ConfigureAwait(false);
        return Ok(new MintResponse(plaintext));
    }

    [HttpDelete("{id:guid}")]
    public Task Revoke(Guid id, CancellationToken cancellationToken) => _tokens.RevokeAsync(id, cancellationToken);

    public sealed record MintResponse(string Token);
}

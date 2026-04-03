using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MQTTower.Core.Interfaces;
using MQTTower.Core.Models;
using MQTTower.Infrastructure.Options;

namespace MQTTower.Web.Controllers;

[ApiController]
[Route("api/agents")]
[EnableRateLimiting("registration")]
public sealed class AgentsRegistrationController : ControllerBase
{
    private readonly IBrokerRegistry _registry;
    private readonly MqttTowerOptions _options;
    private readonly IRegistrationTokenService _registrationTokens;

    public AgentsRegistrationController(IBrokerRegistry registry, IOptions<MqttTowerOptions> options, IRegistrationTokenService registrationTokens)
    {
        _registry = registry;
        _options = options.Value;
        _registrationTokens = registrationTokens;
    }

    public sealed class RegisterRequest
    {
        /// <summary>Shared secret or one-time token (validated in action; whitespace-only can mean "not configured").</summary>
        [StringLength(4000)]
        public string RegistrationToken { get; set; } = string.Empty;

        [Required]
        [StringLength(2048, MinimumLength = 1)]
        public string AgentUrl { get; set; } = string.Empty;

        [Required]
        [StringLength(512, MinimumLength = 1)]
        public string ApiKey { get; set; } = string.Empty;

        [StringLength(256)]
        public string? TlsCertThumbprint { get; set; }

        [StringLength(256)]
        public string Name { get; set; } = string.Empty;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest body, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (string.IsNullOrWhiteSpace(_options.RegistrationSecret))
        {
            if (await _registrationTokens.TryConsumeAsync(body.RegistrationToken, cancellationToken).ConfigureAwait(false))
            {
                // ok — one-time token
            }
            else if (string.IsNullOrWhiteSpace(body.RegistrationToken))
            {
                return StatusCode(503, new { error = "Registration is not configured (set RegistrationSecret or mint a one-time token in the dashboard)" });
            }
            else
            {
                return Unauthorized();
            }
        }
        else
        {
            var secretOk = RegistrationSecretMatches(body.RegistrationToken, _options.RegistrationSecret);
            var oneTimeOk = await _registrationTokens.TryConsumeAsync(body.RegistrationToken, cancellationToken).ConfigureAwait(false);
            if (!secretOk && !oneTimeOk)
            {
                return Unauthorized();
            }
        }

        var url = body.AgentUrl.Trim();
        var existing = await _registry.GetByAgentUrlAsync(url, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.UseLocalServices)
            {
                return Conflict(new { error = "AgentUrl conflicts with local broker profile" });
            }

            existing.ApiKey = body.ApiKey;
            existing.Name = string.IsNullOrWhiteSpace(body.Name) ? existing.Name : body.Name.Trim();
            existing.TlsCertThumbprint = body.TlsCertThumbprint;
            existing.Status = BrokerStatus.Pending;
            existing.RegisteredAt = DateTimeOffset.UtcNow;
            existing.Approved = false;
            await _registry.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            return Ok(new { id = existing.Id });
        }

        var profile = new BrokerProfile
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(body.Name) ? "Agent" : body.Name.Trim(),
            AgentUrl = url,
            ApiKey = body.ApiKey,
            TlsCertThumbprint = body.TlsCertThumbprint,
            Status = BrokerStatus.Pending,
            RegisteredAt = DateTimeOffset.UtcNow,
            Approved = false,
            UseLocalServices = false,
        };

        try
        {
            await _registry.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "A broker with this Agent URL already exists." });
        }

        return Ok(new { id = profile.Id });
    }

    /// <summary>Constant-time comparison of registration token to shared secret (SHA-256 digest, 32-byte fixed length).</summary>
    private static bool RegistrationSecretMatches(string registrationToken, string registrationSecret)
    {
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(registrationToken ?? string.Empty));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(registrationSecret ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

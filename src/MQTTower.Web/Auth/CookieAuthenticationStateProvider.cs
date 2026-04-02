using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MQTTower.Web.Auth;

public sealed class CookieAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _http;

    public CookieAuthenticationStateProvider(IHttpContextAccessor http)
    {
        _http = http;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _http.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }
}

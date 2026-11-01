using System.Text.Json;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Identity;
using Autobahn.Infrastructure.Fleet;
using Autobahn.Security.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Autobahn.Host.Fleet;

public sealed class FleetSessionService(IDataProtectionProvider provider, IOptions<FleetOptions> options)
{
    public const string CookieName = "autobahn_fleet_session";
    public const string RequestHeader = "X-AutobahnFleet-Request";
    private readonly IDataProtector _protector = provider.CreateProtector("Autobahn.Fleet.Session.v1");

    public void SignIn(HttpContext context, string key)
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(options.Value.SessionHours);
        var value = _protector.Protect(JsonSerializer.Serialize(new FleetSession(key, expiry)));
        context.Response.Cookies.Append(CookieName, value, new CookieOptions { HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/admin/fleet", Expires = expiry, IsEssential = true });
    }

    public async Task<(string Actor, bool CanWrite, DateTimeOffset ExpiresAt)> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        string? key = null; var expires = DateTimeOffset.UtcNow.AddHours(options.Value.SessionHours);
        if (context.Request.Headers.Authorization.ToString() is { } auth && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) key = auth[7..];
        else if (context.Request.Cookies.TryGetValue(CookieName, out var cookie))
        {
            try
            {
                var session = JsonSerializer.Deserialize<FleetSession>(_protector.Unprotect(cookie));
                if (session is not null && session.ExpiresAt > DateTimeOffset.UtcNow) { key = session.Key; expires = session.ExpiresAt; }
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException) { }
        }
        var result = await context.RequestServices.GetRequiredService<IApiKeyValidator>().ValidateAsync(key, cancellationToken);
        if (!result.IsSuccess) throw new FleetException(401, "signed_out", "Sign in with an Autobahn admin key.");
        if (result.Role is not (ApiKeyRole.Admin or ApiKeyRole.Both)) throw new FleetException(403, "admin_required", "An admin key is required for the fleet console.");
        var canWrite = context.RequestServices.GetRequiredService<OperatorTenantConfiguration>().IsOperatorTenant(result.TenantSlug);
        return ($"{result.TenantSlug}/{result.ApiKeyId}", canWrite, expires);
    }
}

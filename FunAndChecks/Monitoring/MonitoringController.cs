using System.Security.Claims;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Monitoring;

[ApiController]
public sealed class MonitoringController(
    IOptions<MonitoringOptions> options,
    IIdentityService identity,
    ApplicationDbContext db) : ControllerBase
{
    [Authorize]
    [HttpPost("/api/monitoring/session")]
    public async Task<IActionResult> CreateSession(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!options.Value.Enabled)
            return NotFound();

        var userId = User.GetUserId();
        if (!await AccountExistsAsync(userId, cancellationToken))
            return Unauthorized();
        if (!await IsSuperAdminAsync(userId, cancellationToken))
            return Forbid();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], MonitoringAuthentication.Scheme));
        await HttpContext.SignInAsync(MonitoringAuthentication.Scheme, principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(MonitoringAuthentication.SessionLifetime),
            });
        return NoContent();
    }

    // This endpoint is only reachable by the reverse proxy and explicitly
    // authenticates the monitoring cookie instead of accepting the site's JWT.
    [AllowAnonymous]
    [HttpGet("/internal/monitoring/authorize")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> AuthorizeProxy(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!options.Value.Enabled || !MonitoringSecurity.HasValidSecret(
                Request.Headers[MonitoringAuthentication.ProxyKeyHeader].ToString(), options.Value.ProxyKey))
            return NotFound();

        var authentication = await HttpContext.AuthenticateAsync(MonitoringAuthentication.Scheme);
        if (!authentication.Succeeded || !Guid.TryParse(
                authentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return ReenterMonitoring();

        if (!await AccountExistsAsync(userId, cancellationToken))
        {
            await HttpContext.SignOutAsync(MonitoringAuthentication.Scheme);
            return ReenterMonitoring();
        }
        if (!await IsSuperAdminAsync(userId, cancellationToken))
        {
            await HttpContext.SignOutAsync(MonitoringAuthentication.Scheme);
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        Response.Headers[MonitoringAuthentication.UserHeader] = userId.ToString();
        return NoContent();
    }

    private IActionResult ReenterMonitoring()
    {
        var returnUrl = MonitoringSecurity.SafeGrafanaReturn(
            Request.Headers[MonitoringAuthentication.OriginalUriHeader].ToString());
        return Redirect("/metrics?returnUrl=" + Uri.EscapeDataString(returnUrl));
    }

    private Task<bool> AccountExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken);

    private async Task<bool> IsSuperAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        (await identity.GetCurrentRolesAsync(userId, cancellationToken)).Contains(Roles.SuperAdmin);
}

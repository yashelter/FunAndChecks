using System.Security.Claims;
using FunAndChecks.Application.Admins.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace FunAndChecks.Common;

internal sealed class AdminPermissionRequirement(string? key) : IAuthorizationRequirement
{
    public string? Key { get; } = key;
}

internal sealed class AdminPermissionHandler(IAdminPermissionService permissions)
    : AuthorizationHandler<AdminPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        AdminPermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            return;
        }

        CancellationToken token = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        bool allowed = requirement.Key is string key
            ? await permissions.HasAsync(userId, key, token)
            : await permissions.IsSuperAdminAsync(userId, token);
        if (allowed)
        {
            context.Succeed(requirement);
        }
    }
}

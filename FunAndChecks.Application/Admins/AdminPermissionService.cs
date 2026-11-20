using FluentValidation;
using FunAndChecks.Application.Admins.Interfaces;
using FunAndChecks.Application.Common;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Admins;

public sealed class AdminPermissionService(IApplicationDbContext db, IIdentityService identity) : IAdminPermissionService
{
    public async Task<bool> IsSuperAdminAsync(Guid adminId, CancellationToken cancellationToken) =>
        (await identity.GetCurrentRolesAsync(adminId, cancellationToken)).Contains(Roles.SuperAdmin) &&
        await db.Admins.AnyAsync(admin => admin.Id == adminId, cancellationToken);

    public Task<bool> HasAsync(Guid adminId, string key, CancellationToken cancellationToken) =>
        HasAsync(db, identity, adminId, key, cancellationToken);

    private static async Task<bool> HasAsync(IApplicationDbContext db, IIdentityService identity, Guid adminId,
        string key, CancellationToken cancellationToken)
    {
        if (!AdminPermissions.All.Contains(key))
        {
            return false;
        }

        IReadOnlyCollection<string> roles = await identity.GetCurrentRolesAsync(adminId, cancellationToken);
        if (roles.Contains(Roles.SuperAdmin))
        {
            return await db.Admins.AnyAsync(admin => admin.Id == adminId, cancellationToken);
        }

        return roles.Contains(Roles.Admin) && await db.AdminPermissionGrants
            .AnyAsync(grant => grant.AdminId == adminId && grant.Key == key, cancellationToken);
    }

    public Task EnsureAsync(Guid adminId, string key, CancellationToken cancellationToken) =>
        EnsureAsync(db, identity, adminId, key, cancellationToken);

    public static async Task EnsureAsync(IApplicationDbContext db, IIdentityService identity, Guid adminId,
        string key, CancellationToken cancellationToken)
    {
        if (!await HasAsync(db, identity, adminId, key, cancellationToken))
        {
            throw new ForbiddenException("This action requires a special permission.", "access.permission_required",
                new Dictionary<string, object?> { ["permission"] = key });
        }
    }

    public Task ManageAsync(Guid actorId, Guid? targetId, bool superAdminOnly, Func<CancellationToken, Task> action,
        CancellationToken cancellationToken) => db.ExecuteSerializableAsync(async token =>
    {
        await EnsureAsync(actorId, AdminPermissions.ADMINS_MANAGE, token);
        if ((superAdminOnly || (targetId is Guid target && await IsSuperAdminAsync(target, token))) &&
            !await IsSuperAdminAsync(actorId, token))
        {
            throw new ForbiddenException("Only a super admin can manage a super admin account.");
        }

        await action(token);
    }, cancellationToken);

    public async Task ReplaceAsync(Guid actorId, Guid targetId, ReplaceSpecialPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await ManageAsync(actorId, targetId, false,
                token => ReplaceCoreAsync(targetId, request, token), cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || DatabaseErrors.IsUniqueViolation(ex))
        {
            throw PermissionsChanged();
        }
    }

    private async Task ReplaceCoreAsync(Guid targetId, ReplaceSpecialPermissionsRequest request, CancellationToken token)
    {
        if (request.Keys is null || request.ExpectedKeys is null ||
            request.Keys.Concat(request.ExpectedKeys).Any(key => !AdminPermissions.All.Contains(key)))
        {
            throw new ValidationException("Both permission sets must contain only known permission keys.");
        }

        if (!await db.Admins.AnyAsync(admin => admin.Id == targetId, token))
        {
            throw new NotFoundException("Admin not found.");
        }

        List<AdminPermissionGrant> grants = await db.AdminPermissionGrants
            .Where(grant => grant.AdminId == targetId).ToListAsync(token);
        HashSet<string> current = grants.Select(grant => grant.Key).ToHashSet(StringComparer.Ordinal);
        if (!current.SetEquals(request.ExpectedKeys))
        {
            throw PermissionsChanged();
        }

        HashSet<string> desired = request.Keys.ToHashSet(StringComparer.Ordinal);
        db.AdminPermissionGrants.RemoveRange(grants.Where(grant => !desired.Contains(grant.Key)));
        db.AdminPermissionGrants.AddRange(desired.Except(current)
            .Select(key => new AdminPermissionGrant { AdminId = targetId, Key = key }));
        await db.SaveChangesAsync(token);
    }

    private static ConflictException PermissionsChanged() =>
        new("Permissions were changed by another administrator.", "admin.permissions_changed");
}

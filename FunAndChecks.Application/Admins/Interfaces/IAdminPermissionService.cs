namespace FunAndChecks.Application.Admins.Interfaces;

public interface IAdminPermissionService
{
    Task<bool> IsSuperAdminAsync(Guid adminId, CancellationToken cancellationToken);
    Task<bool> HasAsync(Guid adminId, string key, CancellationToken cancellationToken);
    Task EnsureAsync(Guid adminId, string key, CancellationToken cancellationToken);
    Task ManageAsync(Guid actorId, Guid? targetId, bool superAdminOnly, Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);
    Task ReplaceAsync(Guid actorId, Guid targetId, ReplaceSpecialPermissionsRequest request, CancellationToken cancellationToken);
}

namespace FunAndChecks.Domain.Entities;

public sealed class AdminPermissionGrant
{
    public Guid AdminId { get; set; }
    public required string Key { get; set; }
}

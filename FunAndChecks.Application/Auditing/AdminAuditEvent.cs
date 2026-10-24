namespace FunAndChecks.Application.Auditing;

/// <summary>A safe snapshot of a committed change or an unsuccessful administrative attempt.</summary>
public sealed record AdminAuditEvent(
    DateTimeOffset Timestamp,
    Guid ActorId,
    string ActorName,
    string ActorRole,
    string Action,
    string ObjectType,
    string ObjectId,
    string Outcome,
    string Summary,
    string RequestId,
    IReadOnlyDictionary<string, object?>? Before = null,
    IReadOnlyDictionary<string, object?>? After = null)
{
    public string Kind => "admin_audit";
}

public interface IAdminAuditWriter
{
    /// <summary>Enqueues without blocking. Failure must never change the business operation's result.</summary>
    bool TryWrite(AdminAuditEvent auditEvent);
}

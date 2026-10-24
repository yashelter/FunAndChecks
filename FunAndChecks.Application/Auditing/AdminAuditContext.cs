using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Auditing;

/// <summary>Capture failures are diagnostic data, never business exceptions.</summary>
public static class AdminAuditDiagnostics
{
    private static long _captureFailures;
    public static long CaptureFailures => Interlocked.Read(ref _captureFailures);
    public static void RecordCaptureFailure() => Interlocked.Increment(ref _captureFailures);
}

/// <summary>Request-scoped actor snapshot shared by the HTTP boundary and persistence.</summary>
public sealed class AdminAuditContext(IAdminAuditWriter writer)
{
    private readonly Dictionary<Submission, IReadOnlyDictionary<string, object?>> _submissionBefore = new(ReferenceEqualityComparer.Instance);
    public bool Enabled { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = "";
    public string ActorRole { get; private set; } = "";
    public string Action { get; private set; } = "";
    public string RequestId { get; private set; } = "";
    public int CommittedMutationCount { get; private set; }
    public bool HasCommittedWrites { get; private set; }

    public void Begin(Guid actorId, string actorName, string actorRole, string action, string requestId)
    {
        ActorId = actorId;
        ActorName = actorName;
        ActorRole = actorRole;
        Action = action;
        RequestId = requestId;
        Enabled = true;
    }

    /// <summary>A new history row represents a transition from the latest preceding attempt.</summary>
    public void SetPreviousSubmission(Submission submission, Submission? previous)
    {
        if (!Enabled) return;
        _submissionBefore[submission] = new Dictionary<string, object?>
        {
            ["status"] = (previous?.Status ?? SubmissionStatus.NotSubmitted).ToString(),
            ["comment"] = previous?.Comment,
            ["adminId"] = previous?.AdminId,
            ["submittedAt"] = previous?.SubmittedAt,
            ["previousSubmissionId"] = previous?.Id,
        };
    }

    public IReadOnlyDictionary<string, object?>? GetPreviousSubmission(Submission submission) =>
        _submissionBefore.GetValueOrDefault(submission);

    public AdminAuditEvent Create(string objectType, string objectId, string outcome, string summary,
        IReadOnlyDictionary<string, object?>? before = null, IReadOnlyDictionary<string, object?>? after = null) =>
        new(DateTimeOffset.UtcNow, ActorId, ActorName, ActorRole, Action, objectType, objectId,
            outcome, summary, RequestId, before, after);

    public void Committed(AdminAuditEvent auditEvent)
    {
        HasCommittedWrites = true;
        CommittedMutationCount++;
        Publish(auditEvent);
    }

    public void MarkCommittedWrites() => HasCommittedWrites = true;

    public void Publish(AdminAuditEvent auditEvent)
    {
        if (!Enabled) return;
        try { writer.TryWrite(auditEvent); }
        catch { AdminAuditDiagnostics.RecordCaptureFailure(); }
    }
}

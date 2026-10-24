namespace FunAndChecks.Domain.Entities;

/// <summary>Жалоба на сообщение для модераторов.</summary>
public class ForumReport
{
    public int Id { get; set; }
    public long PostId { get; set; }
    public ForumPost Post { get; set; } = null!;
    public Guid? ReporterId { get; set; }
    public required string ReporterName { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid? ResolvedById { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

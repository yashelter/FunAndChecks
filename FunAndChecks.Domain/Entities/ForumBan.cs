namespace FunAndChecks.Domain.Entities;

/// <summary>Временный запрет писать на форуме. Читать пользователь продолжает.</summary>
public class ForumBan
{
    public Guid UserId { get; set; }
    public DateTime UntilUtc { get; set; }
    public required string Reason { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

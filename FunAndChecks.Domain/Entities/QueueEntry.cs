using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Запись студента в очереди на сдачу.
/// </summary>
public class QueueEntry
{
    public int Id { get; set; }
    public DateTime JoinedAt { get; set; }
    public QueueEntryStatus Status { get; set; }

    public int QueueEventId { get; set; }
    public QueueEvent QueueEvent { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>Админ, который сейчас проверяет этого студента (если проверяет).</summary>
    public Guid? CurrentAdminId { get; set; }
    public Admin? CurrentAdmin { get; set; }
}

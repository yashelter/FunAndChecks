using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Запись студента в очереди на сдачу — один круг. Сдавший или пропущенный студент может встать снова:
/// прошлые круги остаются, а активная запись (ожидает или проверяется) у студента всегда одна.
/// </summary>
public class QueueEntry
{
    public int Id { get; set; }
    /// <summary>Время вступления в этот круг (для повторного круга — время повторного вступления).</summary>
    public DateTime JoinedAt { get; set; }
    public QueueEntryStatus Status { get; set; }
    /// <summary>Номер круга: 1 — первое вступление, больше 1 — повторное.</summary>
    public int Round { get; set; } = 1;

    public int QueueEventId { get; set; }
    public QueueEvent QueueEvent { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>Админ, который сейчас проверяет этого студента (если проверяет).</summary>
    public Guid? CurrentAdminId { get; set; }
    public Admin? CurrentAdmin { get; set; }
}

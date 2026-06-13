using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Попытка сдачи задания. Таблица хранит полную историю всех попыток;
/// актуальный статус задания — статус последней по времени попытки.
/// </summary>
public class Submission
{
    public int Id { get; set; }
    public SubmissionStatus Status { get; set; }
    public string? Comment { get; set; }
    public DateTime SubmittedAt { get; set; }

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public int TaskId { get; set; }
    public CourseTask Task { get; set; } = null!;

    /// <summary>Админ, принимавший сдачу.</summary>
    public Guid AdminId { get; set; }
    public Admin Admin { get; set; } = null!;

    /// <summary>Необязательная привязка к событию очереди, в рамках которого была сдача.</summary>
    public int? QueueEventId { get; set; }
    public QueueEvent? QueueEvent { get; set; }
}

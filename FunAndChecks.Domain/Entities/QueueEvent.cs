namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Событие очереди (день сдачи по предмету).
/// </summary>
public class QueueEvent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime EventDateTime { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public ICollection<QueueEntry> Participants { get; set; } = new List<QueueEntry>();
}

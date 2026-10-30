namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Событие очереди (день сдачи по предмету).
/// </summary>
public class QueueEvent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime EventDateTime { get; set; }

    /// <summary>
    /// Могут ли студенты записываться сами (и вставать повторно). Преподаватель меняет флаг в любой момент,
    /// независимо от того, заполнялась ли очередь автоматически.
    /// </summary>
    public bool AllowSelfJoin { get; set; } = true;

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public ICollection<QueueEntry> Participants { get; set; } = new List<QueueEntry>();

    /// <summary>Группы автозаписи: их подтвердившиеся позже студенты попадают в очередь сами.</summary>
    public ICollection<Group> AutoFillGroups { get; set; } = new List<Group>();
}

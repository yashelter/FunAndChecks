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
    /// Могут ли студенты записываться сами. Закрытая запись не пускает только новых: у кого уже есть круг,
    /// встаёт повторно и при закрытой. Преподаватель меняет флаг в любой момент,
    /// независимо от того, заполнялась ли очередь автоматически.
    /// </summary>
    public bool AllowSelfJoin { get; set; } = true;

    /// <summary>
    /// Через сколько минут после начала (<see cref="EventDateTime"/>) записавшийся считается опоздавшим;
    /// null — опоздания не отмечаются. Опоздавшие на первом круге ждут после пришедших вовремя.
    /// </summary>
    public int? LateAfterMinutes { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public ICollection<QueueEntry> Participants { get; set; } = new List<QueueEntry>();

    /// <summary>Группы автозаписи: их подтвердившиеся позже студенты попадают в очередь сами.</summary>
    public ICollection<Group> AutoFillGroups { get; set; } = new List<Group>();
}

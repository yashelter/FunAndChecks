namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Связь "многие ко многим": какой группе доступен какой предмет.
/// </summary>
public class GroupSubject
{
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
}

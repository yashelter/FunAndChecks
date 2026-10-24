namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Задание по предмету.
/// </summary>
public class CourseTask
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int MaxPoints { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    /// <summary>Группа заданий; null — задание без группы.</summary>
    public int? TaskGroupId { get; set; }
    public TaskGroup? TaskGroup { get; set; }

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
}

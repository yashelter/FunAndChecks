namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Студент — сдаёт задания и стоит в очередях.
/// </summary>
public class Student : User
{
    public string? GitHubUrl { get; set; }

    /// <summary>
    /// Личный цвет студента в hex-формате (#RRGGBB).
    /// Не связан с цветом админа и по умолчанию никак не влияет на отображение.
    /// </summary>
    public string? Color { get; set; }

    public int? GroupId { get; set; }
    public Group? Group { get; set; }

    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
    public ICollection<QueueEntry> QueueEntries { get; set; } = new List<QueueEntry>();
    public ICollection<StudentGrade> Grades { get; set; } = new List<StudentGrade>();
}

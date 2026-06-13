namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Учебный предмет (дисциплина).
/// </summary>
public class Subject
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<CourseTask> Tasks { get; set; } = new List<CourseTask>();
    public ICollection<GradeComponent> GradeComponents { get; set; } = new List<GradeComponent>();
    public ICollection<GroupSubject> GroupSubjects { get; set; } = new List<GroupSubject>();
}

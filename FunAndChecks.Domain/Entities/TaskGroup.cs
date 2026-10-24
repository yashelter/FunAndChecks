namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Необязательная группа заданий внутри предмета (например, «Лабораторные», «Модуль 1»).
/// В окне проверки задания группы показываются свёрнутыми.
/// </summary>
public class TaskGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public ICollection<CourseTask> Tasks { get; set; } = new List<CourseTask>();
}

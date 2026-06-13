namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Глобальная оценочная колонка предмета — например, «Билет» или «Курсовая».
/// В отличие от задач (выполнил/не выполнил) оценивается баллами.
/// </summary>
public class GradeComponent
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int MaxPoints { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public ICollection<StudentGrade> Grades { get; set; } = new List<StudentGrade>();
}

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Глобальная оценочная колонка предмета — например, «Билет» или «Курсовая».
/// В отличие от задач (выполнил/не выполнил) оценивается баллами.
/// </summary>
public class GradeComponent
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Минимально допустимый балл (например, 2 для шкалы 2–5). По умолчанию 0.</summary>
    public int MinPoints { get; set; }

    /// <summary>Максимально допустимый балл (например, 5 для шкалы 2–5).</summary>
    public int MaxPoints { get; set; }

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public ICollection<StudentGrade> Grades { get; set; } = new List<StudentGrade>();
}

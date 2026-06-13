namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Баллы студента за оценочную колонку предмета (билет, курсовую и т.п.).
/// На пару (колонка, студент) существует не больше одной записи.
/// </summary>
public class StudentGrade
{
    public int Id { get; set; }
    public int Points { get; set; }
    public string? Comment { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int GradeComponentId { get; set; }
    public GradeComponent GradeComponent { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>Админ, выставивший (последним обновивший) оценку.</summary>
    public Guid AdminId { get; set; }
    public Admin Admin { get; set; } = null!;
}

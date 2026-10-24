namespace FunAndChecks.Domain.Entities;

/// <summary>Одна строка ответа. Для множественного выбора — по строке на вариант; «Другое» — OptionId = null и Text.</summary>
public class SurveyAnswer
{
    public int Id { get; set; }
    public int ResponseId { get; set; }
    public int QuestionId { get; set; }
    public int? OptionId { get; set; }
    public string? Text { get; set; }
    public decimal? Number { get; set; }
}

using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

public class SurveyQuestion
{
    public int Id { get; set; }
    public int SurveyId { get; set; }
    public int Order { get; set; }
    public required string Text { get; set; }
    public string? Description { get; set; }
    public SurveyQuestionType Type { get; set; }
    public bool IsRequired { get; set; }
    /// <summary>Для вопросов с выбором: можно вписать свой вариант.</summary>
    public bool AllowOther { get; set; }
    public int? MaxLength { get; set; }
    public int? ScaleMin { get; set; }
    public int? ScaleMax { get; set; }
    public string? ScaleMinLabel { get; set; }
    public string? ScaleMaxLabel { get; set; }

    public ICollection<SurveyOption> Options { get; set; } = new List<SurveyOption>();
}

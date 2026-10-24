namespace FunAndChecks.Domain.Entities;

public class SurveyResponse
{
    public int Id { get; set; }
    public int SurveyId { get; set; }
    /// <summary>null для анонимных опросов.</summary>
    public Guid? RespondentId { get; set; }
    /// <summary>Группа студента на момент ответа — для разбивки итогов по группам (с тем же порогом анонимности).</summary>
    public int? GroupId { get; set; }
    /// <summary>Для анонимных опросов округляется до дня, чтобы время не выдавало респондента.</summary>
    public DateTime SubmittedAtUtc { get; set; }

    public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
}

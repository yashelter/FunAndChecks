using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Опрос (упрощённая форма). После публикации вопросы заморожены, чтобы ответы оставались сопоставимыми.
/// </summary>
public class Survey
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public int? SubjectId { get; set; }
    public Subject? Subject { get; set; }
    public Guid? CreatedById { get; set; }
    public required string CreatedByName { get; set; }
    public SurveyStatus Status { get; set; }
    /// <summary>Ответы не связываются с респондентом; кто прошёл — хранится отдельно.</summary>
    public bool IsAnonymous { get; set; }
    public bool AllowSnooze { get; set; }
    public int SnoozeHours { get; set; } = 24;
    public int? MaxSnoozes { get; set; }
    public DateTime? OpensAtUtc { get; set; }
    public DateTime? ClosesAtUtc { get; set; }
    public SurveyTriggerType TriggerType { get; set; }
    public int TriggerThreshold { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    public ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();
    /// <summary>Пусто — все группы предмета (или все студенты для опроса без предмета).</summary>
    public ICollection<SurveyTargetGroup> TargetGroups { get; set; } = new List<SurveyTargetGroup>();
}

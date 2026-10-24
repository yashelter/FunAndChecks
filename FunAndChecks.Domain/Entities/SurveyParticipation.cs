using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>Кто прошёл или отложил опрос. Не ссылается на ответ — так сохраняется анонимность.</summary>
public class SurveyParticipation
{
    public int SurveyId { get; set; }
    public Guid UserId { get; set; }
    public SurveyParticipationStatus Status { get; set; }
    public int SnoozeCount { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

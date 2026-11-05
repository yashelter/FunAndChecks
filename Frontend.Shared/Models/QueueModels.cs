namespace Frontend.Shared.Models;

using Frontend.Shared.Profiles;

public record QueueEventDto(int Id, string Name, DateTime EventDateTime, bool AllowSelfJoin,
    string? SubjectName = null, QueueEntryStatus? MyStatus = null, int? MyPosition = null, int? WaitingCount = null);

/// <summary>
/// Создание события. Если заданы <c>AutoFillGroupIds</c>, в очередь добавляются студенты этих групп
/// (в том числе подтвердившиеся позже). Самозапись задаётся <c>AllowSelfJoin</c> и меняется потом.
/// </summary>
public record CreateQueueEventRequest(
    string Name,
    DateTime EventDateTime,
    int SubjectId,
    bool AllowSelfJoin = true,
    List<int>? AutoFillGroupIds = null,
    int? LateAfterMinutes = null);

/// <summary>Правка события; <c>AllowSelfJoin</c> = null — флаг самозаписи не меняется.</summary>
public record UpdateQueueEventRequest(string Name, DateTime EventDateTime, bool? AllowSelfJoin = null);

public record UpdateQueueStatusRequest(QueueEntryStatus Status);

/// <summary>Порог опоздания в минутах после начала события; null — опоздания не отмечаются.</summary>
public record SetQueueLateRuleRequest(int? LateAfterMinutes);

public record SetLateExcusedRequest(bool Excused);

/// <summary>Событие SignalR об изменении настроек очереди (AllowSelfJoin).</summary>
public record QueueSettingsUpdateDto(int EventId, bool AllowSelfJoin, string? AdminName, Guid? AdminId = null);

public record QueueParticipantDto(
    Guid StudentId,
    string FirstName,
    string LastName,
    string GroupName,
    int TotalPoints,
    QueueEntryStatus Status,
    string? CheckingByAdminName,
    DateTime JoinedAt,
    string? StudentColor,
    bool CanManage = false,
    Guid? CheckingByAdminId = null,
    int EntryId = 0,
    int Round = 1,
    List<PinnedAchievementBadgeDto>? PinnedAchievements = null,
    int? LateMinutes = null,
    bool LateExcused = false)
{
    public string FullName => $"{FirstName} {LastName}";
    /// <summary>Повторный круг: студент встал снова после «сдал» или «пропущен».</summary>
    public bool IsRejoin => Round > 1;
    /// <summary>Записался на первый круг позже порога, и опоздание не снято: ждёт после пришедших вовремя.</summary>
    public bool IsLate => LateMinutes is not null && !LateExcused;
}

public record QueueDetailsDto(
    int EventId,
    string EventName,
    string SubjectName,
    int SubjectId,
    DateTime EventDateTime,
    bool AllowSelfJoin,
    List<QueueParticipantDto> Participants,
    int? LateAfterMinutes = null);

/// <summary>Событие SignalR об изменении записи в очереди.</summary>
public record QueueEntryUpdateDto(int EventId, Guid StudentId, QueueEntryStatus NewStatus, string? AdminName);

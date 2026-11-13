using FunAndChecks.Domain.Enums;
using FunAndChecks.Application.Profiles;

namespace FunAndChecks.Application.Queues;

/// <summary>
/// Событие очереди. Для «моих очередей» студента дополнительно: предмет, статус последнего круга и место среди
/// ожидающих (тот же порядок, что видит преподаватель, см. <see cref="QueueOrder"/>).
/// <c>ArchivedAt</c> — когда очередь вручную завершили и архивировали (null — не архивирована).
/// </summary>
public record QueueEventDto(int Id, string Name, DateTime EventDateTime, bool AllowSelfJoin,
    string? SubjectName = null, QueueEntryStatus? MyStatus = null, int? MyPosition = null, int? WaitingCount = null,
    DateTime? ArchivedAt = null);

/// <summary>
/// Создание события очереди. Если заданы <c>AutoFillGroupIds</c>, в очередь сразу добавляются подтверждённые
/// студенты этих групп, а позже подтвердившиеся — при подтверждении. Самозапись задаётся <c>AllowSelfJoin</c>.
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

/// <summary>Снять (true) или вернуть (false) опоздание записи в очереди.</summary>
public record SetLateExcusedRequest(bool Excused);

/// <summary>Событие для подписчиков SignalR об изменении настроек очереди (AllowSelfJoin).</summary>
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
    bool CanManage,
    Guid? CheckingByAdminId = null,
    int EntryId = 0,
    int Round = 1,
    ActiveAchievementDto? ActiveAchievement = null,
    int? LateMinutes = null,
    bool LateExcused = false);

public record QueueDetailsDto(
    int EventId,
    string EventName,
    string SubjectName,
    int SubjectId,
    DateTime EventDateTime,
    bool AllowSelfJoin,
    List<QueueParticipantDto> Participants,
    int? LateAfterMinutes = null,
    DateTime? ArchivedAt = null);

/// <summary>Событие для подписчиков SignalR об изменении записи в очереди.</summary>
public record QueueEntryUpdateDto(int EventId, Guid StudentId, QueueEntryStatus NewStatus, string? AdminName);

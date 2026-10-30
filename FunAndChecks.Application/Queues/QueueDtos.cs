using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

/// <summary>
/// Событие очереди. Для «моих очередей» студента дополнительно: предмет, статус последнего круга и место среди
/// ожидающих (тот же порядок, что видит преподаватель, см. <see cref="QueueOrder"/>).
/// </summary>
public record QueueEventDto(int Id, string Name, DateTime EventDateTime, bool AllowSelfJoin,
    string? SubjectName = null, QueueEntryStatus? MyStatus = null, int? MyPosition = null, int? WaitingCount = null);

/// <summary>
/// Создание события очереди. Если заданы <c>AutoFillGroupIds</c>, в очередь сразу добавляются подтверждённые
/// студенты этих групп, а позже подтвердившиеся — при подтверждении. Самозапись задаётся <c>AllowSelfJoin</c>.
/// </summary>
public record CreateQueueEventRequest(
    string Name,
    DateTime EventDateTime,
    int SubjectId,
    bool AllowSelfJoin = true,
    List<int>? AutoFillGroupIds = null);

/// <summary>Правка события; <c>AllowSelfJoin</c> = null — флаг самозаписи не меняется.</summary>
public record UpdateQueueEventRequest(string Name, DateTime EventDateTime, bool? AllowSelfJoin = null);

public record UpdateQueueStatusRequest(QueueEntryStatus Status);

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
    int Round = 1);

public record QueueDetailsDto(
    int EventId,
    string EventName,
    string SubjectName,
    int SubjectId,
    DateTime EventDateTime,
    bool AllowSelfJoin,
    List<QueueParticipantDto> Participants);

/// <summary>Событие для подписчиков SignalR об изменении записи в очереди.</summary>
public record QueueEntryUpdateDto(int EventId, Guid StudentId, QueueEntryStatus NewStatus, string? AdminName);

using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

public record QueueEventDto(int Id, string Name, DateTime EventDateTime, bool AllowSelfJoin);

/// <summary>
/// Создание события очереди. Если задан <see cref="AutoFillGroupId"/>, в очередь сразу
/// добавляются все студенты этой группы, а самостоятельная запись отключается.
/// </summary>
public record CreateQueueEventRequest(
    string Name,
    DateTime EventDateTime,
    int SubjectId,
    bool AllowSelfJoin = true,
    int? AutoFillGroupId = null);

public record UpdateQueueEventRequest(string Name, DateTime EventDateTime);

public record UpdateQueueStatusRequest(QueueEntryStatus Status);

public record QueueParticipantDto(
    Guid StudentId,
    string FirstName,
    string LastName,
    string GroupName,
    int TotalPoints,
    QueueEntryStatus Status,
    string? CheckingByAdminName,
    DateTime JoinedAt);

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

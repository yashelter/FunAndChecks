namespace Frontend.Shared.Models;

public record QueueEventDto(int Id, string Name, DateTime EventDateTime, bool AllowSelfJoin);

/// <summary>
/// Создание события. Если задан <see cref="AutoFillGroupId"/>, в очередь добавляются все студенты
/// группы и самозапись отключается.
/// </summary>
public record CreateQueueEventRequest(
    string Name,
    DateTime EventDateTime,
    int SubjectId,
    bool AllowSelfJoin = true,
    List<int>? AutoFillGroupIds = null);

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
    DateTime JoinedAt,
    string? StudentColor)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record QueueDetailsDto(
    int EventId,
    string EventName,
    string SubjectName,
    int SubjectId,
    DateTime EventDateTime,
    bool AllowSelfJoin,
    List<QueueParticipantDto> Participants);

/// <summary>Событие SignalR об изменении записи в очереди.</summary>
public record QueueEntryUpdateDto(int EventId, Guid StudentId, QueueEntryStatus NewStatus, string? AdminName);

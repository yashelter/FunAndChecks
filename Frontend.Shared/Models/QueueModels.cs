namespace Frontend.Shared.Models;

public record QueueEventDto(int Id, string Name, DateTime EventDateTime);

public record CreateQueueEventRequest(string Name, DateTime EventDateTime, int SubjectId);

public record UpdateQueueStatusRequest(QueueEntryStatus Status);

public record QueueParticipantDto(
    Guid StudentId,
    string FirstName,
    string LastName,
    string GroupName,
    int TotalPoints,
    QueueEntryStatus Status,
    string? CheckingByAdminName)
{
    public string FullName => $"{FirstName} {LastName}";
}

public record QueueDetailsDto(
    int EventId,
    string EventName,
    string SubjectName,
    int SubjectId,
    DateTime EventDateTime,
    List<QueueParticipantDto> Participants);

/// <summary>Событие SignalR об изменении записи в очереди.</summary>
public record QueueEntryUpdateDto(int EventId, Guid StudentId, QueueEntryStatus NewStatus, string? AdminName);

namespace Frontend.Shared.Models;

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
    bool CanManage = false,
    Guid? CheckingByAdminId = null,
    int EntryId = 0,
    int Round = 1)
{
    public string FullName => $"{FirstName} {LastName}";
    /// <summary>Повторный круг: студент встал снова после «сдал» или «пропущен».</summary>
    public bool IsRejoin => Round > 1;
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

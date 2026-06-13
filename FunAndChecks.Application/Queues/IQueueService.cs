using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

public interface IQueueService
{
    /// <summary>События, чья дата не истекла больше чем на 2 дня.</summary>
    Task<List<QueueEventDto>> GetActiveEventsAsync(CancellationToken cancellationToken = default);

    Task<List<QueueEventDto>> GetAllEventsAsync(CancellationToken cancellationToken = default);

    Task<QueueDetailsDto> GetDetailsAsync(int eventId, CancellationToken cancellationToken = default);

    Task<QueueEventDto> CreateEventAsync(CreateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task<QueueEventDto> UpdateEventAsync(int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task DeleteEventAsync(int eventId, CancellationToken cancellationToken = default);

    Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default);
}

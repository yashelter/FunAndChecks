using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

public interface IQueueService
{
    /// <summary>События, чья дата не истекла больше чем на 24 часа.</summary>
    Task<List<QueueEventDto>> GetActiveEventsAsync(CancellationToken cancellationToken = default);

    Task<List<QueueEventDto>> GetAllEventsAsync(CancellationToken cancellationToken = default);

    Task<QueueDetailsDto> GetDetailsAsync(int eventId, CancellationToken cancellationToken = default);

    Task<QueueEventDto> CreateEventAsync(Guid adminId, CreateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task<QueueEventDto> UpdateEventAsync(Guid adminId, int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task DeleteEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default);
}

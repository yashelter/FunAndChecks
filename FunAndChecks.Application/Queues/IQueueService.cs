using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

public interface IQueueService
{
    /// <summary>Активные события с учётом запретов предметов для админа.</summary>
    Task<List<QueueEventDto>> GetActiveEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default);

    Task<List<QueueEventDto>> GetAllEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default);

    Task<QueueDetailsDto> GetDetailsAsync(int eventId, Guid? adminId = null, CancellationToken cancellationToken = default);

    Task EnsureEventAllowedAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task<QueueEventDto> CreateEventAsync(Guid adminId, CreateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task<QueueEventDto> UpdateEventAsync(Guid adminId, int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task DeleteEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default);
}

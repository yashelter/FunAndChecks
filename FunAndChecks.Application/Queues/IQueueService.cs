using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

public interface IQueueService
{
    /// <summary>События за последние 24 часа и будущие с учётом запретов предметов для админа.</summary>
    Task<List<QueueEventDto>> GetActiveEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default);

    Task<List<QueueEventDto>> GetAllEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default);

    Task<QueueDetailsDto> GetDetailsAsync(int eventId, Guid? adminId = null, CancellationToken cancellationToken = default);

    Task EnsureEventAllowedAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task<QueueEventDto> CreateEventAsync(Guid adminId, CreateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task<QueueEventDto> UpdateEventAsync(Guid adminId, int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task DeleteEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Студент сам выходит из очереди, пока его не взяли на проверку.</summary>
    Task LeaveAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Очереди, где записан студент, — со статусом и местом среди ожидающих.</summary>
    Task<List<QueueEventDto>> GetMyEventsAsync(Guid studentId, bool includePast, CancellationToken cancellationToken = default);

    Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default);
}

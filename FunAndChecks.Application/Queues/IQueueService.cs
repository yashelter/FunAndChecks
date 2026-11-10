using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

public interface IQueueService
{
    /// <summary>Текущие события (<see cref="QueueVisibility.Current"/>) с учётом запретов предметов для админа.</summary>
    Task<List<QueueEventDto>> GetActiveEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default);

    Task<List<QueueEventDto>> GetAllEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default);

    /// <summary>События выборки <paramref name="scope"/> (текущие, архив или все) в доступных админу предметах.</summary>
    Task<List<QueueEventDto>> GetEventsAsync(Guid? adminId, QueueListScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Завершить и архивировать очередь: она уходит из текущих списков и становится только для просмотра.
    /// Нельзя, пока кого-то проверяют (queue.archive_checking). Повторный вызов ничего не меняет.
    /// </summary>
    Task<QueueEventDto> ArchiveEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task<QueueDetailsDto> GetDetailsAsync(int eventId, Guid? adminId = null, CancellationToken cancellationToken = default);

    Task EnsureEventAllowedAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task<QueueEventDto> CreateEventAsync(Guid adminId, CreateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task<QueueEventDto> UpdateEventAsync(Guid adminId, int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default);

    Task DeleteEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default);

    Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Студент сам выходит из очереди, пока его не взяли на проверку.</summary>
    Task LeaveAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Очереди выборки <paramref name="scope"/>, где записан студент, — со статусом и местом среди ожидающих.</summary>
    Task<List<QueueEventDto>> GetMyEventsAsync(Guid studentId, QueueListScope scope, CancellationToken cancellationToken = default);

    /// <summary>Порог опоздания события; клиенты перечитывают очередь (порядок мог измениться).</summary>
    Task SetLateRuleAsync(Guid adminId, int eventId, SetQueueLateRuleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Преподаватель снимает или возвращает опоздание записи (уважительная причина).</summary>
    Task SetLateExcusedAsync(Guid adminId, int eventId, int entryId, bool excused, CancellationToken cancellationToken = default);

    Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default);
}

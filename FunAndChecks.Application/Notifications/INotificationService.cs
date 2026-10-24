namespace FunAndChecks.Application.Notifications;

public interface INotificationService
{
    /// <summary>Сохраняет и рассылает уведомления. Ошибки не пробрасываются — основное действие уже выполнено.</summary>
    Task NotifyAsync(IEnumerable<Guid> userIds, NotificationRequest request, CancellationToken ct = default);
    Task<NotificationPageDto> ListAsync(Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task MarkReadAsync(Guid userId, long id, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Убирает прочитанное уведомление; непрочитанное убрать нельзя («notification.unread»).</summary>
    Task DeleteAsync(Guid userId, long id, CancellationToken ct = default);
    /// <summary>Убирает все прочитанные уведомления пользователя; возвращает, сколько убрано.</summary>
    Task<int> DeleteReadAsync(Guid userId, CancellationToken ct = default);
}

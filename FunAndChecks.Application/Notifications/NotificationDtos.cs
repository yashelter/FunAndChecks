using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Notifications;

public record NotificationDto(long Id, NotificationType Type, string Subject, string? ActorName, string? Link,
    DateTime CreatedAtUtc, bool IsRead);

public record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int TotalCount, int UnreadCount, int Page, int PageSize);

/// <summary>Что отправить. Ссылка — путь внутри кабинета без префикса /admin или /student (например, "forum/12").</summary>
public record NotificationRequest(NotificationType Type, string Subject, string? ActorName, string? Link);

using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Персональное уведомление. Текст собирает клиент по <see cref="Type"/>,
/// сервер хранит только подставляемые значения и ссылку.
/// </summary>
public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public required string Subject { get; set; }
    public string? ActorName { get; set; }
    public string? Link { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

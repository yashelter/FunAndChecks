using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Notifications;

public class NotificationService(IApplicationDbContext db, IUserNotifier notifier, ILogger<NotificationService> logger)
    : INotificationService
{
    /// <summary>Уведомления не копятся бесконечно: храним последние на пользователя.</summary>
    internal const int KeepPerUser = 200;

    public async Task NotifyAsync(IEnumerable<Guid> userIds, NotificationRequest request, CancellationToken ct = default)
    {
        var recipients = userIds.Distinct().ToList();
        if (recipients.Count == 0) return;
        List<Notification> created;
        try
        {
            var existing = await db.Students.Select(s => s.Id).Where(id => recipients.Contains(id))
                .Union(db.Admins.Select(a => a.Id).Where(id => recipients.Contains(id))).ToListAsync(ct);
            var now = DateTime.UtcNow;
            created = existing.Select(userId => new Notification
            {
                UserId = userId, Type = request.Type, Subject = Truncate(request.Subject, 300)!,
                ActorName = Truncate(request.ActorName, 201), Link = Truncate(request.Link, 300), CreatedAtUtc = now,
            }).ToList();
            db.Notifications.AddRange(created);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to store {Type} notifications for {Count} users.", request.Type, recipients.Count);
            return;
        }

        foreach (var notification in created)
        {
            try { await notifier.NotificationCreatedAsync(notification.UserId, ToDto(notification), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification {Id} was stored, but realtime delivery failed.", notification.Id);
            }
        }
    }

    public async Task<NotificationPageDto> ListAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        if (page == 1) await TrimAsync(userId, ct);
        var mine = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        var total = await mine.CountAsync(ct);
        var unread = await mine.CountAsync(n => n.ReadAtUtc == null, ct);
        var items = await mine.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items.Select(ToDto).ToList(), total, unread, page, pageSize);
    }

    public async Task MarkReadAsync(Guid userId, long id, CancellationToken ct = default)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new NotFoundException("Notification not found.", "notification.not_found");
        notification.ReadAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var unread = await db.Notifications.Where(n => n.UserId == userId && n.ReadAtUtc == null).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var notification in unread) notification.ReadAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid userId, long id, CancellationToken ct = default)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new NotFoundException("Notification not found.", "notification.not_found");
        // Непрочитанное сначала открывают или отмечают прочитанным — иначе его легко смахнуть не глядя.
        if (notification.ReadAtUtc is null)
            throw new ConflictException("Only read notifications can be removed.", "notification.unread");
        db.Notifications.Remove(notification);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteReadAsync(Guid userId, CancellationToken ct = default)
    {
        var read = await db.Notifications.Where(n => n.UserId == userId && n.ReadAtUtc != null).ToListAsync(ct);
        db.Notifications.RemoveRange(read);
        await db.SaveChangesAsync(ct);
        return read.Count;
    }

    private async Task TrimAsync(Guid userId, CancellationToken ct)
    {
        var stale = await db.Notifications.Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id).Skip(KeepPerUser).ToListAsync(ct);
        if (stale.Count == 0) return;
        db.Notifications.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
    }

    internal static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.Subject, n.ActorName, n.Link, n.CreatedAtUtc, n.ReadAtUtc.HasValue);

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}

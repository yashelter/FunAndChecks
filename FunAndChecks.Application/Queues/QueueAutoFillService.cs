using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Queues;

/// <summary>
/// Дописывает студента в очереди с автозаписью его группы, если он подтвердился или попал в группу
/// уже после создания очереди.
/// </summary>
public interface IQueueAutoFillService
{
    /// <summary>
    /// Добавляет активного студента в ещё не прошедшие авто-очереди его текущей группы, где его нет.
    /// Никогда не бросает исключение: вызывается после основной операции (подтверждение почты, правка студента).
    /// </summary>
    Task EnrollAsync(Guid studentId, CancellationToken cancellationToken = default);
}

public class QueueAutoFillService(
    IApplicationDbContext db,
    IQueueNotifier queueNotifier,
    ILogger<QueueAutoFillService> logger) : IQueueAutoFillService
{
    /// <summary>Тот же запас, что у списка активных очередей: событие актуально ещё сутки после начала.</summary>
    private static readonly TimeSpan ActiveEventGracePeriod = TimeSpan.FromDays(1);

    public async Task EnrollAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        List<int> eventIds = [];
        try
        {
            await db.ExecuteSerializableAsync(async ct =>
            {
                eventIds = [];
                var student = await db.Students.AsNoTracking()
                    .Where(s => s.Id == studentId && s.IsActive && s.GroupId != null)
                    .Select(s => new { s.GroupId })
                    .FirstOrDefaultAsync(ct);
                if (student is null) return;

                var threshold = DateTime.UtcNow - ActiveEventGracePeriod;
                eventIds = await db.QueueEvents
                    .Where(qe => qe.EventDateTime > threshold
                                 && qe.AutoFillGroups.Any(g => g.Id == student.GroupId)
                                 && !qe.Participants.Any(p => p.StudentId == studentId))
                    .Select(qe => qe.Id)
                    .ToListAsync(ct);
                if (eventIds.Count == 0) return;

                var now = DateTime.UtcNow;
                db.QueueEntries.AddRange(eventIds.Select(eventId => new QueueEntry
                {
                    QueueEventId = eventId,
                    StudentId = studentId,
                    JoinedAt = now,
                    Status = QueueEntryStatus.Waiting,
                }));
                await db.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add student {StudentId} to auto-filled queues.", studentId);
            return;
        }

        foreach (var eventId in eventIds)
        {
            try
            {
                await queueNotifier.QueueEntryUpdatedAsync(
                    new QueueEntryUpdateDto(eventId, studentId, QueueEntryStatus.Waiting, null), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Student added to auto-filled queue {EventId}, but notification failed.", eventId);
            }
        }
    }
}

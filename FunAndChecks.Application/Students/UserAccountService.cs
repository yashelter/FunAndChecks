using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Students;

public class UserAccountService(IApplicationDbContext db, IIdentityService identity,
    IResultsCacheService cache, IQueueNotifier queueNotifier, ILogger<UserAccountService> logger) : IUserAccountService
{
    public Task<UserAccountPageDto> GetAsync(Guid actingAdminId, string? query, int page, int pageSize,
        CancellationToken cancellationToken = default) =>
        identity.GetNonAdminAccountsAsync(actingAdminId, query, page, pageSize, cancellationToken);

    public async Task DeleteAsync(Guid actingAdminId, Guid userId, CancellationToken cancellationToken = default)
    {
        List<int> affectedSubjects = [];
        List<int> affectedQueueEvents = [];
        await db.ExecuteSerializableAsync(async ct =>
        {
            var groupId = await db.Students.Where(s => s.Id == userId).Select(s => s.GroupId).FirstOrDefaultAsync(ct);
            affectedSubjects = await db.GroupSubjects.Where(gs => groupId != null && gs.GroupId == groupId)
                .Select(gs => gs.SubjectId)
                .Concat(db.Submissions.Where(s => s.StudentId == userId).Select(s => s.Task.SubjectId))
                .Concat(db.StudentGrades.Where(g => g.StudentId == userId).Select(g => g.GradeComponent.SubjectId))
                .Distinct().ToListAsync(ct);
            affectedQueueEvents = await db.QueueEntries.Where(e => e.StudentId == userId)
                .Select(e => e.QueueEventId).Distinct().ToListAsync(ct);

            // Identity is the principal FK: its deletion also removes the profile, submissions,
            // grades, queue entries, attendance records and refresh tokens within this transaction.
            await identity.DeleteNonAdminAccountAsync(actingAdminId, userId, ct);
        }, cancellationToken);

        foreach (var subjectId in affectedSubjects)
        {
            cache.Invalidate(subjectId);
        }
        foreach (var eventId in affectedQueueEvents)
        {
            try
            {
                // Existing clients reload the complete queue on this event, including legacy.
                // The finished hint closes the deleted entry without adding a second wire protocol.
                await queueNotifier.QueueEntryUpdatedAsync(
                    new QueueEntryUpdateDto(eventId, userId, QueueEntryStatus.Finished, null), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Account deleted, but queue notification failed for event {EventId}.", eventId);
            }
        }
    }
}

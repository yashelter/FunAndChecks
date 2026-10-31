using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Statistics;

public class ReviewerStatisticsService(IApplicationDbContext db) : IReviewerStatisticsService
{
    public async Task<List<ReviewerStatsDto>> GetAsync(int? subjectId, CancellationToken cancellationToken = default)
    {
        if (subjectId is int id && !await db.Subjects.AnyAsync(s => s.Id == id, cancellationToken))
            throw new NotFoundException($"Subject with ID {id} not found.", "subject.not_found");

        // Попытки — все, включая повторные по одной задаче: отменённые (undo) удалены и сюда не попадают.
        var submissions = db.Submissions.AsQueryable();
        if (subjectId is int submissionsSubject)
            submissions = submissions.Where(s => s.Task.SubjectId == submissionsSubject);
        var attempts = await submissions
            .GroupBy(s => s.AdminId)
            .Select(g => new
            {
                AdminId = g.Key,
                Accepted = g.Count(s => s.Status == SubmissionStatus.Accepted),
                Rejected = g.Count(s => s.Status == SubmissionStatus.Rejected),
            })
            .ToDictionaryAsync(a => a.AdminId, cancellationToken);

        // Завершённые в очереди — разные студенты: один студент на двух сдачах или кругах считается один раз.
        var finishedEntries = db.QueueEntries.Where(e => e.Status == QueueEntryStatus.Finished && e.FinishedByAdminId != null);
        if (subjectId is int queueSubject)
            finishedEntries = finishedEntries.Where(e => e.QueueEvent.SubjectId == queueSubject);
        var finished = await finishedEntries
            .Select(e => new { AdminId = e.FinishedByAdminId!.Value, e.StudentId })
            .Distinct()
            .GroupBy(e => e.AdminId)
            .Select(g => new { AdminId = g.Key, Students = g.Count() })
            .ToDictionaryAsync(f => f.AdminId, f => f.Students, cancellationToken);

        // Все проверяющие, и с нулями: видно, кто не проверял вовсе.
        var admins = await db.Admins.AsNoTracking()
            .Select(a => new { a.Id, a.FirstName, a.LastName, a.Color, a.Letter })
            .ToListAsync(cancellationToken);

        return admins
            .Select(a =>
            {
                var counts = attempts.GetValueOrDefault(a.Id);
                return new ReviewerStatsDto(a.Id, $"{a.LastName} {a.FirstName}", a.Color, a.Letter,
                    counts?.Accepted ?? 0, counts?.Rejected ?? 0, finished.GetValueOrDefault(a.Id));
            })
            .OrderByDescending(s => s.Accepted + s.Rejected)
            .ThenBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

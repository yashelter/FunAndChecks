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

    public async Task<List<AdminActivityStatsDto>> GetActivityAsync(
        int? subjectId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        if (subjectId is int id && !await db.Subjects.AnyAsync(s => s.Id == id, cancellationToken))
            throw new NotFoundException($"Subject with ID {id} not found.", "subject.not_found");

        var submissions = db.Submissions.AsQueryable();
        if (subjectId is int subSubject)
            submissions = submissions.Where(s => s.Task.SubjectId == subSubject);
        if (fromUtc is DateTime from1) submissions = submissions.Where(s => s.SubmittedAt >= from1);
        if (toUtc is DateTime to1) submissions = submissions.Where(s => s.SubmittedAt <= to1);
        var submissionStats = await submissions
            .GroupBy(s => s.AdminId)
            .Select(g => new
            {
                AdminId = g.Key,
                Accepted = g.Count(s => s.Status == SubmissionStatus.Accepted),
                Rejected = g.Count(s => s.Status == SubmissionStatus.Rejected),
                LastAt = g.Max(s => s.SubmittedAt)
            })
            .ToDictionaryAsync(x => x.AdminId, cancellationToken);

        var queueEntries = db.QueueEntries
            .Where(e => e.Status == QueueEntryStatus.Finished && e.FinishedByAdminId != null);
        if (subjectId is int qSubject)
            queueEntries = queueEntries.Where(e => e.QueueEvent.SubjectId == qSubject);
        if (fromUtc is DateTime from2) queueEntries = queueEntries.Where(e => e.JoinedAt >= from2);
        if (toUtc is DateTime to2) queueEntries = queueEntries.Where(e => e.JoinedAt <= to2);
        var queueFinished = await queueEntries
            .GroupBy(e => e.FinishedByAdminId!.Value)
            .Select(g => new
            {
                AdminId = g.Key,
                Students = g.Select(e => e.StudentId).Distinct().Count(),
                LastAt = g.Max(e => e.JoinedAt)
            })
            .ToDictionaryAsync(x => x.AdminId, cancellationToken);

        var grades = db.StudentGrades.AsQueryable();
        if (subjectId is int gSubject)
            grades = grades.Where(g => g.GradeComponent.SubjectId == gSubject);
        if (fromUtc is DateTime from3) grades = grades.Where(g => g.UpdatedAt >= from3);
        if (toUtc is DateTime to3) grades = grades.Where(g => g.UpdatedAt <= to3);
        var gradeStats = await grades
            .GroupBy(g => g.AdminId)
            .Select(g => new
            {
                AdminId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(x => x.UpdatedAt)
            })
            .ToDictionaryAsync(x => x.AdminId, cancellationToken);

        var sessions = db.AttendanceSessions.Where(s => s.CreatedByAdminId != null);
        if (subjectId is int asSubject)
            sessions = sessions.Where(s => s.SubjectId == asSubject);
        if (fromUtc is DateTime from4) sessions = sessions.Where(s => s.CreatedAt >= from4);
        if (toUtc is DateTime to4) sessions = sessions.Where(s => s.CreatedAt <= to4);
        var sessionStats = await sessions
            .GroupBy(s => s.CreatedByAdminId!.Value)
            .Select(g => new
            {
                AdminId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(x => x.CreatedAt)
            })
            .ToDictionaryAsync(x => x.AdminId, cancellationToken);

        var records = db.AttendanceRecords.Where(r => r.MarkedByAdminId != null);
        if (subjectId is int arSubject)
            records = records.Where(r => r.Session.SubjectId == arSubject);
        if (fromUtc is DateTime from5) records = records.Where(r => r.UpdatedAt != null && r.UpdatedAt >= from5);
        if (toUtc is DateTime to5) records = records.Where(r => r.UpdatedAt != null && r.UpdatedAt <= to5);
        var recordStats = await records
            .GroupBy(r => r.MarkedByAdminId!.Value)
            .Select(g => new
            {
                AdminId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(x => x.UpdatedAt)
            })
            .ToDictionaryAsync(x => x.AdminId, cancellationToken);

        var variants = db.VariantAssignments.Where(v => v.AssignedByAdminId != null);
        if (subjectId is int vSubject)
            variants = variants.Where(v => v.VariantSet.SubjectId == vSubject);
        if (fromUtc is DateTime from6) variants = variants.Where(v => v.AssignedAt >= from6);
        if (toUtc is DateTime to6) variants = variants.Where(v => v.AssignedAt <= to6);
        var variantStats = await variants
            .GroupBy(v => v.AssignedByAdminId!.Value)
            .Select(g => new
            {
                AdminId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(x => x.AssignedAt)
            })
            .ToDictionaryAsync(x => x.AdminId, cancellationToken);

        Dictionary<Guid, (int Count, DateTime LastAt)> achievementStats = [];
        if (subjectId is null)
        {
            var awards = db.AchievementAwards.Where(a => a.IssuedByUserId != null);
            if (fromUtc is DateTime from7) awards = awards.Where(a => a.GrantedAtUtc >= from7);
            if (toUtc is DateTime to7) awards = awards.Where(a => a.GrantedAtUtc <= to7);
            achievementStats = await awards
                .GroupBy(a => a.IssuedByUserId!.Value)
                .Select(g => new
                {
                    AdminId = g.Key,
                    Count = g.Count(),
                    LastAt = g.Max(x => x.GrantedAtUtc)
                })
                .ToDictionaryAsync(x => x.AdminId, x => (x.Count, x.LastAt), cancellationToken);
        }

        var admins = await db.Admins.AsNoTracking()
            .Select(a => new { a.Id, a.FirstName, a.LastName, a.Color, a.Letter })
            .ToListAsync(cancellationToken);

        return admins
            .Select(a =>
            {
                var sub = submissionStats.GetValueOrDefault(a.Id);
                var q = queueFinished.GetValueOrDefault(a.Id);
                var gr = gradeStats.GetValueOrDefault(a.Id);
                var ses = sessionStats.GetValueOrDefault(a.Id);
                var rec = recordStats.GetValueOrDefault(a.Id);
                var varA = variantStats.GetValueOrDefault(a.Id);
                var ach = achievementStats.GetValueOrDefault(a.Id);

                DateTime? lastAt = MaxDate(
                    sub?.LastAt,
                    q?.LastAt,
                    gr?.LastAt,
                    ses?.LastAt,
                    rec?.LastAt,
                    varA?.LastAt,
                    ach.Count > 0 ? ach.LastAt : null);

                return new AdminActivityStatsDto(
                    a.Id,
                    $"{a.LastName} {a.FirstName}",
                    a.Color,
                    a.Letter,
                    sub?.Accepted ?? 0,
                    sub?.Rejected ?? 0,
                    q?.Students ?? 0,
                    gr?.Count ?? 0,
                    ses?.Count ?? 0,
                    rec?.Count ?? 0,
                    varA?.Count ?? 0,
                    ach.Count,
                    lastAt);
            })
            .OrderByDescending(s => s.TotalActions)
            .ThenBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTime? MaxDate(params DateTime?[] dates)
    {
        DateTime? max = null;
        foreach (var d in dates)
        {
            if (d is DateTime dt && (max is null || dt > max.Value))
                max = dt;
        }
        return max;
    }
}

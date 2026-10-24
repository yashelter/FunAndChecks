using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Submissions;

/// <summary>Сдачи студента по предмету: принятые задачи (по последней попытке) и все попытки.</summary>
public record SubjectSubmissionStats(int SubjectId, int AcceptedTasks, int Attempts, int Points);

public static class SubmissionStats
{
    /// <summary>
    /// Текущий статус задачи — последняя попытка (SubmittedAt, затем Id), как в таблице результатов.
    /// История одного студента невелика, поэтому считаем в памяти.
    /// </summary>
    public static async Task<Dictionary<int, SubjectSubmissionStats>> ForStudentAsync(IApplicationDbContext db, Guid studentId,
        IReadOnlyCollection<int> subjectIds, CancellationToken ct = default)
    {
        if (subjectIds.Count == 0) return [];
        var rows = await db.Submissions.AsNoTracking()
            .Where(s => s.StudentId == studentId && subjectIds.Contains(s.Task.SubjectId))
            .Select(s => new { s.Id, s.TaskId, s.Task.SubjectId, s.Task.MaxPoints, s.Status, s.SubmittedAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SubjectId).ToDictionary(g => g.Key, g =>
        {
            var latest = g.GroupBy(r => r.TaskId)
                .Select(t => t.OrderByDescending(r => r.SubmittedAt).ThenByDescending(r => r.Id).First())
                .Where(r => r.Status == SubmissionStatus.Accepted).ToList();
            return new SubjectSubmissionStats(g.Key, latest.Count, g.Count(), latest.Sum(r => r.MaxPoints));
        });
    }
}

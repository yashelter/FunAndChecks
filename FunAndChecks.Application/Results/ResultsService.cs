using System.Collections.Concurrent;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Results;

public class ResultsService(
    IApplicationDbContext db,
    IResultsCacheService cache)
    : IResultsService
{
    public async Task<SubjectResultsDto> GetSubjectResultsAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrAddAsync(subjectId, async () =>
        {
            return await BuildSubjectResultsAsync(subjectId, cancellationToken)
                   ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");
        });
    }

    public async Task<IReadOnlyList<StudentColorUpdateDto>> GetStudentColorsAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        if (cache.GetResults(subjectId) is { } cached)
            return cached.UserResults.Select(row => new StudentColorUpdateDto(row.StudentId, row.StudentColor)).ToList();

        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        // Те же студенты, что в таблице: активные, из групп, привязанных к предмету.
        return await db.Students
            .Where(s => s.IsActive && s.GroupId != null &&
                        db.GroupSubjects.Any(gs => gs.SubjectId == subjectId && gs.GroupId == s.GroupId))
            .Select(s => new StudentColorUpdateDto(s.Id, s.Color))
            .ToListAsync(cancellationToken);
    }

    private async Task<SubjectResultsDto?> BuildSubjectResultsAsync(int subjectId, CancellationToken cancellationToken)
    {
        var subject = await db.Subjects
            .Include(s => s.Tasks).ThenInclude(t => t.TaskGroup)
            .Include(s => s.GradeComponents.OrderBy(c => c.Name))
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken);

        if (subject == null)
            return null;

        var studentsData = await db.Students
            .Where(s => s.IsActive && s.GroupId != null &&
                        db.GroupSubjects.Any(gs => gs.SubjectId == subjectId && gs.GroupId == s.GroupId))
            .Select(s => new
            {
                s.Id,
                s.FullName,
                s.LastName,
                s.FirstName,
                GroupName = s.Group != null ? s.Group.Name : "N/A",
                s.Color,
                Grades = s.Grades
                    .Where(g => g.GradeComponent.SubjectId == subjectId)
                    .Select(g => new { g.GradeComponentId, g.Points })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var studentIds = studentsData.Select(s => s.Id).ToList();

        var allSubmissions = await db.Submissions
            .Where(s => s.Task.SubjectId == subjectId && studentIds.Contains(s.StudentId))
            // Последняя попытка (StudentId, TaskId) с детерминированным тай-брейком по Id.
            .Where(s => s.Id == db.Submissions
                .Where(s2 => s2.StudentId == s.StudentId && s2.TaskId == s.TaskId)
                .OrderByDescending(s2 => s2.SubmittedAt)
                .ThenByDescending(s2 => s2.Id)
                .Select(s2 => (int?)s2.Id)
                .FirstOrDefault())
            .Select(s => new
            {
                s.StudentId,
                s.TaskId,
                s.Status,
                s.SubmittedAt,
                AdminLetter = s.Admin != null ? s.Admin.Letter : "?",
                AdminColor = s.Admin != null ? s.Admin.Color : null
            })
            .ToListAsync(cancellationToken);

        var latestSubmissionsByStudent = allSubmissions
            .GroupBy(s => s.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(s => s.TaskId)
                      .ToDictionary(
                          tg => tg.Key,
                          tg => tg.OrderByDescending(s => s.SubmittedAt).First()
                      )
            );

        var tasks = TaskOrder.Apply(subject.Tasks);
        var taskHeaders = tasks.Select(t => new TaskHeaderDto(t.Id, t.Name, t.MaxPoints)).ToList();
        var gradeColumns = subject.GradeComponents.Select(c => new GradeColumnDto(c.Id, c.Name, c.MaxPoints)).ToList();

        var rows = studentsData.Select(student =>
        {
            var totalPoints = 0;
            var cells = new Dictionary<int, ResultCellDto>();

            var hasSubmissions = latestSubmissionsByStudent.TryGetValue(student.Id, out var dict);

            foreach (var task in tasks)
            {
                var lastSubmission = hasSubmissions && dict!.TryGetValue(task.Id, out var sub) ? sub : null;

                if (lastSubmission?.Status == SubmissionStatus.Accepted)
                    totalPoints += task.MaxPoints;

                // Пустые ячейки не передаём: на предметах со 100+ задачами это тысячи одинаковых объектов в каждом ответе.
                // Все потребители читают ячейку через GetValueOrDefault — нет ячейки, значит «не сдано».
                if (lastSubmission?.Status == SubmissionStatus.Accepted)
                    cells[task.Id] = new ResultCellDto("+", lastSubmission.AdminColor, lastSubmission.Status);
                else if (lastSubmission?.Status == SubmissionStatus.Rejected)
                    cells[task.Id] = new ResultCellDto(lastSubmission.AdminLetter ?? string.Empty, lastSubmission.AdminColor, lastSubmission.Status);
            }

            var grades = new Dictionary<int, int>();
            foreach (var grade in student.Grades)
                grades[grade.GradeComponentId] = grade.Points;

            return new StudentResultRowDto(
                student.Id,
                student.FullName,
                student.GroupName,
                totalPoints,
                cells,
                grades,
                student.Color,
                student.LastName,
                student.FirstName);
        })
        .OrderByDescending(r => r.TotalPoints)
        .ThenBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return new SubjectResultsDto(subject.Id, subject.Name, taskHeaders, gradeColumns, rows);
    }

    public async Task<StudentSubjectResultsDto> GetStudentResultsAsync(Guid studentId, int subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await db.Subjects
            .Include(s => s.Tasks).ThenInclude(t => t.TaskGroup)
            .Include(s => s.GradeComponents)
            .FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken)
            ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var submissions = await db.Submissions
            .Include(s => s.Admin)
            .Where(s => s.StudentId == studentId && s.Task.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

        var taskResults = new List<StudentTaskResultDto>();

        foreach (var task in TaskOrder.Apply(subject.Tasks))
        {
            var taskSubmissions = submissions
                .Where(s => s.TaskId == task.Id)
                .OrderByDescending(s => s.SubmittedAt)
                .ThenByDescending(s => s.Id)
                .ToList();

            var currentStatus = taskSubmissions.FirstOrDefault()?.Status ?? SubmissionStatus.NotSubmitted;

            List<SubmissionLogDto>? history = null;
            if (currentStatus == SubmissionStatus.Rejected)
            {
                history = taskSubmissions
                    .Select(s => new SubmissionLogDto(
                        s.Status,
                        s.Comment,
                        s.SubmittedAt,
                        new AdminDto(s.Admin.Id, s.Admin.FirstName, s.Admin.LastName, s.Admin.Color, s.Admin.Letter)))
                    .ToList();
            }

            taskResults.Add(new StudentTaskResultDto(task.Id, task.Name, currentStatus, task.MaxPoints, history));
        }

        var grades = await db.StudentGrades
            .Where(g => g.StudentId == studentId && g.GradeComponent.SubjectId == subjectId)
            .Select(g => new StudentGradeResultDto(
                g.GradeComponentId,
                g.GradeComponent.Name,
                g.Points,
                g.GradeComponent.MaxPoints,
                g.Comment))
            .ToListAsync(cancellationToken);

        var totalPointsEarned =
            taskResults.Where(tr => tr.CurrentStatus == SubmissionStatus.Accepted).Sum(tr => tr.MaxPoints);

        var maxPointsPossible = subject.Tasks.Sum(t => t.MaxPoints);

        return new StudentSubjectResultsDto(
            subject.Id,
            subject.Name,
            totalPointsEarned,
            maxPointsPossible,
            taskResults,
            grades);
    }

    private static ResultCellDto ToCell(Submission? lastSubmission) =>
        lastSubmission?.Status switch
        {
            SubmissionStatus.Accepted => new ResultCellDto("+", lastSubmission.Admin?.Color, lastSubmission.Status),
            SubmissionStatus.Rejected => new ResultCellDto(lastSubmission.Admin?.Letter ?? "?", lastSubmission.Admin?.Color, lastSubmission.Status),
            _ => new ResultCellDto("", null, SubmissionStatus.NotSubmitted),
        };
}

using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Submissions;
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
        var cached = cache.GetResults(subjectId);
        if (cached != null)
            return cached;

        var results = await BuildSubjectResultsAsync(subjectId, cancellationToken)
                      ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");

        cache.UpdateResults(subjectId, results);
        return results;
    }

    private async Task<SubjectResultsDto?> BuildSubjectResultsAsync(int subjectId, CancellationToken cancellationToken)
    {
        var subject = await db.Subjects
            .Include(s => s.Tasks.OrderBy(t => t.Name))
            .Include(s => s.GradeComponents.OrderBy(c => c.Name))
            .FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken);

        if (subject == null)
            return null;

        var students = await db.Students
            .Where(s => s.IsActive && s.GroupId != null &&
                        db.GroupSubjects.Any(gs => gs.SubjectId == subjectId && gs.GroupId == s.GroupId))
            .Include(s => s.Group)
            .Include(s => s.Submissions.Where(sub => sub.Task.SubjectId == subjectId))
            .ThenInclude(sub => sub.Admin)
            .Include(s => s.Grades.Where(g => g.GradeComponent.SubjectId == subjectId))
            .ToListAsync(cancellationToken);

        var taskHeaders = subject.Tasks.Select(t => new TaskHeaderDto(t.Id, t.Name, t.MaxPoints)).ToList();
        var gradeColumns = subject.GradeComponents.Select(c => new GradeColumnDto(c.Id, c.Name, c.MaxPoints)).ToList();

        var rows = students.Select(student =>
        {
            var totalPoints = 0;
            var cells = new Dictionary<int, ResultCellDto>();

            foreach (var task in subject.Tasks)
            {
                var lastSubmission = student.Submissions
                    .Where(s => s.TaskId == task.Id)
                    .OrderByDescending(s => s.SubmittedAt)
                    .FirstOrDefault();

                if (lastSubmission?.Status == SubmissionStatus.Accepted)
                    totalPoints += task.MaxPoints;

                cells[task.Id] = ToCell(lastSubmission);
            }

            // Оценки-категории (билет/курсовая) показываются отдельными колонками и НЕ входят в Σ баллов.
            var grades = new Dictionary<int, int>();
            foreach (var grade in student.Grades)
                grades[grade.GradeComponentId] = grade.Points;

            return new StudentResultRowDto(
                student.Id,
                student.FullName,
                student.Group?.Name ?? "N/A",
                totalPoints,
                cells,
                grades,
                student.Color);
        })
        // Порядок по умолчанию для отображения: по баллам, затем по ФИО.
        .OrderByDescending(r => r.TotalPoints)
        .ThenBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return new SubjectResultsDto(subject.Id, subject.Name, taskHeaders, gradeColumns, rows);
    }

    public async Task<StudentSubjectResultsDto> GetStudentResultsAsync(Guid studentId, int subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await db.Subjects
            .Include(s => s.Tasks)
            .Include(s => s.GradeComponents)
            .FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken)
            ?? throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var submissions = await db.Submissions
            .Include(s => s.Admin)
            .Where(s => s.StudentId == studentId && s.Task.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

        var taskResults = new List<StudentTaskResultDto>();

        foreach (var task in subject.Tasks)
        {
            var taskSubmissions = submissions
                .Where(s => s.TaskId == task.Id)
                .OrderByDescending(s => s.SubmittedAt)
                .ToList();

            var currentStatus = taskSubmissions.FirstOrDefault()?.Status ?? SubmissionStatus.NotSubmitted;

            // Историю показываем только если последняя попытка не принята.
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

        // Оценки-категории идут отдельным списком и не входят в сумму баллов по задачам.
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

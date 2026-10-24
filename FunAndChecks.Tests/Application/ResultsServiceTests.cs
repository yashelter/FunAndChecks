using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Results;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Caching;
using FunAndChecks.Tests.Common;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class ResultsServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ResultsCacheService _cache = new();

    [Fact]
    public async Task GetSubjectResults_TotalExcludesGrades_ButGradesShownSeparately()
    {
        int subjectId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject, maxPoints: 10);
        var component = ctx.Component(subject, maxPoints: 100);
        await ctx.SaveChangesAsync();
        subjectId = subject.Id;

        ctx.Submissions.Add(new Submission
        {
            StudentId = student.Id, TaskId = task.Id, AdminId = admin.Id,
            Status = SubmissionStatus.Accepted, SubmittedAt = DateTime.UtcNow,
        });
        ctx.StudentGrades.Add(new StudentGrade
        {
            GradeComponentId = component.Id, StudentId = student.Id, AdminId = admin.Id,
            Points = 30, UpdatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var sut = new ResultsService(ctx, _cache);
        var results = await sut.GetSubjectResultsAsync(subjectId);

        results.GradeColumns.Should().ContainSingle();
        results.UserResults.Should().ContainSingle();
        // Оценки-категории НЕ входят в Σ — только баллы задач.
        results.UserResults[0].TotalPoints.Should().Be(10);
        results.UserResults[0].Grades[component.Id].Should().Be(30);
    }

    [Fact]
    public async Task Tasks_AreOrderedLexicographicallyByName_InTableAndStudentResults()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        // Создаём вперемешку: порядок создания не должен влиять на порядок в списках.
        foreach (var name in new[] { "1.2", "Лаба 10", "1.01", "лаба 2", "1.1" }) ctx.Task(subject, name: name);
        await ctx.SaveChangesAsync();
        string[] expected = ["1.01", "1.1", "1.2", "Лаба 10", "лаба 2"];

        var sut = new ResultsService(ctx, _cache);

        (await sut.GetSubjectResultsAsync(subject.Id)).TaskHeaders.Select(t => t.TaskName).Should().Equal(expected);
        (await sut.GetStudentResultsAsync(student.Id, subject.Id)).TaskResults.Select(t => t.TaskName).Should().Equal(expected);
    }

    [Fact]
    public async Task Tasks_InGroups_FollowUngroupedTasks_InTableAndStudentResults()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var labs = ctx.TaskGroup(subject, "Лабы");
        var modules = ctx.TaskGroup(subject, "Модули");
        (string, TaskGroup?)[] tasks = [("1.2", labs), ("Экзамен", null), ("М 2", modules), ("1.01", labs), ("М 1", modules)];
        foreach (var (name, taskGroup) in tasks)
            ctx.Task(subject, name: name, group: taskGroup);
        await ctx.SaveChangesAsync();
        string[] expected = ["Экзамен", "1.01", "1.2", "М 1", "М 2"];

        var sut = new ResultsService(ctx, _cache);

        (await sut.GetSubjectResultsAsync(subject.Id)).TaskHeaders.Select(t => t.TaskName).Should().Equal(expected);
        (await sut.GetStudentResultsAsync(student.Id, subject.Id)).TaskResults.Select(t => t.TaskName).Should().Equal(expected);
    }

    [Fact]
    public async Task GetSubjectResults_UsesCacheOnSecondCall()
    {
        int subjectId;
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        subjectId = subject.Id;

        var sut = new ResultsService(ctx, _cache);
        var first = await sut.GetSubjectResultsAsync(subjectId);
        var second = await sut.GetSubjectResultsAsync(subjectId);

        second.Should().BeSameAs(first);

        _cache.Invalidate(subjectId);
        var third = await sut.GetSubjectResultsAsync(subjectId);
        third.Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task GetSubjectResults_UnknownSubject_Throws()
    {
        await using var ctx = _db.NewContext();
        var sut = new ResultsService(ctx, _cache);
        var act = () => sut.GetSubjectResultsAsync(404);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetStudentResults_MaxPointsFromTasksOnly_GradesSeparate()
    {
        Guid studentId; int subjectId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var task = ctx.Task(subject, maxPoints: 10);
        ctx.Component(subject, maxPoints: 100);
        await ctx.SaveChangesAsync();
        studentId = student.Id; subjectId = subject.Id;

        var sut = new ResultsService(ctx, _cache);
        var result = await sut.GetStudentResultsAsync(studentId, subjectId);

        // Колонки-оценки идут отдельным списком, в максимум по задачам не входят.
        result.MaxPointsPossible.Should().Be(10);
    }

    public void Dispose() => _db.Dispose();
}

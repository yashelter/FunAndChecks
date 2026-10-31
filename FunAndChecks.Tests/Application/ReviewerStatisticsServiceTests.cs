using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Statistics;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>
/// Статистика проверяющих: все попытки (а не только последние) и разные студенты, завершённые в очереди.
/// </summary>
public class ReviewerStatisticsServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();

    private sealed record Fixture(Guid Anna, Guid Boris, Guid Idle, int Math, int Physics, Guid Student1, Guid Student2,
        int MathTask, int PhysicsTask, int MathQueue, int MathQueue2, int PhysicsQueue);

    private async Task<Fixture> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var anna = ctx.Admin("А");
        anna.LastName = "Аннова";
        var boris = ctx.Admin("Б");
        boris.LastName = "Борисов";
        var idle = ctx.Admin("В");
        idle.LastName = "Вялый";
        var group = ctx.Group();
        var math = ctx.Subject("Математика");
        var physics = ctx.Subject("Физика");
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, math);
        ctx.LinkGroupSubject(group, physics);
        var s1 = ctx.Student(group, "Первый");
        var s2 = ctx.Student(group, "Второй");
        var mathTask = ctx.Task(math);
        var physicsTask = ctx.Task(physics);
        var mathQueue = new QueueEvent { Name = "М1", SubjectId = math.Id, EventDateTime = DateTime.UtcNow };
        var mathQueue2 = new QueueEvent { Name = "М2", SubjectId = math.Id, EventDateTime = DateTime.UtcNow };
        var physicsQueue = new QueueEvent { Name = "Ф1", SubjectId = physics.Id, EventDateTime = DateTime.UtcNow };
        ctx.QueueEvents.AddRange(mathQueue, mathQueue2, physicsQueue);
        await ctx.SaveChangesAsync();
        return new(anna.Id, boris.Id, idle.Id, math.Id, physics.Id, s1.Id, s2.Id, mathTask.Id, physicsTask.Id,
            mathQueue.Id, mathQueue2.Id, physicsQueue.Id);
    }

    private static void Attempt(ApplicationDbContext ctx, Guid admin, Guid student, int task, SubmissionStatus status, int minutesAgo) =>
        ctx.Submissions.Add(new Submission { AdminId = admin, StudentId = student, TaskId = task, Status = status, SubmittedAt = DateTime.UtcNow.AddMinutes(-minutesAgo) });

    private static void Finished(ApplicationDbContext ctx, int queue, Guid student, Guid? by, int round = 1,
        QueueEntryStatus status = QueueEntryStatus.Finished) =>
        ctx.QueueEntries.Add(new QueueEntry { QueueEventId = queue, StudentId = student, Status = status, Round = round, JoinedAt = DateTime.UtcNow, FinishedByAdminId = by });

    [Fact]
    public async Task CountsEveryAttempt_NotOnlyTheLatestPerTask()
    {
        var f = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // Одна задача: две непринятые попытки, потом принятая — у Анны 1 принято и 2 не принято.
            Attempt(ctx, f.Anna, f.Student1, f.MathTask, SubmissionStatus.Rejected, 30);
            Attempt(ctx, f.Anna, f.Student1, f.MathTask, SubmissionStatus.Rejected, 20);
            Attempt(ctx, f.Anna, f.Student1, f.MathTask, SubmissionStatus.Accepted, 10);
            Attempt(ctx, f.Boris, f.Student2, f.MathTask, SubmissionStatus.Accepted, 5);
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var stats = await new ReviewerStatisticsService(read).GetAsync(null);

        stats.Single(s => s.AdminId == f.Anna).Should().Match<ReviewerStatsDto>(s => s.Accepted == 1 && s.Rejected == 2);
        stats.Single(s => s.AdminId == f.Boris).Should().Match<ReviewerStatsDto>(s => s.Accepted == 1 && s.Rejected == 0);
        stats[0].AdminId.Should().Be(f.Anna, "the busiest reviewer comes first");
    }

    [Fact]
    public async Task ReviewersWithoutActivity_AreListedWithZeros()
    {
        var f = await SeedAsync();
        await using var read = _db.NewContext();

        var idle = (await new ReviewerStatisticsService(read).GetAsync(null)).Single(s => s.AdminId == f.Idle);

        idle.Should().Be(new ReviewerStatsDto(f.Idle, idle.FullName, idle.Color, idle.Letter, 0, 0, 0));
        idle.FullName.Should().StartWith("Вялый");
    }

    [Fact]
    public async Task StudentsFinished_CountsDistinctStudents_AcrossQueuesAndRounds()
    {
        var f = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // Первый студент завершён Анной дважды (два круга одной сдачи и ещё одна сдача) — это один человек.
            Finished(ctx, f.MathQueue, f.Student1, f.Anna, round: 1);
            Finished(ctx, f.MathQueue, f.Student1, f.Anna, round: 2);
            Finished(ctx, f.MathQueue2, f.Student1, f.Anna);
            Finished(ctx, f.MathQueue2, f.Student2, f.Anna);
            // Без отметки «кто завершил» (до обновления) и не завершённые — не считаются.
            Finished(ctx, f.PhysicsQueue, f.Student2, null);
            Finished(ctx, f.PhysicsQueue, f.Student1, f.Boris, status: QueueEntryStatus.Skipped);
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var stats = await new ReviewerStatisticsService(read).GetAsync(null);

        stats.Single(s => s.AdminId == f.Anna).StudentsFinished.Should().Be(2);
        stats.Single(s => s.AdminId == f.Boris).StudentsFinished.Should().Be(0);
    }

    [Fact]
    public async Task SubjectFilter_KeepsOnlyThatSubjectsAttemptsAndQueues()
    {
        var f = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            Attempt(ctx, f.Anna, f.Student1, f.MathTask, SubmissionStatus.Accepted, 10);
            Attempt(ctx, f.Anna, f.Student1, f.PhysicsTask, SubmissionStatus.Rejected, 10);
            Finished(ctx, f.MathQueue, f.Student1, f.Anna);
            Finished(ctx, f.PhysicsQueue, f.Student2, f.Anna);
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var sut = new ReviewerStatisticsService(read);

        (await sut.GetAsync(f.Physics)).Single(s => s.AdminId == f.Anna)
            .Should().Match<ReviewerStatsDto>(s => s.Accepted == 0 && s.Rejected == 1 && s.StudentsFinished == 1);
        (await sut.GetAsync(null)).Single(s => s.AdminId == f.Anna)
            .Should().Match<ReviewerStatsDto>(s => s.Accepted == 1 && s.Rejected == 1 && s.StudentsFinished == 2);
    }

    [Fact]
    public async Task UnknownSubject_Throws()
    {
        await using var read = _db.NewContext();
        var act = () => new ReviewerStatisticsService(read).GetAsync(404);
        (await act.Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("subject.not_found");
    }

    public void Dispose() => _db.Dispose();
}

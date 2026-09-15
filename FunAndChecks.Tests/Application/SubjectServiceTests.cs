using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;
using FunAndChecks.Application.Tasks;

namespace FunAndChecks.Tests.Application;

public class SubjectServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();
    private readonly IAdminAccessService _access = Substitute.For<IAdminAccessService>();

    private SubjectService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _cache, _access, 
            new CreateSubjectRequestValidator(), 
            new UpdateSubjectRequestValidator(),
            new CreateTaskRequestValidator(),
            new UpdateTaskRequestValidator());

    [Fact]
    public async Task UpdateSubject_InvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("Old Name");
        await ctx.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = CreateSut(ctx);

        var req = new UpdateSubjectRequest("New Name");
        await sut.UpdateAsync(adminId, subject.Id, req);

        _cache.Received(1).Invalidate(subject.Id);
    }

    [Fact]
    public async Task CreateTask_InvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S1");
        await ctx.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = CreateSut(ctx);

        var req = new CreateTaskRequest("T1", "Desc", 10);
        await sut.CreateTaskAsync(adminId, subject.Id, req);

        _cache.Received(1).Invalidate(subject.Id);
    }

    [Fact]
    public async Task UpdateTask_InvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S1");
        await ctx.SaveChangesAsync();
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = CreateSut(ctx);

        var req = new UpdateTaskRequest("T1", "Desc", 15);
        await sut.UpdateTaskAsync(adminId, task.Id, req);

        _cache.Received(1).Invalidate(subject.Id);
    }

    [Fact]
    public async Task CreateSubject_DuplicateName_ThrowsConflictWithCode()
    {
        await using var ctx = _db.NewContext();
        ctx.Subject("Physics");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        var act = () => sut.CreateAsync(new CreateSubjectRequest("Physics"));

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("subjects.name_taken");
        ctx.Subjects.Count(s => s.Name == "Physics").Should().Be(1);
    }

    [Fact]
    public async Task UpdateSubject_DuplicateName_ThrowsConflict()
    {
        await using var ctx = _db.NewContext();
        var first = ctx.Subject("First");
        var second = ctx.Subject("Second");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        var act = () => sut.UpdateAsync(Guid.NewGuid(), second.Id, new UpdateSubjectRequest("First"));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateSubject_ToOwnName_Succeeds()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("Same");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        await sut.UpdateAsync(Guid.NewGuid(), subject.Id, new UpdateSubjectRequest("Same"));

        (await ctx.Subjects.FindAsync(subject.Id))!.Name.Should().Be("Same");
    }

    [Fact]
    public async Task CreateTask_DuplicateNameWithinSubject_ThrowsConflictWithCode()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        var otherSubject = ctx.Subject("Other");
        await ctx.SaveChangesAsync();
        ctx.Task(subject, name: "Task");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        var act = () => sut.CreateTaskAsync(Guid.NewGuid(), subject.Id, new CreateTaskRequest("Task", "Desc", 10));

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("tasks.name_taken");

        // Одноимённое задание в другом предмете — допустимо (уникальность в пределах предмета).
        await sut.CreateTaskAsync(Guid.NewGuid(), otherSubject.Id, new CreateTaskRequest("Task", "Desc", 10));
        ctx.Tasks.Count(t => t.Name == "Task").Should().Be(2);
    }

    [Fact]
    public async Task UpdateTask_DuplicateName_ThrowsConflict()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        await ctx.SaveChangesAsync();
        var first = ctx.Task(subject, name: "First");
        var second = ctx.Task(subject, name: "Second");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        var act = () => sut.UpdateTaskAsync(Guid.NewGuid(), second.Id, new UpdateTaskRequest("First", "Desc", 10));

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateTask_ToOwnName_Succeeds()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        await ctx.SaveChangesAsync();
        var task = ctx.Task(subject, name: "Same");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        await sut.UpdateTaskAsync(Guid.NewGuid(), task.Id, new UpdateTaskRequest("Same", "New desc", 15));

        var updated = await ctx.Tasks.FindAsync(task.Id);
        updated!.Name.Should().Be("Same");
        updated.Description.Should().Be("New desc");
    }

    public void Dispose() => _db.Dispose();
}

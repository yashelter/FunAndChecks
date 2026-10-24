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
            new UpdateTaskRequestValidator(),
            new CreateTaskGroupRequestValidator(),
            new UpdateTaskGroupRequestValidator());

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

    [Fact]
    public async Task CreateTaskGroup_DuplicateNameWithinSubject_ThrowsConflictWithCode()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        var otherSubject = ctx.Subject("Other");
        await ctx.SaveChangesAsync();
        var sut = CreateSut(ctx);

        var created = await sut.CreateTaskGroupAsync(Guid.NewGuid(), subject.Id, new CreateTaskGroupRequest("  Labs "));
        created.Name.Should().Be("Labs");

        var act = () => sut.CreateTaskGroupAsync(Guid.NewGuid(), subject.Id, new CreateTaskGroupRequest("Labs"));
        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("task_groups.name_taken");

        await sut.CreateTaskGroupAsync(Guid.NewGuid(), otherSubject.Id, new CreateTaskGroupRequest("Labs"));
        ctx.TaskGroups.Count(g => g.Name == "Labs").Should().Be(2);
    }

    [Fact]
    public async Task CreateTask_WithGroupOfAnotherSubject_IsRejected()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        var otherSubject = ctx.Subject("Other");
        await ctx.SaveChangesAsync();
        var foreignGroup = ctx.TaskGroup(otherSubject, "Labs");
        await ctx.SaveChangesAsync();
        var sut = CreateSut(ctx);

        var act = () => sut.CreateTaskAsync(Guid.NewGuid(), subject.Id, new CreateTaskRequest("Task", "", 1, foreignGroup.Id));

        (await act.Should().ThrowAsync<FluentValidation.ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorCode == "task_group_invalid");
        ctx.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateTask_AssignsGroupLater_AndNullClearsIt()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        await ctx.SaveChangesAsync();
        var group = ctx.TaskGroup(subject, "Labs");
        var task = ctx.Task(subject, name: "1.1");
        await ctx.SaveChangesAsync();
        var sut = CreateSut(ctx);

        var grouped = await sut.UpdateTaskAsync(Guid.NewGuid(), task.Id, new UpdateTaskRequest("1.1", "desc", 10, group.Id));
        grouped.TaskGroupId.Should().Be(group.Id);
        grouped.TaskGroupName.Should().Be("Labs");
        _cache.Received().Invalidate(subject.Id);

        var ungrouped = await sut.UpdateTaskAsync(Guid.NewGuid(), task.Id, new UpdateTaskRequest("1.1", "desc", 10));
        ungrouped.TaskGroupId.Should().BeNull();
        (await ctx.Tasks.FindAsync(task.Id))!.TaskGroupId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteTaskGroup_KeepsItsTasksWithoutGroup()
    {
        int groupId, taskId;
        await using (var ctx = _db.NewContext())
        {
            var subject = ctx.Subject("S");
            await ctx.SaveChangesAsync();
            var group = ctx.TaskGroup(subject, "Labs");
            var task = ctx.Task(subject, name: "1.1", group: group);
            await ctx.SaveChangesAsync();
            groupId = group.Id;
            taskId = task.Id;
        }

        await using (var ctx = _db.NewContext())
            await CreateSut(ctx).DeleteTaskGroupAsync(Guid.NewGuid(), groupId);

        await using (var ctx = _db.NewContext())
        {
            ctx.TaskGroups.Should().BeEmpty();
            var task = await ctx.Tasks.FindAsync(taskId);
            task.Should().NotBeNull();
            task!.TaskGroupId.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetTasks_UngroupedFirst_ThenGroupsByName_KeepingTaskOrderInsideGroup()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S");
        await ctx.SaveChangesAsync();
        var labs = ctx.TaskGroup(subject, "Labs");
        var homework = ctx.TaskGroup(subject, "Homework");
        await ctx.SaveChangesAsync();
        ctx.Task(subject, name: "1.2", group: labs);
        ctx.Task(subject, name: "Exam");
        ctx.Task(subject, name: "1.01", group: labs);
        ctx.Task(subject, name: "HW 2", group: homework);
        ctx.Task(subject, name: "Bonus");
        ctx.Task(subject, name: "HW 10", group: homework);
        await ctx.SaveChangesAsync();
        var studentGroup = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(studentGroup);
        await ctx.SaveChangesAsync();
        var sut = CreateSut(ctx);
        string[] expected = ["Bonus", "Exam", "HW 10", "HW 2", "1.01", "1.2"];

        (await sut.GetTasksAsync(subject.Id)).Select(t => t.Name).Should().Equal(expected);
        var withStatus = await sut.GetTasksWithStatusAsync(subject.Id, student.Id);
        withStatus.Select(t => t.Name).Should().Equal(expected);
        withStatus.Select(t => t.TaskGroupName).Should().Equal(null, null, "Homework", "Homework", "Labs", "Labs");
        (await sut.GetTaskGroupsAsync(subject.Id)).Select(g => (g.Name, g.TaskCount)).Should().Equal(("Homework", 2), ("Labs", 2));
    }

    public void Dispose() => _db.Dispose();
}

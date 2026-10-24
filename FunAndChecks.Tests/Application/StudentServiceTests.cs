using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Students;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Results;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Caching;
using FunAndChecks.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class StudentServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();
    private readonly IResultsNotifier _resultsNotifier = Substitute.For<IResultsNotifier>();
    private readonly IQueueNotifier _queueNotifier = Substitute.For<IQueueNotifier>();

    private StudentService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, _cache, ctx.Access(), new SetStudentColorRequestValidator(),
            new FunAndChecks.Application.Students.Validators.UpdateStudentAccountRequestValidator(),
            _resultsNotifier, _queueNotifier, NullLogger<StudentService>.Instance);

    [Fact]
    public async Task GetStudentsBySubject_ReturnsOnlyLinkedGroups()
    {
        int subjectId;
        await using var ctx = _db.NewContext();
        var linkedGroup = ctx.Group("Linked");
        var otherGroup = ctx.Group("Other");
        var subject = ctx.Subject();
        var admin = ctx.Admin();
        ctx.GrantSubject(admin, subject);
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(linkedGroup, subject);
        var inSubject = ctx.Student(linkedGroup, "Linked");
        ctx.Student(otherGroup, "Other");
        await ctx.SaveChangesAsync();
        subjectId = subject.Id;

        _identity.GetEmailsAsync(Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, string?> { [inSubject.Id] = "linked@example.com" });

        var sut = CreateSut(ctx);
        var students = await sut.GetStudentsBySubjectAsync(admin.Id, subjectId);

        students.Should().ContainSingle();
        students[0].LastName.Should().Be("Linked");
        students[0].Email.Should().Be("linked@example.com");
    }

    [Fact]
    public async Task GetDetails_IncludesEmailFromIdentity()
    {
        Guid studentId;
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();
        studentId = student.Id;

        _identity.GetEmailAsync(studentId).Returns("me@example.com");

        var sut = CreateSut(ctx);
        var details = await sut.GetDetailsAsync(Guid.NewGuid(), studentId);
        details.Email.Should().Be("me@example.com");
    }

    [Fact]
    public async Task SearchStudents_FindsBySurname()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        ctx.Student(group, "Petrov");
        ctx.Student(group, "Sidorov");
        await ctx.SaveChangesAsync();

        _identity.GetEmailsAsync(Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, string?>());

        var sut = CreateSut(ctx);
        var found = await sut.SearchStudentsAsync(Guid.NewGuid(), "petr");

        found.Should().ContainSingle();
        found[0].LastName.Should().Be("Petrov");
    }

    [Fact]
    public async Task SearchStudents_ExcludesGroupsHiddenOrRestrictedForAdmin()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var visibleGroup = ctx.Group("Visible");
        var restrictedGroup = ctx.Group("Restricted");
        await ctx.SaveChangesAsync();
        ctx.Student(visibleGroup, "Visible");
        ctx.Student(restrictedGroup, "Restricted");
        await ctx.SaveChangesAsync();
        await ctx.Access().SetGroupRestrictedAsync(admin.Id, restrictedGroup.Id, true);
        _identity.GetEmailsAsync(Arg.Any<IEnumerable<Guid>>()).Returns(new Dictionary<Guid, string?>());

        var students = await CreateSut(ctx).SearchStudentsAsync(admin.Id, string.Empty);

        students.Select(student => student.LastName).Should().Equal("Visible");
    }

    [Fact]
    public async Task SetColor_UpdatesColor_AndInvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        await sut.SetColorAsync(Guid.NewGuid(), student.Id, new SetStudentColorRequest("#228822"));

        var updated = await ctx.Students.FindAsync(student.Id);
        updated!.Color.Should().Be("#228822");
        _cache.Received().Invalidate(subject.Id);
    }

    [Fact]
    public async Task SetColor_Null_RemovesFill()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        student.Color = "#228822";
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        await sut.SetColorAsync(Guid.NewGuid(), student.Id, new SetStudentColorRequest(null));

        var updated = await ctx.Students.FindAsync(student.Id);
        updated!.Color.Should().BeNull();
    }

    [Theory]
    [InlineData("#E53935")]
    [InlineData("#8E24AA")]
    [InlineData("#00BFA5")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData(null)]
    public async Task SetColor_PersistsForBothResultsAndQueue_AndNotifiesEveryAffectedView(string? color)
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var otherGroup = ctx.Group("Other");
        var firstSubject = ctx.Subject("First");
        var secondSubject = ctx.Subject("Second");
        var unrelatedSubject = ctx.Subject("Unrelated");
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, firstSubject);
        ctx.LinkGroupSubject(group, secondSubject);
        ctx.LinkGroupSubject(otherGroup, unrelatedSubject);
        var student = ctx.Student(group);
        student.Color = "#1122FF";
        var otherStudent = ctx.Student(otherGroup);
        var queue = new QueueEvent { Name = "Review", SubjectId = firstSubject.Id, EventDateTime = DateTime.UtcNow };
        var oldQueue = new QueueEvent { Name = "Old subject", SubjectId = unrelatedSubject.Id, EventDateTime = DateTime.UtcNow.AddDays(-3) };
        var unrelatedQueue = new QueueEvent { Name = "Other student", SubjectId = unrelatedSubject.Id, EventDateTime = DateTime.UtcNow };
        ctx.QueueEvents.AddRange(queue, oldQueue, unrelatedQueue);
        await ctx.SaveChangesAsync();
        ctx.QueueEntries.AddRange(
            new QueueEntry { QueueEventId = queue.Id, StudentId = student.Id, Status = QueueEntryStatus.Checking, JoinedAt = DateTime.UtcNow },
            new QueueEntry { QueueEventId = oldQueue.Id, StudentId = student.Id, Status = QueueEntryStatus.Finished, JoinedAt = DateTime.UtcNow },
            new QueueEntry { QueueEventId = unrelatedQueue.Id, StudentId = otherStudent.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        // Prime the actual cache before mutation: a fresh database read alone would
        // not catch stale tables caused by missing invalidation.
        var cache = new ResultsCacheService();
        var initialResults = new ResultsService(ctx, cache);
        (await initialResults.GetSubjectResultsAsync(firstSubject.Id)).UserResults.Single().StudentColor.Should().Be("#1122FF");
        (await initialResults.GetSubjectResultsAsync(secondSubject.Id)).UserResults.Single().StudentColor.Should().Be("#1122FF");
        var service = new StudentService(ctx, _identity, cache, ctx.Access(),
            new SetStudentColorRequestValidator(), new FunAndChecks.Application.Students.Validators.UpdateStudentAccountRequestValidator(),
            _resultsNotifier, _queueNotifier, NullLogger<StudentService>.Instance);

        await service.SetColorAsync(Guid.NewGuid(), student.Id, new SetStudentColorRequest(color));

        // Reopen the context to prove persistence rather than returning a tracked entity.
        await using var fresh = _db.NewContext();
        var resultsService = new ResultsService(fresh, cache);
        foreach (var subjectId in new[] { firstSubject.Id, secondSubject.Id })
        {
            (await resultsService.GetSubjectResultsAsync(subjectId)).UserResults.Single().StudentColor.Should().Be(color);
            await _resultsNotifier.Received(1).StudentColorUpdatedAsync(subjectId,
                Arg.Is<StudentColorUpdateDto>(update => update.StudentId == student.Id && update.Color == color), Arg.Any<CancellationToken>());
        }
        var queues = new QueueService(fresh, _queueNotifier, fresh.Access(),
            new CreateQueueEventRequestValidator(), new UpdateQueueEventRequestValidator(), NullLogger<QueueService>.Instance);
        foreach (var eventId in new[] { queue.Id, oldQueue.Id })
        {
            (await queues.GetDetailsAsync(eventId)).Participants.Single().StudentColor.Should().Be(color);
            await _queueNotifier.Received(1).StudentColorUpdatedAsync(eventId,
                Arg.Is<StudentColorUpdateDto>(update => update.StudentId == student.Id && update.Color == color), Arg.Any<CancellationToken>());
        }
        (await queues.GetDetailsAsync(queue.Id)).Participants.Single().Status.Should().Be(QueueEntryStatus.Checking);
        (await queues.GetDetailsAsync(oldQueue.Id)).Participants.Single().Status.Should().Be(QueueEntryStatus.Finished);
        await _resultsNotifier.DidNotReceive().StudentColorUpdatedAsync(unrelatedSubject.Id, Arg.Any<StudentColorUpdateDto>(), Arg.Any<CancellationToken>());
        await _queueNotifier.DidNotReceive().StudentColorUpdatedAsync(unrelatedQueue.Id, Arg.Any<StudentColorUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetColor_NotificationFailure_KeepsSavedColor_AndNotifiesOtherViews()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var firstQueue = new QueueEvent { Name = "First", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow };
        var secondQueue = new QueueEvent { Name = "Second", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow };
        ctx.QueueEvents.AddRange(firstQueue, secondQueue);
        await ctx.SaveChangesAsync();
        ctx.QueueEntries.AddRange(
            new QueueEntry { QueueEventId = firstQueue.Id, StudentId = student.Id, JoinedAt = DateTime.UtcNow },
            new QueueEntry { QueueEventId = secondQueue.Id, StudentId = student.Id, JoinedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        _resultsNotifier.StudentColorUpdatedAsync(Arg.Any<int>(), Arg.Any<StudentColorUpdateDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Results transport unavailable")));
        _queueNotifier.StudentColorUpdatedAsync(firstQueue.Id, Arg.Any<StudentColorUpdateDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Queue transport unavailable")));

        await CreateSut(ctx).SetColorAsync(Guid.NewGuid(), student.Id, new SetStudentColorRequest("#C2185B"));

        await using var fresh = _db.NewContext();
        (await fresh.Students.FindAsync(student.Id))!.Color.Should().Be("#C2185B");
        _cache.Received(1).Invalidate(subject.Id);
        await _queueNotifier.Received(1).StudentColorUpdatedAsync(secondQueue.Id,
            Arg.Is<StudentColorUpdateDto>(update => update.StudentId == student.Id && update.Color == "#C2185B"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetColor_WhenGroupRestricted_ThrowsAndDoesNotChangeStudent()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();
        await ctx.Access().SetGroupRestrictedAsync(admin.Id, group.Id, true);

        var act = () => CreateSut(ctx).SetColorAsync(admin.Id, student.Id, new SetStudentColorRequest("#228822"));

        await act.Should().ThrowAsync<FunAndChecks.Application.Common.Exceptions.ForbiddenException>();
        student.Color.Should().BeNull();
        await _resultsNotifier.DidNotReceive().StudentColorUpdatedAsync(Arg.Any<int>(), Arg.Any<StudentColorUpdateDto>(), Arg.Any<CancellationToken>());
        await _queueNotifier.DidNotReceive().StudentColorUpdatedAsync(Arg.Any<int>(), Arg.Any<StudentColorUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStudentAccount_ValidRequest_UpdatesProfileAndIdentity()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group, "Old");
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var req = new UpdateStudentAccountRequest("NewFirst", "NewLast", group.Id, "new@test.com", "newpass");

        await sut.UpdateStudentAccountAsync(Guid.NewGuid(), student.Id, req);

        var updated = await ctx.Students.FindAsync(student.Id);
        updated!.FirstName.Should().Be("NewFirst");
        updated.LastName.Should().Be("NewLast");

        await _identity.Received(1).UpdateAccountAdminAsync(student.Id, "new@test.com", "newpass");
    }

    [Fact]
    public async Task UpdateStudentAccount_WhenGroupChanges_InvalidatesOldAndNewSubjects()
    {
        await using var ctx = _db.NewContext();
        var oldGroup = ctx.Group("Old");
        var newGroup = ctx.Group("New");
        var oldSubject = ctx.Subject("Old subject");
        var newSubject = ctx.Subject("New subject");
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(oldGroup, oldSubject);
        ctx.LinkGroupSubject(newGroup, newSubject);
        var student = ctx.Student(oldGroup);
        await ctx.SaveChangesAsync();

        await CreateSut(ctx).UpdateStudentAccountAsync(Guid.NewGuid(), student.Id,
            new UpdateStudentAccountRequest("New", "Name", newGroup.Id, "new@test.com", null));

        _cache.Received(1).Invalidate(oldSubject.Id);
        _cache.Received(1).Invalidate(newSubject.Id);
    }

    [Fact]
    public async Task UpdateStudentAccount_GroupNotFound_ThrowsNotFound()
    {
        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);
        var req = new UpdateStudentAccountRequest("New", "Last", 999, "test@test.com", null);

        var act = async () => await sut.UpdateStudentAccountAsync(Guid.NewGuid(), Guid.NewGuid(), req);

        await act.Should().ThrowAsync<FunAndChecks.Application.Common.Exceptions.NotFoundException>().WithMessage("Group not found.");
    }

    public void Dispose() => _db.Dispose();
}

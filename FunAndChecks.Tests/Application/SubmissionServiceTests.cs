using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace FunAndChecks.Tests.Application;

public class SubmissionServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();
    private readonly IResultsNotifier _notifier = Substitute.For<IResultsNotifier>();

    private SubmissionService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, new AdminAccessService(ctx), _cache, _notifier, new CreateSubmissionRequestValidator(), NullLogger<SubmissionService>.Instance);

    [Fact]
    public async Task Create_Valid_InvalidatesCacheAndNotifies()
    {
        Guid adminId, studentId; int taskId, subjectId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        adminId = admin.Id; studentId = student.Id; taskId = task.Id; subjectId = subject.Id;

        var sut = CreateSut(ctx);
        await sut.CreateAsync(adminId, new CreateSubmissionRequest(studentId, taskId, SubmissionStatus.Accepted, null));

        ctx.Submissions.Should().ContainSingle();
        _cache.Received().Invalidate(subjectId);
        await _notifier.Received(1).ResultUpdatedAsync(subjectId, Arg.Any<ResultUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenSubjectRestricted_Throws()
    {
        Guid adminId, studentId; int taskId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        adminId = admin.Id; studentId = student.Id; taskId = task.Id;

        await new AdminAccessService(ctx).SetSubjectRestrictedAsync(adminId, subject.Id, true);

        var sut = CreateSut(ctx);
        var act = () => sut.CreateAsync(adminId, new CreateSubmissionRequest(studentId, taskId, SubmissionStatus.Accepted, null));
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Create_WhenStudentGroupRestricted_Throws()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        await new AdminAccessService(ctx).SetGroupRestrictedAsync(admin.Id, group.Id, true);

        var act = () => CreateSut(ctx).CreateAsync(admin.Id,
            new CreateSubmissionRequest(student.Id, task.Id, SubmissionStatus.Accepted, null));

        await act.Should().ThrowAsync<ForbiddenException>();
        ctx.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenStudentGroupIsNotLinked_RejectsWholeOperation()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var act = () => sut.CreateAsync(admin.Id,
            new CreateSubmissionRequest(student.Id, task.Id, SubmissionStatus.Accepted, null));

        var error = await act.Should().ThrowAsync<ConflictException>();
        error.Which.Code.Should().Be("student.not_enrolled");
        ctx.Submissions.Should().BeEmpty();
        _cache.DidNotReceive().Invalidate(subject.Id);
    }

    [Fact]
    public async Task Create_WhenNotificationFails_KeepsCommittedSubmission()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(group, subject);
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        _notifier.ResultUpdatedAsync(subject.Id, Arg.Any<ResultUpdateDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SignalR unavailable")));

        var sut = CreateSut(ctx);
        await sut.CreateAsync(admin.Id,
            new CreateSubmissionRequest(student.Id, task.Id, SubmissionStatus.Accepted, null));

        ctx.Submissions.Should().ContainSingle();
        _cache.Received().Invalidate(subject.Id);
    }

    [Fact]
    public async Task Create_UnknownTask_ThrowsNotFound()
    {
        Guid adminId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        adminId = admin.Id;

        var sut = CreateSut(ctx);
        var act = () => sut.CreateAsync(adminId, new CreateSubmissionRequest(Guid.NewGuid(), 999, SubmissionStatus.Accepted, null));
        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose() => _db.Dispose();
}

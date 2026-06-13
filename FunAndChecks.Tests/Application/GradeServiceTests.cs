using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Grades;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class GradeServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();
    private readonly IResultsNotifier _notifier = Substitute.For<IResultsNotifier>();

    private GradeService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx,
            new AdminAccessService(ctx),
            _cache,
            _notifier,
            new CreateGradeComponentRequestValidator(),
            new UpdateGradeComponentRequestValidator(),
            new SetGradeRequestValidator());

    [Fact]
    public async Task CreateComponent_AddsColumn_AndInvalidatesCache()
    {
        Guid adminId; int subjectId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        adminId = admin.Id; subjectId = subject.Id;

        var sut = CreateSut(ctx);
        var result = await sut.CreateComponentAsync(adminId, subjectId, new CreateGradeComponentRequest("Exam", 0, 50));

        result.MaxPoints.Should().Be(50);
        (await sut.GetComponentsAsync(subjectId)).Should().ContainSingle();
        _cache.Received().Invalidate(subjectId);
    }

    [Fact]
    public async Task CreateComponent_WhenRestricted_Throws()
    {
        Guid adminId; int subjectId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        adminId = admin.Id; subjectId = subject.Id;

        await new AdminAccessService(ctx).SetSubjectRestrictedAsync(adminId, subjectId, true);

        var sut = CreateSut(ctx);
        var act = () => sut.CreateComponentAsync(adminId, subjectId, new CreateGradeComponentRequest("Exam", 0, 50));
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SetGrade_IsUpsert_AndNotifies()
    {
        Guid adminId, studentId; int componentId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var component = ctx.Component(subject, maxPoints: 100);
        await ctx.SaveChangesAsync();
        adminId = admin.Id; studentId = student.Id; componentId = component.Id;

        var sut = CreateSut(ctx);
        await sut.SetGradeAsync(adminId, componentId, studentId, new SetGradeRequest(40, "ok"));
        await sut.SetGradeAsync(adminId, componentId, studentId, new SetGradeRequest(80, "better"));

        var grades = await sut.GetStudentGradesAsync(studentId, subject.Id);
        grades.Should().ContainSingle();
        grades[0].Points.Should().Be(80);
        await _notifier.Received(2).GradeUpdatedAsync(subject.Id, Arg.Any<GradeUpdateDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetGrade_ExceedingMax_Throws()
    {
        Guid adminId, studentId; int componentId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var component = ctx.Component(subject, maxPoints: 100);
        await ctx.SaveChangesAsync();
        adminId = admin.Id; studentId = student.Id; componentId = component.Id;

        var sut = CreateSut(ctx);
        var act = () => sut.SetGradeAsync(adminId, componentId, studentId, new SetGradeRequest(101, null));
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SetGrade_BelowMin_Throws()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var component = ctx.Component(subject, maxPoints: 5, minPoints: 2);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var act = () => sut.SetGradeAsync(admin.Id, component.Id, student.Id, new SetGradeRequest(1, null));
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SetGrade_WithinRange_Succeeds()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var component = ctx.Component(subject, maxPoints: 5, minPoints: 2);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        await sut.SetGradeAsync(admin.Id, component.Id, student.Id, new SetGradeRequest(4, "хорошо"));

        var grades = await sut.GetStudentGradesAsync(student.Id, subject.Id);
        grades.Should().ContainSingle();
        grades[0].Points.Should().Be(4);
        grades[0].MinPoints.Should().Be(2);
    }

    [Fact]
    public async Task UpdateComponent_ChangesRange_AndInvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var component = ctx.Component(subject, maxPoints: 100);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var updated = await sut.UpdateComponentAsync(admin.Id, component.Id, new UpdateGradeComponentRequest("Курсовая", 2, 5));

        updated.Name.Should().Be("Курсовая");
        updated.MinPoints.Should().Be(2);
        updated.MaxPoints.Should().Be(5);
        _cache.Received().Invalidate(subject.Id);
    }

    public void Dispose() => _db.Dispose();
}

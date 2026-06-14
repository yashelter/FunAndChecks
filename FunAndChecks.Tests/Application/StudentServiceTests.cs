using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class StudentServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();

    private StudentService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, _cache, new SetStudentColorRequestValidator());

    [Fact]
    public async Task GetStudentsBySubject_ReturnsOnlyLinkedGroups()
    {
        int subjectId;
        await using var ctx = _db.NewContext();
        var linkedGroup = ctx.Group("Linked");
        var otherGroup = ctx.Group("Other");
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        ctx.LinkGroupSubject(linkedGroup, subject);
        var inSubject = ctx.Student(linkedGroup, "Linked");
        ctx.Student(otherGroup, "Other");
        await ctx.SaveChangesAsync();
        subjectId = subject.Id;

        _identity.GetEmailsAsync(Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, string?> { [inSubject.Id] = "linked@example.com" });

        var sut = CreateSut(ctx);
        var students = await sut.GetStudentsBySubjectAsync(subjectId);

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
        var details = await sut.GetDetailsAsync(studentId);
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
        var found = await sut.SearchStudentsAsync("petr");

        found.Should().ContainSingle();
        found[0].LastName.Should().Be("Petrov");
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
        await sut.SetColorAsync(student.Id, new SetStudentColorRequest("#228822"));

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
        await sut.SetColorAsync(student.Id, new SetStudentColorRequest(null));

        var updated = await ctx.Students.FindAsync(student.Id);
        updated!.Color.Should().BeNull();
    }

    public void Dispose() => _db.Dispose();
}

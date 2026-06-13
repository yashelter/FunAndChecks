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

    private StudentService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, new UpdateMyProfileRequestValidator());

    [Fact]
    public async Task UpdateMyProfile_UpdatesGitHubAndColor()
    {
        Guid studentId;
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        await ctx.SaveChangesAsync();
        studentId = student.Id;

        var sut = CreateSut(ctx);
        await sut.UpdateMyProfileAsync(studentId, new UpdateMyProfileRequest("https://github.com/x", "#aabbcc"));

        var updated = await ctx.Students.FindAsync(studentId);
        updated!.GitHubUrl.Should().Be("https://github.com/x");
        updated.Color.Should().Be("#aabbcc");
    }

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

    public void Dispose() => _db.Dispose();
}

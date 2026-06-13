using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class AdminServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private AdminService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, new CreateAdminRequestValidator(), new UpdateAdminRequestValidator());

    [Fact]
    public async Task Create_Valid_CreatesProfileWithConfirmedEmail()
    {
        _identity.CreateAccountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(), true, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                using var c = _db.NewContext();
                Seed.AddAccount(c, ci.ArgAt<Guid>(0));
                c.SaveChanges();
                return AccountResult.Success();
            });

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var id = await sut.CreateAsync(new CreateAdminRequest("Super", "Admin", "sa@example.com", "secret", "#123456", "S", true));

        (await ctx.Admins.FindAsync(id)).Should().NotBeNull();
        await _identity.Received(1).CreateAccountAsync(id, "sa@example.com", "secret",
            Arg.Is<IEnumerable<string>>(r => r.Contains("SuperAdmin")), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_Self_Throws()
    {
        Guid adminId;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        adminId = admin.Id;

        var sut = CreateSut(ctx);
        var act = () => sut.DeleteAsync(adminId, adminId);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Delete_WithSubmissions_Throws()
    {
        Guid actingId, targetId;
        await using var ctx = _db.NewContext();
        var acting = ctx.Admin("X");
        var target = ctx.Admin("Y");
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();
        var student = ctx.Student(group);
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();
        ctx.Submissions.Add(new Submission
        {
            StudentId = student.Id, TaskId = task.Id, AdminId = target.Id,
            Status = SubmissionStatus.Accepted, SubmittedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
        actingId = acting.Id; targetId = target.Id;

        var sut = CreateSut(ctx);
        var act = () => sut.DeleteAsync(actingId, targetId);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Delete_Valid_RemovesProfileAndAccount()
    {
        Guid actingId, targetId;
        await using var ctx = _db.NewContext();
        var acting = ctx.Admin("X");
        var target = ctx.Admin("Y");
        await ctx.SaveChangesAsync();
        actingId = acting.Id; targetId = target.Id;

        var sut = CreateSut(ctx);
        await sut.DeleteAsync(actingId, targetId);

        (await ctx.Admins.FindAsync(targetId)).Should().BeNull();
        await _identity.Received(1).DeleteAccountAsync(targetId, Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}

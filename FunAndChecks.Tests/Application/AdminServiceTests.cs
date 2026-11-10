using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
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
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();

    private AdminService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, _cache, new CreateAdminRequestValidator(), new UpdateAdminRequestValidator());

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

    [Fact]
    public async Task Update_AdminHasActivity_InvalidatesCacheForAffectedSubjectsOnly()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subjectWithSubmission = ctx.Subject("S1");
        var subjectWithOtherAdminsGrade = ctx.Subject("S2");
        var untouchedSubject = ctx.Subject("S3");
        var admin = ctx.Admin();
        var otherAdmin = ctx.Admin("B");
        await ctx.SaveChangesAsync();

        var student = ctx.Student(group);
        var task = ctx.Task(subjectWithSubmission);
        var component = ctx.Component(subjectWithOtherAdminsGrade);
        await ctx.SaveChangesAsync();

        ctx.Submissions.Add(new Submission
        {
            Status = SubmissionStatus.Accepted,
            SubmittedAt = DateTime.UtcNow,
            StudentId = student.Id,
            TaskId = task.Id,
            AdminId = admin.Id,
        });
        ctx.StudentGrades.Add(new StudentGrade
        {
            Points = 5,
            UpdatedAt = DateTime.UtcNow,
            GradeComponentId = component.Id,
            StudentId = student.Id,
            AdminId = otherAdmin.Id,
        });
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        await sut.UpdateAsync(Guid.NewGuid(), admin.Id, new UpdateAdminRequest("New", "Name", "#223344", "N"));

        var updated = await ctx.Admins.FindAsync(admin.Id);
        updated!.FirstName.Should().Be("New");
        updated.Letter.Should().Be("N");

        _cache.Received(1).Invalidate(subjectWithSubmission.Id);
        _cache.DidNotReceive().Invalidate(subjectWithOtherAdminsGrade.Id);
        _cache.DidNotReceive().Invalidate(untouchedSubject.Id);
    }

    [Fact]
    public async Task Update_AdminWithoutActivity_DoesNotInvalidateCache()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        await sut.UpdateAsync(Guid.NewGuid(), admin.Id, new UpdateAdminRequest("New", "Name", "#223344", "N"));

        _cache.DidNotReceiveWithAnyArgs().Invalidate(default!);
    }

    [Fact]
    public async Task Update_ProfileOnly_DoesNotTouchAccountOrRoles()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();

        await CreateSut(ctx).UpdateAsync(Guid.NewGuid(), admin.Id, new UpdateAdminRequest("New", "Name", null, null));

        await _identity.DidNotReceiveWithAnyArgs().UpdateAccountAdminAsync(default, default!, default);
        await _identity.DidNotReceiveWithAnyArgs().SetRoleAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Update_NewEmailAndPassword_UpdatesAccount()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        _identity.GetEmailAsync(admin.Id).Returns("old@example.com");

        await CreateSut(ctx).UpdateAsync(Guid.NewGuid(), admin.Id,
            new UpdateAdminRequest("A", "B", null, null, " new@example.com ", "secret1"));

        await _identity.Received(1).UpdateAccountAdminAsync(admin.Id, "new@example.com", "secret1");
    }

    [Fact]
    public async Task Update_SameEmailWithoutPassword_KeepsAccountUntouched()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        _identity.GetEmailAsync(admin.Id).Returns("same@example.com");

        await CreateSut(ctx).UpdateAsync(Guid.NewGuid(), admin.Id,
            new UpdateAdminRequest("A", "B", null, null, "SAME@example.com", ""));

        await _identity.DidNotReceiveWithAnyArgs().UpdateAccountAdminAsync(default, default!, default);
    }

    [Fact]
    public async Task Update_OnlyPassword_KeepsCurrentEmail()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        _identity.GetEmailAsync(admin.Id).Returns("cur@example.com");

        await CreateSut(ctx).UpdateAsync(Guid.NewGuid(), admin.Id,
            new UpdateAdminRequest("A", "B", null, null, null, "secret1"));

        await _identity.Received(1).UpdateAccountAdminAsync(admin.Id, "cur@example.com", "secret1");
    }

    [Fact]
    public async Task Update_GrantSuperAdmin_SetsRole()
    {
        await using var ctx = _db.NewContext();
        var acting = ctx.Admin("X");
        var admin = ctx.Admin("Y");
        await ctx.SaveChangesAsync();
        _identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns([acting.Id]);

        await CreateSut(ctx).UpdateAsync(acting.Id, admin.Id, new UpdateAdminRequest("A", "B", null, null, IsSuperAdmin: true));

        await _identity.Received(1).SetRoleAsync(admin.Id, Roles.SuperAdmin, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_RevokeSuperAdminFromOther_RemovesRole()
    {
        await using var ctx = _db.NewContext();
        var acting = ctx.Admin("X");
        var admin = ctx.Admin("Y");
        await ctx.SaveChangesAsync();
        _identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns([acting.Id, admin.Id]);

        await CreateSut(ctx).UpdateAsync(acting.Id, admin.Id, new UpdateAdminRequest("A", "B", null, null, IsSuperAdmin: false));

        await _identity.Received(1).SetRoleAsync(admin.Id, Roles.SuperAdmin, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_UnchangedSuperAdminFlag_DoesNotSetRole()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        _identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns([admin.Id]);

        await CreateSut(ctx).UpdateAsync(admin.Id, admin.Id, new UpdateAdminRequest("A", "B", null, null, IsSuperAdmin: true));

        await _identity.DidNotReceiveWithAnyArgs().SetRoleAsync(default, default!, default, default);
    }

    [Fact]
    public async Task Update_RevokeOwnSuperAdmin_ThrowsAndKeepsProfile()
    {
        Guid adminId;
        await using (var ctx = _db.NewContext())
        {
            var admin = ctx.Admin();
            var other = ctx.Admin("Z");
            await ctx.SaveChangesAsync();
            adminId = admin.Id;
            _identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns([admin.Id, other.Id]);

            var act = () => CreateSut(ctx).UpdateAsync(admin.Id, admin.Id,
                new UpdateAdminRequest("Changed", "B", null, null, IsSuperAdmin: false));

            (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("admin.cannot_demote_self");
        }

        await _identity.DidNotReceiveWithAnyArgs().SetRoleAsync(default, default!, default, default);
        await using var check = _db.NewContext();
        (await check.Admins.FindAsync(adminId))!.FirstName.Should().NotBe("Changed");
    }

    [Fact]
    public async Task Update_RevokeLastSuperAdmin_Throws()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        await ctx.SaveChangesAsync();
        _identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns([admin.Id]);

        var act = () => CreateSut(ctx).UpdateAsync(Guid.NewGuid(), admin.Id,
            new UpdateAdminRequest("A", "B", null, null, IsSuperAdmin: false));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("admin.last_superadmin");
    }

    [Fact]
    public async Task GetDetails_ReturnsEmailAndSuperAdminFlag()
    {
        await using var ctx = _db.NewContext();
        var super = ctx.Admin("S");
        var plain = ctx.Admin("P");
        await ctx.SaveChangesAsync();
        _identity.GetEmailsAsync(Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, string?> { [super.Id] = "s@example.com", [plain.Id] = "p@example.com" });
        _identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, Arg.Any<CancellationToken>()).Returns([super.Id]);

        var details = await CreateSut(ctx).GetDetailsAsync();

        details.Should().ContainSingle(d => d.Id == super.Id && d.Email == "s@example.com" && d.IsSuperAdmin);
        details.Should().ContainSingle(d => d.Id == plain.Id && d.Email == "p@example.com" && !d.IsSuperAdmin);
    }

    [Fact]
    public void UpdateValidator_ChecksOptionalEmailAndPassword()
    {
        var validator = new UpdateAdminRequestValidator();
        validator.Validate(new UpdateAdminRequest("A", "B", null, null, null, "123")).IsValid.Should().BeFalse();
        validator.Validate(new UpdateAdminRequest("A", "B", null, null, "bad", null)).IsValid.Should().BeFalse();
        validator.Validate(new UpdateAdminRequest("A", "B", null, null, null, "")).IsValid.Should().BeTrue();
        validator.Validate(new UpdateAdminRequest("A", "B", null, null, "ok@example.com", "secret1")).IsValid.Should().BeTrue();
    }

    public void Dispose() => _db.Dispose();
}

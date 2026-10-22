using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Tasks;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Persistence.Migrations;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class SubjectPermissionsTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task MissingSubjectAccess_IsDenied_WhileNewGroupsRemainAllowed()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        var group = db.Group();
        await db.SaveChangesAsync();
        var access = db.Access();
        (await access.IsSubjectRestrictedAsync(admin.Id, subject.Id)).Should().BeTrue();
        (await access.GetAccessAsync(admin.Id)).RestrictedSubjectIds.Should().Equal(subject.Id);
        var denied = () => access.EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        (await denied.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("access.subject_restricted");
        await access.EnsureGroupAllowedAsync(admin.Id, group.Id);
        (await access.IsGroupRestrictedAsync(admin.Id, group.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HidingAnUngrantedSubject_CannotGrantAccess()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        await db.SaveChangesAsync();
        var access = db.Access();
        await access.SetSubjectHiddenAsync(admin.Id, subject.Id, true);
        var stored = await db.AdminSubjectAccesses.SingleAsync();
        stored.IsRestricted.Should().BeTrue();
        stored.IsHidden.Should().BeTrue();
        (await access.IsSubjectRestrictedAsync(admin.Id, subject.Id)).Should().BeTrue();
        await access.SetSubjectHiddenAsync(admin.Id, subject.Id, false);
        (await access.IsSubjectRestrictedAsync(admin.Id, subject.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ExplicitGrant_SurvivesArchiveRoundTrip_AndRevocationRemovesIt()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        await db.SaveChangesAsync();
        var access = db.Access();
        await access.SetSubjectRestrictedAsync(admin.Id, subject.Id, false);
        await access.EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        await access.SetSubjectHiddenAsync(admin.Id, subject.Id, true);
        var hidden = () => access.EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        (await hidden.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("access.subject_hidden");
        await access.SetSubjectHiddenAsync(admin.Id, subject.Id, false);
        await using var reopened = _db.NewContext();
        await reopened.Access().EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        await reopened.Access().SetSubjectRestrictedAsync(admin.Id, subject.Id, true);
        (await reopened.Access().IsSubjectRestrictedAsync(admin.Id, subject.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task SavingExplicitSelection_DoesNotGrantNewSubjects_AndPreservesGroupDefaultsAndArchives()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var selected = db.Subject("Selected");
        var archived = db.Subject("Archived");
        var group = db.Group();
        await db.SaveChangesAsync();
        var access = db.Access();
        await access.SetSubjectHiddenAsync(admin.Id, archived.Id, true);
        // A subject appears after the administrator loaded the editing dialog.
        var addedWhileEditing = db.Subject("New");
        await db.SaveChangesAsync();
        await access.ReplacePermissionsAsync(admin.Id, new([selected.Id, selected.Id], []));
        await access.EnsureSubjectAllowedAsync(admin.Id, selected.Id);
        (await access.IsSubjectRestrictedAsync(admin.Id, addedWhileEditing.Id)).Should().BeTrue();
        (await access.GetAccessAsync(admin.Id)).HiddenSubjectIds.Should().Equal(archived.Id);
        await access.EnsureGroupAllowedAsync(admin.Id, group.Id);
        (await db.AdminSubjectAccesses.CountAsync(a => a.AdminId == admin.Id && !a.IsRestricted)).Should().Be(1);
    }

    [Fact]
    public async Task InvalidGrant_LeavesBothListsUnchanged()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        var group = db.Group();
        db.GrantSubject(admin, subject);
        await db.SaveChangesAsync();
        var access = db.Access();
        var action = () => access.ReplacePermissionsAsync(admin.Id, new([int.MaxValue], [group.Id]));
        await action.Should().ThrowAsync<NotFoundException>();
        await using var reopened = _db.NewContext();
        await reopened.Access().EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        await reopened.Access().EnsureGroupAllowedAsync(admin.Id, group.Id);
    }

    [Fact]
    public async Task SuperAdminBypass_UsesCurrentRoles_AndStillHonorsPersonalArchives()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        var group = db.Group();
        await db.SaveChangesAsync();
        var roles = new List<string> { Roles.Admin, Roles.SuperAdmin };
        var identity = Substitute.For<IIdentityService>();
        identity.GetCurrentRolesAsync(admin.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyCollection<string>>(roles));
        var access = new AdminAccessService(db, identity);
        await access.SetGroupRestrictedAsync(admin.Id, group.Id, true);
        await access.EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        var restrictedGroup = () => access.EnsureGroupAllowedAsync(admin.Id, group.Id);
        await restrictedGroup.Should().ThrowAsync<ForbiddenException>();
        (await access.GetAccessAsync(admin.Id)).RestrictedSubjectIds.Should().BeEmpty();
        await access.SetSubjectHiddenAsync(admin.Id, subject.Id, true);
        var hidden = () => access.EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        (await hidden.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("access.subject_hidden");
        await access.SetSubjectHiddenAsync(admin.Id, subject.Id, false);
        roles.Remove(Roles.SuperAdmin);
        var denied = () => access.EnsureSubjectAllowedAsync(admin.Id, subject.Id);
        await denied.Should().ThrowAsync<ForbiddenException>();
        var deniedGroup = () => access.EnsureGroupAllowedAsync(admin.Id, group.Id);
        await deniedGroup.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SubjectAndGroupLists_ExposeOnlyExplicitlyGrantedSubjects()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var granted = db.Subject("Granted");
        var hidden = db.Subject("Hidden");
        var denied = db.Subject("Denied");
        var group = db.Group();
        var hiddenGroup = db.Group("Hidden group");
        db.GrantSubject(admin, granted);
        db.GrantSubject(admin, hidden);
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, granted);
        db.LinkGroupSubject(group, hidden);
        db.LinkGroupSubject(group, denied);
        db.LinkGroupSubject(hiddenGroup, granted);
        await db.SaveChangesAsync();
        var access = db.Access();
        await access.SetSubjectHiddenAsync(admin.Id, hidden.Id, true);
        await access.SetGroupHiddenAsync(admin.Id, hiddenGroup.Id, true);
        var cache = Substitute.For<IResultsCacheService>();
        var subjects = new SubjectService(db, cache, access,
            new CreateSubjectRequestValidator(), new UpdateSubjectRequestValidator(),
            new CreateTaskRequestValidator(), new UpdateTaskRequestValidator(),
            new CreateTaskGroupRequestValidator(), new UpdateTaskGroupRequestValidator());
        var groups = new GroupService(db, Substitute.For<IIdentityService>(), cache, access,
            new CreateGroupRequestValidator(), new UpdateGroupRequestValidator());
        (await subjects.GetVisibleForAdminAsync(admin.Id)).Select(s => s.Id).Should().Equal(granted.Id);
        (await groups.GetSubjectIdsAsync(admin.Id, group.Id)).Should().Equal(granted.Id);
        (await groups.GetVisibleForAdminAsync(admin.Id)).Should().ContainSingle();
        // Обзор всех доступов разом фильтрует так же, как доступ одной группы.
        var links = await groups.GetSubjectLinksAsync(admin.Id);
        links.Should().ContainSingle().Which.Should().BeEquivalentTo(new GroupSubjectsDto(group.Id, [granted.Id]));
    }

    [Fact]
    public async Task PermissionMigration_PreservesOldAccess_AndDoesNotGrantFutureEntities()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var originallyAllowed = db.Subject("Allowed");
        var hidden = db.Subject("Hidden");
        var denied = db.Subject("Denied");
        db.AdminSubjectAccesses.AddRange(
            new AdminSubjectAccess { Admin = admin, Subject = hidden, IsHidden = true },
            new AdminSubjectAccess { Admin = admin, Subject = denied, IsRestricted = true });
        await db.SaveChangesAsync();
        var sql = new RequireExplicitSubjectAccess().UpOperations.OfType<SqlOperation>().Single().Sql;
        await db.Database.ExecuteSqlRawAsync(sql);
        await db.Access().EnsureSubjectAllowedAsync(admin.Id, originallyAllowed.Id);
        (await db.Access().GetAccessAsync(admin.Id)).HiddenSubjectIds.Should().Equal(hidden.Id);
        (await db.Access().IsSubjectRestrictedAsync(admin.Id, denied.Id)).Should().BeTrue();
        var futureSubject = db.Subject("Future");
        var futureAdmin = db.Admin("Future");
        await db.SaveChangesAsync();
        (await db.Access().IsSubjectRestrictedAsync(admin.Id, futureSubject.Id)).Should().BeTrue();
        (await db.Access().IsSubjectRestrictedAsync(futureAdmin.Id, originallyAllowed.Id)).Should().BeTrue();
    }

    public void Dispose() => _db.Dispose();
}

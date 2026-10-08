using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class AdminAccessServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task SetSubjectRestricted_CreatesRecord_AndEnsureThrows()
    {
        Guid adminId;
        int subjectId;
        await using (var ctx = _db.NewContext())
        {
            var admin = ctx.Admin();
            var subject = ctx.Subject();
            await ctx.SaveChangesAsync();
            adminId = admin.Id;
            subjectId = subject.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var sut = new AdminAccessService(ctx);
            await sut.SetSubjectRestrictedAsync(adminId, subjectId, restricted: true);
        }

        await using (var ctx = _db.NewContext())
        {
            var sut = new AdminAccessService(ctx);
            (await sut.IsSubjectRestrictedAsync(adminId, subjectId)).Should().BeTrue();
            var act = () => sut.EnsureSubjectAllowedAsync(adminId, subjectId);
            await act.Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public async Task ClearingBothFlags_RemovesRecord()
    {
        Guid adminId;
        int subjectId;
        await using (var ctx = _db.NewContext())
        {
            var admin = ctx.Admin();
            var subject = ctx.Subject();
            await ctx.SaveChangesAsync();
            adminId = admin.Id;
            subjectId = subject.Id;

            var sut = new AdminAccessService(ctx);
            await sut.SetSubjectHiddenAsync(adminId, subjectId, hidden: true);
            await sut.SetSubjectHiddenAsync(adminId, subjectId, hidden: false);
        }

        await using (var ctx = _db.NewContext())
        {
            var sut = new AdminAccessService(ctx);
            var access = await sut.GetAccessAsync(adminId);
            access.HiddenSubjectIds.Should().BeEmpty();
            access.RestrictedSubjectIds.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task GetAccess_SeparatesRestrictedAndHidden()
    {
        Guid adminId;
        int restrictedSubject, hiddenGroup;
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var subject = ctx.Subject();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        adminId = admin.Id;
        restrictedSubject = subject.Id;
        hiddenGroup = group.Id;

        var sut = new AdminAccessService(ctx);
        await sut.SetSubjectRestrictedAsync(adminId, restrictedSubject, true);
        await sut.SetGroupHiddenAsync(adminId, hiddenGroup, true);

        var access = await sut.GetAccessAsync(adminId);
        access.RestrictedSubjectIds.Should().ContainSingle().Which.Should().Be(restrictedSubject);
        access.HiddenGroupIds.Should().ContainSingle().Which.Should().Be(hiddenGroup);
    }

    [Fact]
    public async Task SetRestriction_ForUnknownAdmin_Throws()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();

        var sut = new AdminAccessService(ctx);
        var act = () => sut.SetSubjectRestrictedAsync(Guid.NewGuid(), subject.Id, true);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ReplaceRestrictions_UpdatesBothLists_PreservesArchives_AndRemovesEmptyRecords()
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var archivedSubject = ctx.Subject("Archived");
        var previouslyRestrictedSubject = ctx.Subject("Previous");
        var newSubject = ctx.Subject("New");
        var archivedGroup = ctx.Group("Archived");
        var previouslyRestrictedGroup = ctx.Group("Previous");
        await ctx.SaveChangesAsync();
        var sut = new AdminAccessService(ctx);
        await sut.SetSubjectHiddenAsync(admin.Id, archivedSubject.Id, true);
        await sut.SetSubjectRestrictedAsync(admin.Id, previouslyRestrictedSubject.Id, true);
        await sut.SetGroupHiddenAsync(admin.Id, archivedGroup.Id, true);
        await sut.SetGroupRestrictedAsync(admin.Id, previouslyRestrictedGroup.Id, true);

        // Duplicate selection is accepted without creating duplicate access records.
        await sut.ReplaceRestrictionsAsync(admin.Id, new ReplaceAdminRestrictionsRequest(
            [archivedSubject.Id, newSubject.Id, newSubject.Id], [archivedGroup.Id]));

        var access = await sut.GetAccessAsync(admin.Id);
        access.RestrictedSubjectIds.Should().BeEquivalentTo([archivedSubject.Id, newSubject.Id]);
        access.RestrictedGroupIds.Should().Equal(archivedGroup.Id);
        access.HiddenSubjectIds.Should().Equal(archivedSubject.Id);
        access.HiddenGroupIds.Should().Equal(archivedGroup.Id);
        (await ctx.AdminSubjectAccesses.AnyAsync(a => a.AdminId == admin.Id && a.SubjectId == previouslyRestrictedSubject.Id)).Should().BeFalse();
        (await ctx.AdminGroupAccesses.AnyAsync(a => a.AdminId == admin.Id && a.GroupId == previouslyRestrictedGroup.Id)).Should().BeFalse();

        await sut.ReplaceRestrictionsAsync(admin.Id, new ReplaceAdminRestrictionsRequest([], []));
        access = await sut.GetAccessAsync(admin.Id);
        access.RestrictedSubjectIds.Should().BeEmpty();
        access.RestrictedGroupIds.Should().BeEmpty();
        access.HiddenSubjectIds.Should().Equal(archivedSubject.Id);
        access.HiddenGroupIds.Should().Equal(archivedGroup.Id);
        (await ctx.AdminSubjectAccesses.CountAsync(a => a.AdminId == admin.Id)).Should().Be(1);
        (await ctx.AdminGroupAccesses.CountAsync(a => a.AdminId == admin.Id)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplaceRestrictions_RejectsUnknownEntity_WithoutPartialChanges(bool unknownSubject)
    {
        await using var ctx = _db.NewContext();
        var admin = ctx.Admin();
        var subject = ctx.Subject();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        var sut = new AdminAccessService(ctx);
        await sut.SetSubjectRestrictedAsync(admin.Id, subject.Id, true);
        await sut.SetGroupHiddenAsync(admin.Id, group.Id, true);

        var request = unknownSubject
            ? new ReplaceAdminRestrictionsRequest([int.MaxValue], [group.Id])
            : new ReplaceAdminRestrictionsRequest([], [int.MaxValue]);
        var act = () => sut.ReplaceRestrictionsAsync(admin.Id, request);
        await act.Should().ThrowAsync<NotFoundException>();

        await using var verify = _db.NewContext();
        var access = await new AdminAccessService(verify).GetAccessAsync(admin.Id);
        access.RestrictedSubjectIds.Should().Equal(subject.Id);
        access.RestrictedGroupIds.Should().BeEmpty();
        access.HiddenGroupIds.Should().Equal(group.Id);
    }

    [Fact]
    public async Task ReplaceRestrictions_RejectsUnknownAdmin()
    {
        await using var ctx = _db.NewContext();
        var sut = new AdminAccessService(ctx);
        var act = () => sut.ReplaceRestrictionsAsync(Guid.NewGuid(), new ReplaceAdminRestrictionsRequest([], []));
        await act.Should().ThrowAsync<NotFoundException>();
    }

    public void Dispose() => _db.Dispose();
}

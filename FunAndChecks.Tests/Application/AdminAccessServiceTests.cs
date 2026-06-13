using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Tests.Common;
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

    public void Dispose() => _db.Dispose();
}

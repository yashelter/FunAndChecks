using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Groups;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class GroupServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();

    private GroupService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, _cache, new CreateGroupRequestValidator(), new UpdateGroupRequestValidator());

    [Fact]
    public async Task Update_UpdatesGroupAndInvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group("Old Name");
        var subject1 = ctx.Subject("S1");
        var subject2 = ctx.Subject("S2");
        await ctx.SaveChangesAsync();

        ctx.LinkGroupSubject(group, subject1);
        ctx.LinkGroupSubject(group, subject2);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);
        var req = new UpdateGroupRequest("New Name");

        await sut.UpdateAsync(group.Id, req);

        var updated = await ctx.Groups.FindAsync(group.Id);
        updated!.Name.Should().Be("New Name");

        _cache.Received(1).Invalidate(subject1.Id);
        _cache.Received(1).Invalidate(subject2.Id);
    }

    [Fact]
    public async Task LinkSubject_LinksAndInvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        await sut.LinkSubjectAsync(group.Id, subject.Id);

        var links = ctx.GroupSubjects.Where(gs => gs.GroupId == group.Id && gs.SubjectId == subject.Id).ToList();
        links.Should().ContainSingle();

        _cache.Received(1).Invalidate(subject.Id);
    }

    [Fact]
    public async Task UnlinkSubject_UnlinksAndInvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        var subject = ctx.Subject();
        await ctx.SaveChangesAsync();

        ctx.LinkGroupSubject(group, subject);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        await sut.UnlinkSubjectAsync(group.Id, subject.Id);

        var links = ctx.GroupSubjects.Where(gs => gs.GroupId == group.Id && gs.SubjectId == subject.Id).ToList();
        links.Should().BeEmpty();

        _cache.Received(1).Invalidate(subject.Id);
    }

    public void Dispose() => _db.Dispose();
}

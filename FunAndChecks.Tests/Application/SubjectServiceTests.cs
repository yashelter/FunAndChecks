using FluentAssertions;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Tests.Common;
using NSubstitute;
using Xunit;
using FunAndChecks.Application.Tasks;

namespace FunAndChecks.Tests.Application;

public class SubjectServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();
    private readonly IAdminAccessService _access = Substitute.For<IAdminAccessService>();

    private SubjectService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _cache, _access, 
            new CreateSubjectRequestValidator(), 
            new UpdateSubjectRequestValidator(),
            new CreateTaskRequestValidator(),
            new UpdateTaskRequestValidator());

    [Fact]
    public async Task UpdateSubject_InvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("Old Name");
        await ctx.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = CreateSut(ctx);

        var req = new UpdateSubjectRequest("New Name");
        await sut.UpdateAsync(adminId, subject.Id, req);

        _cache.Received(1).Invalidate(subject.Id);
    }

    [Fact]
    public async Task CreateTask_InvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S1");
        await ctx.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = CreateSut(ctx);

        var req = new CreateTaskRequest("T1", "Desc", 10);
        await sut.CreateTaskAsync(adminId, subject.Id, req);

        _cache.Received(1).Invalidate(subject.Id);
    }

    [Fact]
    public async Task UpdateTask_InvalidatesCache()
    {
        await using var ctx = _db.NewContext();
        var subject = ctx.Subject("S1");
        await ctx.SaveChangesAsync();
        var task = ctx.Task(subject);
        await ctx.SaveChangesAsync();

        var adminId = Guid.NewGuid();
        var sut = CreateSut(ctx);

        var req = new UpdateTaskRequest("T1", "Desc", 15);
        await sut.UpdateTaskAsync(adminId, task.Id, req);

        _cache.Received(1).Invalidate(subject.Id);
    }

    public void Dispose() => _db.Dispose();
}

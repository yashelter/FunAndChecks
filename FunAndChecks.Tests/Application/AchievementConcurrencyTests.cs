using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>Separate connections exercise transaction behavior unavailable in the shared-connection API factory.</summary>
public class AchievementConcurrencyTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"funchecks-achievements-{Guid.NewGuid():N}.db");
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IProfileMediaProcessor _media = Substitute.For<IProfileMediaProcessor>();

    [Fact]
    public async Task ConcurrentGrants_CreateOneActiveAward_AndReturnOneConflict()
    {
        var seed = await SeedAsync();
        using var barrier = new Barrier(2);
        var operations = Enumerable.Range(0, 2).Select(_ => Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await Service(db).GrantAsync(seed.AdminId, seed.AchievementId, new GrantAchievementRequest(seed.StudentId));
                return true;
            }
            catch (ConflictException ex)
            {
                ex.Code.Should().Be("achievement.already_awarded");
                return false;
            }
        })).ToArray();
        (await Task.WhenAll(operations)).Should().BeEquivalentTo([true, false]);
        await using var verify = Context();
        (await verify.AchievementAwards.CountAsync(a => a.RevokedAtUtc == null)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentPinAndRevoke_CannotLeaveARevokedAwardPinned()
    {
        var seed = await SeedAsync();
        int awardId;
        await using (var setup = Context())
            awardId = (await Service(setup).GrantAsync(seed.AdminId, seed.AchievementId, new GrantAchievementRequest(seed.StudentId))).Id;
        using var barrier = new Barrier(2);
        var pin = Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await new ProfileService(db, _identity, _media, new UpdateProfileRequestValidator())
                    .UpdateAsync(seed.StudentId, new UpdateProfileRequest("", null, null, [awardId]));
            }
            catch (ConflictException ex) { ex.Code.Should().Be("profile.pins_invalid"); }
            return true;
        });
        var revoke = Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await Service(db).RevokeAsync(seed.AdminId, awardId);
            return true;
        });
        await Task.WhenAll(pin, revoke);
        await using var verify = Context();
        (await verify.AchievementAwards.SingleAsync()).RevokedAtUtc.Should().NotBeNull();
        (await verify.PinnedAchievements.AnyAsync()).Should().BeFalse();
    }

    private Task<T> Concurrent<T>(Func<ApplicationDbContext, Task<T>> action) => Task.Factory.StartNew(async () =>
    {
        await using var context = Context();
        return await action(context);
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private AchievementService Service(ApplicationDbContext db) => new(db, _identity, _media, new UpsertAchievementRequestValidator());

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_databasePath};Default Timeout=3;Pooling=False;Foreign Keys=True").Options);

    private async Task<(Guid AdminId, Guid StudentId, int AchievementId)> SeedAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var group = db.Group();
        var admin = db.Admin();
        await db.SaveChangesAsync();
        var student = db.Student(group);
        var definition = new AchievementDefinition
            { Name = "Concurrent", CreatedByUserId = admin.Id, CreatedByName = admin.FullName, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        db.AchievementDefinitions.Add(definition);
        await db.SaveChangesAsync();
        var adminProfile = new ProfileAccountInfo(admin.Id, admin.FirstName, admin.LastName, null, true);
        var studentProfile = new ProfileAccountInfo(student.Id, student.FirstName, student.LastName, group.Name, false);
        _identity.GetCurrentRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([Roles.Admin, Roles.SuperAdmin]);
        _identity.GetProfileAccountAsync(admin.Id, Arg.Any<CancellationToken>()).Returns(adminProfile);
        _identity.GetProfileAccountAsync(student.Id, Arg.Any<CancellationToken>()).Returns(studentProfile);
        _identity.GetProfileAccountsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ProfileAccountInfo> { [admin.Id] = adminProfile, [student.Id] = studentProfile });
        return (admin.Id, student.Id, definition.Id);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(_databasePath + suffix)) File.Delete(_databasePath + suffix);
    }
}

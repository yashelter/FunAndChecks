using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Variants;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>«Кто первый — того и вариант»: отдельные соединения, как у двух студентов, нажавших одновременно.</summary>
public class VariantConcurrencyTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"funchecks-variants-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task TwoStudentsClaimingOneUniqueVariant_ExactlyOneWins()
    {
        var (setId, variantId, students) = await SeedAsync();
        using var barrier = new Barrier(2);
        var operations = students.Select(studentId => Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await Service(db).ClaimAsync(studentId, setId, variantId);
                return true;
            }
            catch (ConflictException ex)
            {
                ex.Code.Should().Be("variant.taken");
                return false;
            }
        })).ToArray();

        (await Task.WhenAll(operations)).Should().BeEquivalentTo([true, false]);
        await using var verify = Context();
        (await verify.VariantAssignments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OneStudentDoubleClickingTwoVariants_GetsOnlyOne()
    {
        var (setId, variantId, students) = await SeedAsync();
        int secondId;
        await using (var db = Context())
            secondId = await db.Variants.Where(v => v.Id != variantId).Select(v => v.Id).FirstAsync();
        using var barrier = new Barrier(2);
        var operations = new[] { variantId, secondId }.Select(id => Concurrent(async db =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try
            {
                await Service(db).ClaimAsync(students[0], setId, id);
                return true;
            }
            catch (ConflictException ex)
            {
                ex.Code.Should().Be("variant.already_chosen");
                return false;
            }
        })).ToArray();

        (await Task.WhenAll(operations)).Should().BeEquivalentTo([true, false]);
        await using var verify = Context();
        (await verify.VariantAssignments.CountAsync()).Should().Be(1);
    }

    private Task<T> Concurrent<T>(Func<ApplicationDbContext, Task<T>> action) => Task.Factory.StartNew(async () =>
    {
        await using var context = Context();
        return await action(context);
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private static VariantService Service(ApplicationDbContext db) => new(db, db.Access(),
        new SaveVariantSetRequestValidator(), new AddVariantRequestValidator(), new AddVariantRangeRequestValidator(),
        new UpdateVariantRequestValidator());

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_databasePath};Default Timeout=3;Pooling=False;Foreign Keys=True").Options);

    private async Task<(int SetId, int VariantId, Guid[] Students)> SeedAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var admin = db.Admin();
        var subject = db.Subject();
        db.GrantSubject(admin, subject);
        var group = db.Group();
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var first = db.Student(group, "Alpha");
        var second = db.Student(group, "Beta");
        await db.SaveChangesAsync();
        var set = await Service(db).CreateSetAsync(admin.Id, subject.Id,
            new SaveVariantSetRequest("ЛР", VariantUniqueness.UniqueInSubject, true, null, 1, 2));
        return (set.Id, set.Variants[0].Id, [first.Id, second.Id]);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(_databasePath + suffix)) File.Delete(_databasePath + suffix);
    }
}

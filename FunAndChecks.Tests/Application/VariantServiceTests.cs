using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Variants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class VariantServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private readonly TestDatabase _db = new();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private VariantService Service(ApplicationDbContext db) => new(db, db.Access(),
        new SaveVariantSetRequestValidator(), new AddVariantRequestValidator(), new AddVariantRangeRequestValidator(),
        new UpdateVariantRequestValidator(), _notifications, new FixedClock(Now));

    private sealed record Data(Guid AdminId, int SubjectId, int GroupId, Guid Alpha, Guid Beta, Guid OtherGroup, Guid NotEnrolled);

    private async Task<Data> SeedAsync()
    {
        await using var db = _db.NewContext();
        var admin = db.Admin();
        var subject = db.Subject();
        db.GrantSubject(admin, subject);
        var group = db.Group();
        var second = db.Group("M8O-102");
        var stranger = db.Group("M8O-999");
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        db.LinkGroupSubject(second, subject);
        var alpha = db.Student(group, "Alpha");
        var beta = db.Student(group, "Beta");
        var other = db.Student(second, "Gamma");
        var outsider = db.Student(stranger, "Delta");
        await db.SaveChangesAsync();
        return new(admin.Id, subject.Id, group.Id, alpha.Id, beta.Id, other.Id, outsider.Id);
    }

    private async Task<VariantSetDto> CreateSetAsync(Data data, VariantUniqueness uniqueness, bool open = true, DateTime? deadline = null)
    {
        await using var db = _db.NewContext();
        return await Service(db).CreateSetAsync(data.AdminId, data.SubjectId,
            new SaveVariantSetRequest("ЛР", uniqueness, open, deadline, 1, 3));
    }

    private async Task<StudentVariantSetDto> ClaimAsync(Guid studentId, int setId, int variantId)
    {
        await using var db = _db.NewContext();
        return await Service(db).ClaimAsync(studentId, setId, variantId);
    }

    [Fact]
    public async Task UniqueInSubject_SecondStudentCannotTakeTheSameVariant_AnyGroup()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.UniqueInSubject);
        var first = set.Variants[0].Id;

        var mine = await ClaimAsync(data.Alpha, set.Id, first);
        mine.Mine!.Number.Should().Be(1);
        mine.SelfClaimed.Should().BeTrue();

        var sameGroup = () => ClaimAsync(data.Beta, set.Id, first);
        (await sameGroup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.taken");
        var otherGroup = () => ClaimAsync(data.OtherGroup, set.Id, first);
        (await otherGroup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.taken");

        await using var db = _db.NewContext();
        var view = (await Service(db).GetMyAsync(data.Beta, data.SubjectId)).Single();
        view.Variants.Single(v => v.Id == first).Taken.Should().BeTrue();
        view.Variants.Where(v => v.Id != first).Should().OnlyContain(v => !v.Taken);
    }

    [Fact]
    public async Task UniqueInGroup_OtherGroupCanTakeTheSameVariant()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.UniqueInGroup);
        var first = set.Variants[0].Id;
        await ClaimAsync(data.Alpha, set.Id, first);

        var sameGroup = () => ClaimAsync(data.Beta, set.Id, first);
        (await sameGroup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.taken");
        (await ClaimAsync(data.OtherGroup, set.Id, first)).Mine!.Id.Should().Be(first);
    }

    [Fact]
    public async Task AllowRepeats_EveryoneCanTakeTheSameVariant_ButOnlyOncePerSet()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.AllowRepeats);
        var first = set.Variants[0].Id;
        await ClaimAsync(data.Alpha, set.Id, first);
        await ClaimAsync(data.Beta, set.Id, first);

        var again = () => ClaimAsync(data.Alpha, set.Id, set.Variants[1].Id);
        (await again.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.already_chosen");
        await using var db = _db.NewContext();
        (await Service(db).GetSetsAsync(data.AdminId, data.SubjectId)).Single().Variants[0].AssignedCount.Should().Be(2);
    }

    [Fact]
    public async Task ClosedManually_OrAfterDeadline_StudentsCannotClaim()
    {
        var data = await SeedAsync();
        var closed = await CreateSetAsync(data, VariantUniqueness.AllowRepeats, open: false);
        var expired = await CreateSetAsync(data, VariantUniqueness.AllowRepeats, deadline: Now.AddMinutes(-1));
        var future = await CreateSetAsync(data, VariantUniqueness.AllowRepeats, deadline: Now.AddMinutes(1));

        foreach (var set in new[] { closed, expired })
        {
            set.IsOpen.Should().BeFalse();
            var claim = () => ClaimAsync(data.Alpha, set.Id, set.Variants[0].Id);
            (await claim.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.claims_closed");
        }
        future.IsOpen.Should().BeTrue();
        (await ClaimAsync(data.Alpha, future.Id, future.Variants[0].Id)).Mine.Should().NotBeNull();
    }

    [Fact]
    public async Task StudentOutsideLinkedGroups_CannotSeeOrClaim()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.AllowRepeats);
        var claim = () => ClaimAsync(data.NotEnrolled, set.Id, set.Variants[0].Id);
        (await claim.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("student.not_enrolled");
        await using var db = _db.NewContext();
        var view = () => Service(db).GetMyAsync(data.NotEnrolled, data.SubjectId);
        (await view.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("student.not_enrolled");
    }

    [Fact]
    public async Task Teacher_AssignsIgnoringUniquenessAndDeadline_AndStudentIsNotified()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.UniqueInSubject, open: false);
        var first = set.Variants[0].Id;
        await using (var db = _db.NewContext())
        {
            await Service(db).AssignAsync(data.AdminId, set.Id, data.Alpha, first);
            await Service(db).AssignAsync(data.AdminId, set.Id, data.Beta, first);
            // Повторная выдача того же варианта ничего не меняет и не шлёт уведомление.
            await Service(db).AssignAsync(data.AdminId, set.Id, data.Beta, first);
        }

        await _notifications.Received(2).NotifyAsync(Arg.Any<IEnumerable<Guid>>(),
            Arg.Is<NotificationRequest>(r => r.Type == NotificationType.VariantAssigned && r.Subject == "ЛР: вариант 1" && r.Link == "variants"),
            Arg.Any<CancellationToken>());

        await using (var db = _db.NewContext())
        {
            var board = await Service(db).GetBoardAsync(data.AdminId, data.SubjectId);
            board.Students.Single(s => s.StudentId == data.Alpha).Variants[set.Id].Should().Be(new AssignedVariantDto(first, 1, null, false));
            board.Students.Should().NotContain(s => s.StudentId == data.NotEnrolled);
            await Service(db).AssignAsync(data.AdminId, set.Id, data.Beta, set.Variants[1].Id);
            await Service(db).UnassignAsync(data.AdminId, set.Id, data.Alpha);
            (await db.VariantAssignments.SingleAsync()).Should().BeEquivalentTo(new { StudentId = data.Beta, VariantId = set.Variants[1].Id });
        }
        await _notifications.Received(3).NotifyAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<NotificationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Teacher_CannotAssignToStudentOutsideTheSubject()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.AllowRepeats);
        await using var db = _db.NewContext();
        var assign = () => Service(db).AssignAsync(data.AdminId, set.Id, data.NotEnrolled, set.Variants[0].Id);
        (await assign.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("student.not_enrolled");
    }

    [Fact]
    public async Task AssignedVariant_CannotBeDeleted_FreeOneCan_AndSetDeletionRemovesAssignments()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.AllowRepeats);
        await ClaimAsync(data.Alpha, set.Id, set.Variants[0].Id);
        await using var db = _db.NewContext();
        var service = Service(db);

        var delete = () => service.DeleteVariantAsync(data.AdminId, set.Variants[0].Id);
        (await delete.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.in_use");
        await service.DeleteVariantAsync(data.AdminId, set.Variants[1].Id);
        (await db.Variants.CountAsync()).Should().Be(2);

        await service.DeleteSetAsync(data.AdminId, set.Id);
        (await db.VariantSets.AnyAsync()).Should().BeFalse();
        (await db.Variants.AnyAsync()).Should().BeFalse();
        (await db.VariantAssignments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SubjectDeletion_CascadesThroughSetsVariantsAndAssignments()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.AllowRepeats);
        await ClaimAsync(data.Alpha, set.Id, set.Variants[0].Id);
        await using var db = _db.NewContext();
        await db.Subjects.Where(s => s.Id == data.SubjectId).ExecuteDeleteAsync();
        (await db.VariantAssignments.AnyAsync()).Should().BeFalse();
        (await db.Variants.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Range_SkipsExistingNumbers_AndSingleAddRejectsDuplicates()
    {
        var data = await SeedAsync();
        var set = await CreateSetAsync(data, VariantUniqueness.AllowRepeats);
        await using var db = _db.NewContext();
        var service = Service(db);

        (await service.AddVariantRangeAsync(data.AdminId, set.Id, new AddVariantRangeRequest(2, 5))).Should().Be(2);
        var duplicate = () => service.AddVariantAsync(data.AdminId, set.Id, new AddVariantRequest(4, null, null));
        (await duplicate.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("variant.number_taken");
        var added = await service.AddVariantAsync(data.AdminId, set.Id, new AddVariantRequest(10, " Графы ", "  "));
        added.Title.Should().Be("Графы");
        added.Description.Should().BeNull();
        (await service.GetSetsAsync(data.AdminId, data.SubjectId)).Single().Variants.Select(v => v.Number)
            .Should().Equal(1, 2, 3, 4, 5, 10);
        var tooMany = () => service.AddVariantRangeAsync(data.AdminId, set.Id, new AddVariantRangeRequest(1, 501));
        await tooMany.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task RestrictedSubject_IsForbidden_AndRestrictedGroupIsLeftOutOfTheBoard()
    {
        var data = await SeedAsync();
        await using (var db = _db.NewContext())
        {
            db.AdminGroupAccesses.Add(new AdminGroupAccess { AdminId = data.AdminId, GroupId = data.GroupId, IsRestricted = true });
            await db.SaveChangesAsync();
            var board = await Service(db).GetBoardAsync(data.AdminId, data.SubjectId);
            board.Students.Select(s => s.StudentId).Should().Equal(data.OtherGroup);
            var assign = () => Service(db).AssignAsync(data.AdminId, 0, data.Alpha, 0);
            await assign.Should().ThrowAsync<NotFoundException>();
        }
        await using (var db = _db.NewContext())
        {
            var access = await db.AdminSubjectAccesses.SingleAsync();
            access.IsRestricted = true;
            await db.SaveChangesAsync();
            var read = () => Service(db).GetSetsAsync(data.AdminId, data.SubjectId);
            await read.Should().ThrowAsync<ForbiddenException>();
        }
    }

    public void Dispose() => _db.Dispose();

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}

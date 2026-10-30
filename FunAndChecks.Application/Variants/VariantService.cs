using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Variants;

public class VariantService(IApplicationDbContext db, IAdminAccessService access,
    IValidator<SaveVariantSetRequest> setValidator,
    IValidator<AddVariantRequest> addValidator,
    IValidator<AddVariantRangeRequest> rangeValidator,
    IValidator<UpdateVariantRequest> updateValidator,
    INotificationService? notifications = null,
    TimeProvider? clock = null) : IVariantService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public async Task<List<VariantSetDto>> GetSetsAsync(Guid adminId, int subjectId, CancellationToken ct = default)
    {
        await EnsureSubjectAsync(adminId, subjectId, ct);
        return await LoadSetsAsync(subjectId, ct);
    }

    public async Task<VariantSetDto> CreateSetAsync(Guid adminId, int subjectId, SaveVariantSetRequest request, CancellationToken ct = default)
    {
        await setValidator.ValidateAndThrowAsync(request, ct);
        await EnsureSubjectAsync(adminId, subjectId, ct);
        var set = new VariantSet
        {
            SubjectId = subjectId, Name = request.Name.Trim(), Uniqueness = request.Uniqueness,
            ClaimsOpen = request.ClaimsOpen, ClaimDeadline = request.ClaimDeadline, CreatedAt = UtcNow,
        };
        if (request is { RangeFrom: { } from, RangeTo: { } to })
            for (var number = from; number <= to; number++)
                set.Variants.Add(new Variant { Number = number });
        db.VariantSets.Add(set);
        await db.SaveChangesAsync(ct);
        return await LoadSetAsync(set.Id, ct);
    }

    public async Task<VariantSetDto> UpdateSetAsync(Guid adminId, int setId, SaveVariantSetRequest request, CancellationToken ct = default)
    {
        await setValidator.ValidateAndThrowAsync(request, ct);
        var set = await GetSetForAdminAsync(adminId, setId, ct);
        // Новое правило действует на следующие выборы: уже выданные повторы остаются.
        set.Name = request.Name.Trim();
        set.Uniqueness = request.Uniqueness;
        set.ClaimsOpen = request.ClaimsOpen;
        set.ClaimDeadline = request.ClaimDeadline;
        await db.SaveChangesAsync(ct);
        return await LoadSetAsync(set.Id, ct);
    }

    public async Task DeleteSetAsync(Guid adminId, int setId, CancellationToken ct = default)
    {
        var set = await GetSetForAdminAsync(adminId, setId, ct);
        // Выдачи ссылаются на варианты с Restrict: убираем их явно, а не полагаемся на порядок каскадов.
        db.VariantAssignments.RemoveRange(await db.VariantAssignments.Where(a => a.VariantSetId == setId).ToListAsync(ct));
        db.VariantSets.Remove(set);
        await db.SaveChangesAsync(ct);
    }

    public async Task<VariantDto> AddVariantAsync(Guid adminId, int setId, AddVariantRequest request, CancellationToken ct = default)
    {
        await addValidator.ValidateAndThrowAsync(request, ct);
        await GetSetForAdminAsync(adminId, setId, ct);
        if (await db.Variants.AnyAsync(v => v.VariantSetId == setId && v.Number == request.Number, ct))
            throw NumberTaken(request.Number);
        var variant = new Variant { VariantSetId = setId, Number = request.Number, Title = Clean(request.Title), Description = Clean(request.Description) };
        db.Variants.Add(variant);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex)) { throw NumberTaken(request.Number); }
        return new(variant.Id, variant.Number, variant.Title, variant.Description, 0);
    }

    public async Task<int> AddVariantRangeAsync(Guid adminId, int setId, AddVariantRangeRequest request, CancellationToken ct = default)
    {
        await rangeValidator.ValidateAndThrowAsync(request, ct);
        await GetSetForAdminAsync(adminId, setId, ct);
        var added = 0;
        await db.ExecuteSerializableAsync(async token =>
        {
            var existing = (await db.Variants.Where(v => v.VariantSetId == setId).Select(v => v.Number).ToListAsync(token)).ToHashSet();
            var fresh = Enumerable.Range(request.From, request.To - request.From + 1).Where(n => !existing.Contains(n)).ToList();
            db.Variants.AddRange(fresh.Select(n => new Variant { VariantSetId = setId, Number = n }));
            await db.SaveChangesAsync(token);
            added = fresh.Count;
        }, ct);
        return added;
    }

    public async Task<VariantDto> UpdateVariantAsync(Guid adminId, int variantId, UpdateVariantRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var variant = await GetVariantForAdminAsync(adminId, variantId, ct);
        variant.Title = Clean(request.Title);
        variant.Description = Clean(request.Description);
        await db.SaveChangesAsync(ct);
        var count = await db.VariantAssignments.CountAsync(a => a.VariantId == variantId, ct);
        return new(variant.Id, variant.Number, variant.Title, variant.Description, count);
    }

    public async Task DeleteVariantAsync(Guid adminId, int variantId, CancellationToken ct = default)
    {
        var variant = await GetVariantForAdminAsync(adminId, variantId, ct);
        if (await db.VariantAssignments.AnyAsync(a => a.VariantId == variantId, ct))
            throw new ConflictException("The variant is assigned to students.", "variant.in_use");
        db.Variants.Remove(variant);
        await db.SaveChangesAsync(ct);
    }

    public async Task<VariantBoardDto> GetBoardAsync(Guid adminId, int subjectId, CancellationToken ct = default)
    {
        await EnsureSubjectAsync(adminId, subjectId, ct);
        var sets = await LoadSetsAsync(subjectId, ct);
        // Как в журнале посещаемости: скрытые и запрещённые группы в выдаче не участвуют.
        var students = await db.Students.AsNoTracking()
            .Where(s => s.IsActive && s.GroupId != null
                && db.GroupSubjects.Any(gs => gs.SubjectId == subjectId && gs.GroupId == s.GroupId)
                && !db.AdminGroupAccesses.Any(a => a.AdminId == adminId && a.GroupId == s.GroupId && (a.IsRestricted || a.IsHidden)))
            .Select(s => new { s.Id, s.LastName, s.FirstName, GroupId = s.GroupId!.Value, GroupName = s.Group!.Name })
            .ToListAsync(ct);
        var assignments = await db.VariantAssignments.AsNoTracking()
            .Where(a => a.VariantSet.SubjectId == subjectId)
            .Select(a => new { a.StudentId, a.VariantSetId, a.VariantId, a.Variant.Number, a.Variant.Title, a.SelfClaimed })
            .ToListAsync(ct);
        var byStudent = assignments.ToLookup(a => a.StudentId);
        var rows = students
            .OrderBy(s => s.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new VariantBoardRowDto(s.Id, $"{s.LastName} {s.FirstName}", s.GroupId, s.GroupName,
                byStudent[s.Id].ToDictionary(a => a.VariantSetId, a => new AssignedVariantDto(a.VariantId, a.Number, a.Title, a.SelfClaimed))))
            .ToList();
        return new(subjectId, sets, rows);
    }

    public async Task<List<StudentVariantSetDto>> GetForStudentAsync(Guid adminId, int subjectId, Guid studentId, CancellationToken ct = default)
    {
        await EnsureSubjectAsync(adminId, subjectId, ct);
        var groupId = await db.Students.Where(s => s.Id == studentId).Select(s => s.GroupId).SingleOrDefaultAsync(ct);
        return await BuildStudentViewAsync(subjectId, studentId, groupId, ct);
    }

    public async Task AssignAsync(Guid adminId, int setId, Guid studentId, int variantId, CancellationToken ct = default)
    {
        var set = await GetSetForAdminAsync(adminId, setId, ct);
        await access.EnsureStudentAllowedAsync(adminId, studentId, ct);
        var student = await db.Students.FindAsync([studentId], ct) ?? throw new NotFoundException("Student not found.");
        await EnsureEnrolledAsync(student, set.SubjectId, ct);
        var variant = await db.Variants.SingleOrDefaultAsync(v => v.Id == variantId && v.VariantSetId == setId, ct)
            ?? throw VariantNotFound();

        // Преподаватель не ограничен ни правилом повторов, ни сроком выбора.
        var changed = false;
        await db.ExecuteSerializableAsync(async token =>
        {
            var assignment = await db.VariantAssignments.SingleOrDefaultAsync(a => a.VariantSetId == setId && a.StudentId == studentId, token);
            changed = assignment?.VariantId != variantId;
            if (!changed) return;
            if (assignment is null)
                db.VariantAssignments.Add(assignment = new VariantAssignment { VariantSetId = setId, StudentId = studentId });
            assignment.VariantId = variantId;
            assignment.AssignedAt = UtcNow;
            assignment.AssignedByAdminId = adminId;
            assignment.SelfClaimed = false;
            await db.SaveChangesAsync(token);
        }, ct);

        if (changed && notifications is not null)
        {
            var actor = await db.Admins.Where(a => a.Id == adminId).Select(a => a.LastName + " " + a.FirstName).SingleOrDefaultAsync(ct);
            await notifications.NotifyAsync([studentId], new(NotificationType.VariantAssigned,
                $"{set.Name}: вариант {variant.Number}", actor, "variants"), ct);
        }
    }

    public async Task UnassignAsync(Guid adminId, int setId, Guid studentId, CancellationToken ct = default)
    {
        await GetSetForAdminAsync(adminId, setId, ct);
        await access.EnsureStudentAllowedAsync(adminId, studentId, ct);
        var assignment = await db.VariantAssignments.SingleOrDefaultAsync(a => a.VariantSetId == setId && a.StudentId == studentId, ct);
        if (assignment is null) return;
        db.VariantAssignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StudentVariantSetDto>> GetMyAsync(Guid studentId, int subjectId, CancellationToken ct = default)
    {
        var student = await db.Students.FindAsync([studentId], ct) ?? throw new NotFoundException("Student not found.");
        await EnsureEnrolledAsync(student, subjectId, ct);
        return await BuildStudentViewAsync(subjectId, studentId, student.GroupId, ct);
    }

    public async Task<StudentVariantSetDto> ClaimAsync(Guid studentId, int setId, int variantId, CancellationToken ct = default)
    {
        var student = await db.Students.FindAsync([studentId], ct) ?? throw new NotFoundException("Student not found.");
        var subjectId = await db.VariantSets.Where(s => s.Id == setId).Select(s => (int?)s.SubjectId).SingleOrDefaultAsync(ct)
            ?? throw SetNotFound();
        await EnsureEnrolledAsync(student, subjectId, ct);

        try
        {
            // Кто первый — того и вариант: проверка занятости и запись в одной сериализуемой транзакции,
            // проигравший в гонке после повтора видит вариант занятым.
            await db.ExecuteSerializableAsync(async token =>
            {
                var set = await db.VariantSets.SingleAsync(s => s.Id == setId, token);
                if (!set.IsClaimOpen(UtcNow))
                    throw new ConflictException("Choosing variants is closed.", "variant.claims_closed");
                if (await db.VariantAssignments.AnyAsync(a => a.VariantSetId == setId && a.StudentId == studentId, token))
                    throw AlreadyChosen();
                if (!await db.Variants.AnyAsync(v => v.Id == variantId && v.VariantSetId == setId, token))
                    throw VariantNotFound();
                if (await TakenByOthersQuery(set, studentId, student.GroupId).AnyAsync(a => a.VariantId == variantId, token))
                    throw new ConflictException("The variant is already taken.", "variant.taken");
                db.VariantAssignments.Add(new VariantAssignment
                {
                    VariantSetId = setId, StudentId = studentId, VariantId = variantId,
                    AssignedAt = UtcNow, SelfClaimed = true,
                });
                await db.SaveChangesAsync(token);
            }, ct);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex)) { throw AlreadyChosen(); }

        return (await BuildStudentViewAsync(subjectId, studentId, student.GroupId, ct)).Single(s => s.Id == setId);
    }

    /// <summary>Выдачи, из-за которых вариант уже не взять этому студенту по правилу набора.</summary>
    private IQueryable<VariantAssignment> TakenByOthersQuery(VariantSet set, Guid studentId, int? groupId) => set.Uniqueness switch
    {
        VariantUniqueness.UniqueInSubject => db.VariantAssignments.Where(a => a.VariantSetId == set.Id && a.StudentId != studentId),
        VariantUniqueness.UniqueInGroup => db.VariantAssignments.Where(a => a.VariantSetId == set.Id && a.StudentId != studentId
            && a.Student.GroupId != null && a.Student.GroupId == groupId),
        _ => db.VariantAssignments.Where(_ => false),
    };

    private async Task<List<StudentVariantSetDto>> BuildStudentViewAsync(int subjectId, Guid studentId, int? groupId, CancellationToken ct)
    {
        var sets = await db.VariantSets.AsNoTracking().Include(s => s.Variants)
            .Where(s => s.SubjectId == subjectId).OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync(ct);
        var now = UtcNow;
        var result = new List<StudentVariantSetDto>(sets.Count);
        foreach (var set in sets)
        {
            var mine = await db.VariantAssignments.AsNoTracking()
                .SingleOrDefaultAsync(a => a.VariantSetId == set.Id && a.StudentId == studentId, ct);
            var taken = (await TakenByOthersQuery(set, studentId, groupId).Select(a => a.VariantId).Distinct().ToListAsync(ct)).ToHashSet();
            var variants = set.Variants.OrderBy(v => v.Number)
                .Select(v => new StudentVariantDto(v.Id, v.Number, v.Title, v.Description, taken.Contains(v.Id))).ToList();
            result.Add(new(set.Id, set.Name, set.Uniqueness, set.IsClaimOpen(now), set.ClaimDeadline,
                mine is null ? null : variants.Single(v => v.Id == mine.VariantId), mine?.SelfClaimed ?? false, variants));
        }
        return result;
    }

    private async Task<List<VariantSetDto>> LoadSetsAsync(int subjectId, CancellationToken ct)
    {
        var ids = await db.VariantSets.Where(s => s.SubjectId == subjectId).OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => s.Id).ToListAsync(ct);
        var result = new List<VariantSetDto>(ids.Count);
        foreach (var id in ids) result.Add(await LoadSetAsync(id, ct));
        return result;
    }

    private async Task<VariantSetDto> LoadSetAsync(int setId, CancellationToken ct)
    {
        var set = await db.VariantSets.AsNoTracking().Include(s => s.Variants).SingleAsync(s => s.Id == setId, ct);
        var counts = await db.VariantAssignments.Where(a => a.VariantSetId == setId).GroupBy(a => a.VariantId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new(set.Id, set.SubjectId, set.Name, set.Uniqueness, set.ClaimsOpen, set.ClaimDeadline, set.IsClaimOpen(UtcNow),
            set.Variants.OrderBy(v => v.Number)
                .Select(v => new VariantDto(v.Id, v.Number, v.Title, v.Description, counts.GetValueOrDefault(v.Id))).ToList());
    }

    private async Task EnsureSubjectAsync(Guid adminId, int subjectId, CancellationToken ct)
    {
        await access.EnsureSubjectAllowedAsync(adminId, subjectId, ct);
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw new NotFoundException("Subject not found.", "subject.not_found");
    }

    private async Task<VariantSet> GetSetForAdminAsync(Guid adminId, int setId, CancellationToken ct)
    {
        var set = await db.VariantSets.FindAsync([setId], ct) ?? throw SetNotFound();
        await access.EnsureSubjectAllowedAsync(adminId, set.SubjectId, ct);
        return set;
    }

    private async Task<Variant> GetVariantForAdminAsync(Guid adminId, int variantId, CancellationToken ct)
    {
        var variant = await db.Variants.Include(v => v.VariantSet).SingleOrDefaultAsync(v => v.Id == variantId, ct)
            ?? throw VariantNotFound();
        await access.EnsureSubjectAllowedAsync(adminId, variant.VariantSet.SubjectId, ct);
        return variant;
    }

    private async Task EnsureEnrolledAsync(Student student, int subjectId, CancellationToken ct)
    {
        if (student.GroupId is not { } groupId
            || !await db.GroupSubjects.AnyAsync(gs => gs.GroupId == groupId && gs.SubjectId == subjectId, ct))
            throw new ConflictException("Student is not enrolled in this subject.", "student.not_enrolled",
                new Dictionary<string, object?> { ["studentId"] = student.Id, ["subjectId"] = subjectId });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static NotFoundException SetNotFound() => new("Variant set not found.", "variant_set.not_found");
    private static NotFoundException VariantNotFound() => new("Variant not found.", "variant.not_found");
    private static ConflictException AlreadyChosen() => new("You already have a variant in this set.", "variant.already_chosen");
    private static ConflictException NumberTaken(int number) =>
        new("A variant with this number already exists.", "variant.number_taken", new Dictionary<string, object?> { ["number"] = number });
}

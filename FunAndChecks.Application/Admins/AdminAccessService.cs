using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Admins;

public class AdminAccessService(IApplicationDbContext db) : IAdminAccessService
{
    public async Task<AdminAccessDto> GetAccessAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        var subjects = await db.AdminSubjectAccesses
            .Where(a => a.AdminId == adminId)
            .ToListAsync(cancellationToken);
        var groups = await db.AdminGroupAccesses
            .Where(a => a.AdminId == adminId)
            .ToListAsync(cancellationToken);

        return new AdminAccessDto(
            subjects.Where(a => a.IsRestricted).Select(a => a.SubjectId).ToList(),
            groups.Where(a => a.IsRestricted).Select(a => a.GroupId).ToList(),
            subjects.Where(a => a.IsHidden).Select(a => a.SubjectId).ToList(),
            groups.Where(a => a.IsHidden).Select(a => a.GroupId).ToList());
    }

    public async Task EnsureSubjectAllowedAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default)
    {
        var access = await db.AdminSubjectAccesses
            .Where(a => a.AdminId == adminId && a.SubjectId == subjectId)
            .Select(a => new { a.IsRestricted, a.IsHidden })
            .FirstOrDefaultAsync(cancellationToken);
        if (access?.IsRestricted == true)
            throw new ForbiddenException("You are restricted from working with this subject.", "access.subject_restricted",
                new Dictionary<string, object?> { ["subjectId"] = subjectId });
        if (access?.IsHidden == true)
            throw new ForbiddenException("This subject is hidden from your workspace.", "access.subject_hidden",
                new Dictionary<string, object?> { ["subjectId"] = subjectId });
    }

    public async Task EnsureGroupAllowedAsync(Guid adminId, int groupId, CancellationToken cancellationToken = default)
    {
        var access = await db.AdminGroupAccesses
            .Where(a => a.AdminId == adminId && a.GroupId == groupId)
            .Select(a => new { a.IsRestricted, a.IsHidden })
            .FirstOrDefaultAsync(cancellationToken);
        if (access?.IsRestricted == true)
            throw new ForbiddenException("You are restricted from working with this group.", "access.group_restricted",
                new Dictionary<string, object?> { ["groupId"] = groupId });
        if (access?.IsHidden == true)
            throw new ForbiddenException("This group is hidden from your workspace.", "access.group_hidden",
                new Dictionary<string, object?> { ["groupId"] = groupId });
    }

    public async Task EnsureGroupsAllowedAsync(Guid adminId, IEnumerable<int> groupIds, CancellationToken cancellationToken = default)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0)
            return;

        var accesses = await db.AdminGroupAccesses
            .Where(a => a.AdminId == adminId && ids.Contains(a.GroupId))
            .Select(a => new { a.GroupId, a.IsRestricted, a.IsHidden })
            .ToDictionaryAsync(a => a.GroupId, cancellationToken);
        if (accesses.Count == 0)
            return;

        foreach (var groupId in ids)
        {
            if (!accesses.TryGetValue(groupId, out var access))
                continue;
            if (access.IsRestricted)
                throw new ForbiddenException("You are restricted from working with this group.", "access.group_restricted",
                    new Dictionary<string, object?> { ["groupId"] = groupId });
            if (access.IsHidden)
                throw new ForbiddenException("This group is hidden from your workspace.", "access.group_hidden",
                    new Dictionary<string, object?> { ["groupId"] = groupId });
        }
    }

    public Task<bool> IsSubjectRestrictedAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default) =>
        db.AdminSubjectAccesses
            .AnyAsync(a => a.AdminId == adminId && a.SubjectId == subjectId && a.IsRestricted, cancellationToken);

    public Task<bool> IsGroupRestrictedAsync(Guid adminId, int groupId, CancellationToken cancellationToken = default) =>
        db.AdminGroupAccesses
            .AnyAsync(a => a.AdminId == adminId && a.GroupId == groupId && a.IsRestricted, cancellationToken);

    public Task SetSubjectRestrictedAsync(Guid adminId, int subjectId, bool restricted, CancellationToken cancellationToken = default) =>
        UpsertSubjectAsync(adminId, subjectId, a => a.IsRestricted = restricted, cancellationToken);

    public Task SetSubjectHiddenAsync(Guid adminId, int subjectId, bool hidden, CancellationToken cancellationToken = default) =>
        UpsertSubjectAsync(adminId, subjectId, a => a.IsHidden = hidden, cancellationToken);

    public Task SetGroupRestrictedAsync(Guid adminId, int groupId, bool restricted, CancellationToken cancellationToken = default) =>
        UpsertGroupAsync(adminId, groupId, a => a.IsRestricted = restricted, cancellationToken);

    public Task SetGroupHiddenAsync(Guid adminId, int groupId, bool hidden, CancellationToken cancellationToken = default) =>
        UpsertGroupAsync(adminId, groupId, a => a.IsHidden = hidden, cancellationToken);

    private async Task UpsertSubjectAsync(Guid adminId, int subjectId, Action<AdminSubjectAccess> mutate, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminId, cancellationToken);
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var access = await db.AdminSubjectAccesses
            .FirstOrDefaultAsync(a => a.AdminId == adminId && a.SubjectId == subjectId, cancellationToken);

        if (access == null)
        {
            access = new AdminSubjectAccess { AdminId = adminId, SubjectId = subjectId };
            db.AdminSubjectAccesses.Add(access);
        }

        mutate(access);

        // Пустую запись (оба флага сняты) не храним.
        if (!access.IsRestricted && !access.IsHidden)
            db.AdminSubjectAccesses.Remove(access);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertGroupAsync(Guid adminId, int groupId, Action<AdminGroupAccess> mutate, CancellationToken cancellationToken)
    {
        await EnsureAdminAsync(adminId, cancellationToken);
        if (!await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken))
            throw new NotFoundException($"Group with ID {groupId} not found.");

        var access = await db.AdminGroupAccesses
            .FirstOrDefaultAsync(a => a.AdminId == adminId && a.GroupId == groupId, cancellationToken);

        if (access == null)
        {
            access = new AdminGroupAccess { AdminId = adminId, GroupId = groupId };
            db.AdminGroupAccesses.Add(access);
        }

        mutate(access);

        if (!access.IsRestricted && !access.IsHidden)
            db.AdminGroupAccesses.Remove(access);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAdminAsync(Guid adminId, CancellationToken cancellationToken)
    {
        if (!await db.Admins.AnyAsync(a => a.Id == adminId, cancellationToken))
            throw new NotFoundException($"Admin with ID {adminId} not found.");
    }
}

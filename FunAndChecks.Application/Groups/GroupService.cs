using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Groups;

public class GroupService(
    IApplicationDbContext db,
    IIdentityService identityService,
    IResultsCacheService cache,
    IValidator<CreateGroupRequest> createGroupValidator,
    IValidator<UpdateGroupRequest> updateGroupValidator)
    : IGroupService
{
    public Task<List<GroupDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        db.Groups
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(g.Id, g.Name))
            .ToListAsync(cancellationToken);

    public async Task<GroupDto> GetAsync(int groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups
            .Where(g => g.Id == groupId)
            .Select(g => new GroupDto(g.Id, g.Name))
            .FirstOrDefaultAsync(cancellationToken);

        return group ?? throw new NotFoundException($"Group with ID {groupId} not found.");
    }

    public async Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken cancellationToken = default)
    {
        await createGroupValidator.ValidateAndThrowAsync(request, cancellationToken);

        var group = new Group { Name = request.Name };
        db.Groups.Add(group);
        await db.SaveChangesAsync(cancellationToken);

        return new GroupDto(group.Id, group.Name);
    }

    public async Task<GroupDto> UpdateAsync(int groupId, UpdateGroupRequest request, CancellationToken cancellationToken = default)
    {
        await updateGroupValidator.ValidateAndThrowAsync(request, cancellationToken);

        var group = await db.Groups.FindAsync([groupId], cancellationToken)
                    ?? throw new NotFoundException($"Group with ID {groupId} not found.");

        group.Name = request.Name;
        await db.SaveChangesAsync(cancellationToken);

        // Имя группы отображается в таблицах результатов — сбрасываем кэш затронутых предметов.
        await InvalidateGroupSubjectsCacheAsync(groupId, cancellationToken);

        return new GroupDto(group.Id, group.Name);
    }

    public async Task DeleteAsync(int groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.FindAsync([groupId], cancellationToken)
                    ?? throw new NotFoundException($"Group with ID {groupId} not found.");

        await InvalidateGroupSubjectsCacheAsync(groupId, cancellationToken);

        db.Groups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task LinkSubjectAsync(int groupId, int subjectId, CancellationToken cancellationToken = default)
    {
        var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken);
        if (!groupExists)
            throw new NotFoundException($"Group with ID {groupId} not found.");

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {subjectId} not found.");

        var linkExists = await db.GroupSubjects
            .AnyAsync(gs => gs.GroupId == groupId && gs.SubjectId == subjectId, cancellationToken);
        if (linkExists)
            return; // идемпотентность

        db.GroupSubjects.Add(new GroupSubject { GroupId = groupId, SubjectId = subjectId });
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId); // изменился состав студентов предмета
    }

    public async Task UnlinkSubjectAsync(int groupId, int subjectId, CancellationToken cancellationToken = default)
    {
        var link = await db.GroupSubjects
            .FirstOrDefaultAsync(gs => gs.GroupId == groupId && gs.SubjectId == subjectId, cancellationToken);

        if (link == null)
            return; // идемпотентность

        db.GroupSubjects.Remove(link);
        await db.SaveChangesAsync(cancellationToken);

        cache.Invalidate(subjectId); // изменился состав студентов предмета
    }

    public Task<List<StudentDto>> GetStudentsAsync(int groupId, CancellationToken cancellationToken = default) =>
        db.Students
            .Where(s => s.GroupId == groupId)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentDto(s.Id, s.FirstName, s.LastName, s.Color))
            .ToListAsync(cancellationToken);

    public async Task<List<StudentDetailsDto>> GetStudentsDetailedAsync(int groupId, CancellationToken cancellationToken = default)
    {
        var students = await db.Students
            .Where(s => s.GroupId == groupId)
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentDetailsDto(s.Id, s.FirstName, s.LastName, null, s.GitHubUrl, s.Color, s.GroupId))
            .ToListAsync(cancellationToken);

        var emails = await identityService.GetEmailsAsync(students.Select(s => s.Id));
        return students
            .Select(s => s with { Email = emails.GetValueOrDefault(s.Id) })
            .ToList();
    }

    private async Task InvalidateGroupSubjectsCacheAsync(int groupId, CancellationToken cancellationToken)
    {
        var subjectIds = await db.GroupSubjects
            .Where(gs => gs.GroupId == groupId)
            .Select(gs => gs.SubjectId)
            .ToListAsync(cancellationToken);

        foreach (var subjectId in subjectIds)
            cache.Invalidate(subjectId);
    }
}

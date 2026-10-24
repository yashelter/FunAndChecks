using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Forum;

/// <summary>
/// Кто читает форум и что ему видно. Студенту — «Общее» и предметы его группы;
/// преподавателю — разрешённые супер-админом предметы; супер-админу — всё.
/// </summary>
public record ForumViewer(Guid UserId, string FullName, bool IsAdmin, bool IsSuperAdmin, IReadOnlySet<int> SubjectIds)
{
    public bool CanSee(int? subjectId) => subjectId is null || SubjectIds.Contains(subjectId.Value);

    public bool CanModerate(int? subjectId) => IsAdmin && CanSee(subjectId);
}

public interface IForumAccess
{
    Task<ForumViewer> GetViewerAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Тема, которую зритель видит; иначе 404 — существование чужих тем не раскрываем.</summary>
    Task<ForumTopic> RequireTopicAsync(ForumViewer viewer, int topicId, CancellationToken ct = default);

    /// <summary>Все учебные аккаунты, которым видна тема с этим предметом.</summary>
    Task<IReadOnlySet<Guid>> AudienceAsync(int? subjectId, CancellationToken ct = default);
}

public class ForumAccess(IApplicationDbContext db, IIdentityService identity) : IForumAccess
{
    public async Task<ForumViewer> GetViewerAsync(Guid userId, CancellationToken ct = default)
    {
        var account = await identity.GetProfileAccountAsync(userId, ct)
            ?? throw new ForbiddenException("The forum is available to confirmed students and teachers.", "forum.no_access");
        var roles = await identity.GetCurrentRolesAsync(userId, ct);
        var isSuper = roles.Contains(Roles.SuperAdmin);
        var isAdmin = isSuper || roles.Contains(Roles.Admin);
        List<int> subjects;
        if (isSuper)
            subjects = await db.Subjects.Select(s => s.Id).ToListAsync(ct);
        else if (isAdmin)
            subjects = await db.AdminSubjectAccesses.Where(a => a.AdminId == userId && !a.IsRestricted)
                .Select(a => a.SubjectId).ToListAsync(ct);
        else
            subjects = await db.Students.Where(s => s.Id == userId && s.GroupId != null)
                .SelectMany(s => db.GroupSubjects.Where(gs => gs.GroupId == s.GroupId).Select(gs => gs.SubjectId))
                .ToListAsync(ct);
        return new(userId, account.FullName, isAdmin, isSuper, subjects.ToHashSet());
    }

    public async Task<ForumTopic> RequireTopicAsync(ForumViewer viewer, int topicId, CancellationToken ct = default)
    {
        var topic = await db.ForumTopics.SingleOrDefaultAsync(t => t.Id == topicId && !t.IsDeleted, ct);
        if (topic is null || !viewer.CanSee(topic.SubjectId))
            throw new NotFoundException("Topic not found.", "forum.topic_not_found");
        return topic;
    }

    public async Task<IReadOnlySet<Guid>> AudienceAsync(int? subjectId, CancellationToken ct = default)
    {
        var supers = await identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, ct);
        var students = db.Students.Where(s => s.IsActive);
        var admins = db.Admins.Select(a => a.Id);
        if (subjectId is int id)
        {
            students = students.Where(s => s.GroupId != null &&
                db.GroupSubjects.Any(gs => gs.GroupId == s.GroupId && gs.SubjectId == id));
            admins = db.AdminSubjectAccesses.Where(a => a.SubjectId == id && !a.IsRestricted).Select(a => a.AdminId);
        }
        var result = (await students.Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        result.UnionWith(await admins.ToListAsync(ct));
        result.UnionWith(supers);
        return result;
    }
}

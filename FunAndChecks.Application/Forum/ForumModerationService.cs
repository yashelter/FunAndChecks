using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Forum;

public interface IForumModerationService
{
    Task ReportAsync(Guid userId, long postId, ReportForumPostRequest request, CancellationToken ct = default);
    Task<ForumReportPageDto> GetReportsAsync(Guid adminId, int page, int pageSize, CancellationToken ct = default);
    Task ResolveReportAsync(Guid adminId, int reportId, ResolveForumReportRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ForumBanDto>> GetBansAsync(Guid adminId, CancellationToken ct = default);
    Task BanAsync(Guid adminId, Guid userId, BanForumUserRequest request, CancellationToken ct = default);
    Task UnbanAsync(Guid adminId, Guid userId, CancellationToken ct = default);
}

public class ForumModerationService(
    IApplicationDbContext db,
    IForumAccess access,
    IIdentityService identity,
    IValidator<ReportForumPostRequest> reportValidator,
    IValidator<BanForumUserRequest> banValidator) : IForumModerationService
{
    public async Task ReportAsync(Guid userId, long postId, ReportForumPostRequest request, CancellationToken ct = default)
    {
        await reportValidator.ValidateAndThrowAsync(request, ct);
        var viewer = await access.GetViewerAsync(userId, ct);
        var post = await db.ForumPosts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new NotFoundException("Message not found.", "forum.post_not_found");
        await access.RequireTopicAsync(viewer, post.TopicId, ct);
        if (post.IsDeleted) throw new ConflictException("The message was already deleted.", "forum.post_deleted");
        if (await db.ForumReports.AnyAsync(r => r.PostId == postId && r.ReporterId == userId && r.ResolvedAtUtc == null, ct))
            throw new ConflictException("You have already reported this message.", "forum.already_reported");
        db.ForumReports.Add(new ForumReport
        {
            PostId = postId, ReporterId = userId, ReporterName = viewer.FullName, Reason = request.Reason!.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ForumReportPageDto> GetReportsAsync(Guid adminId, int page, int pageSize, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        var viewer = await RequireAdminAsync(adminId, ct);
        var subjects = viewer.SubjectIds.ToList();
        var reports = db.ForumReports.AsNoTracking().Where(r => r.ResolvedAtUtc == null && !r.Post.Topic.IsDeleted &&
            (r.Post.Topic.SubjectId == null || subjects.Contains(r.Post.Topic.SubjectId.Value)));
        var total = await reports.CountAsync(ct);
        var entries = await reports.OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.PostId, r.Post.TopicId, TopicTitle = r.Post.Topic.Title, r.Post.Body, r.Post.IsDeleted,
                r.Post.AuthorId, r.Post.AuthorName, r.ReporterName, r.Reason, r.CreatedAtUtc,
            }).ToListAsync(ct);
        var authors = await identity.GetProfileAccountsAsync(entries.Where(e => e.AuthorId.HasValue).Select(e => e.AuthorId!.Value), ct);
        var items = entries.Select(e => new ForumReportDto(e.Id, e.PostId, e.TopicId, e.TopicTitle,
            e.IsDeleted ? "" : ForumService.Excerpt(e.Body), e.IsDeleted,
            e.AuthorId is Guid id && authors.TryGetValue(id, out var a) ? new(id, a.FullName, a.IsAdmin) : new(null, e.AuthorName, false),
            e.ReporterName, e.Reason, e.CreatedAtUtc)).ToList();
        return new(items, total, page, pageSize);
    }

    public async Task ResolveReportAsync(Guid adminId, int reportId, ResolveForumReportRequest request, CancellationToken ct = default)
    {
        var viewer = await RequireAdminAsync(adminId, ct);
        await db.ExecuteSerializableAsync(async token =>
        {
            var report = await db.ForumReports.Include(r => r.Post).ThenInclude(p => p.Topic)
                .SingleOrDefaultAsync(r => r.Id == reportId, token)
                ?? throw new NotFoundException("Report not found.", "forum.report_not_found");
            if (report.Post.Topic.IsDeleted || !viewer.CanModerate(report.Post.Topic.SubjectId))
                throw new NotFoundException("Report not found.", "forum.report_not_found");
            var now = DateTime.UtcNow;
            // Закрываем все открытые жалобы на это сообщение разом.
            var open = await db.ForumReports.Where(r => r.PostId == report.PostId && r.ResolvedAtUtc == null).ToListAsync(token);
            foreach (var item in open)
            {
                item.ResolvedAtUtc = now;
                item.ResolvedById = adminId;
            }
            if (request.DeletePost && !report.Post.IsDeleted)
                await ForumService.SoftDeleteAsync(db, report.Post, report.Post.Topic, adminId, token);
            await db.SaveChangesAsync(token);
        }, ct);
    }

    public async Task<IReadOnlyList<ForumBanDto>> GetBansAsync(Guid adminId, CancellationToken ct = default)
    {
        await RequireAdminAsync(adminId, ct);
        var bans = await db.ForumBans.AsNoTracking().Where(b => b.UntilUtc > DateTime.UtcNow)
            .OrderBy(b => b.UntilUtc).ToListAsync(ct);
        var accounts = await identity.GetProfileAccountsAsync(bans.Select(b => b.UserId), ct);
        return bans.Select(b => accounts.TryGetValue(b.UserId, out var a)
                ? new ForumBanDto(b.UserId, a.FullName, a.GroupName, b.UntilUtc, b.Reason, b.CreatedAtUtc)
                : new ForumBanDto(b.UserId, "—", null, b.UntilUtc, b.Reason, b.CreatedAtUtc))
            .ToList();
    }

    public async Task BanAsync(Guid adminId, Guid userId, BanForumUserRequest request, CancellationToken ct = default)
    {
        await banValidator.ValidateAndThrowAsync(request, ct);
        var viewer = await RequireAdminAsync(adminId, ct);
        if (userId == adminId) throw new ConflictException("You cannot ban yourself.", "forum.ban_self");
        _ = await identity.GetProfileAccountAsync(userId, ct)
            ?? throw new NotFoundException("User not found.", "profile.not_found");
        var roles = await identity.GetCurrentRolesAsync(userId, ct);
        if ((roles.Contains(Roles.Admin) || roles.Contains(Roles.SuperAdmin)) && !viewer.IsSuperAdmin)
            throw new ForbiddenException("Only a super administrator can restrict teachers.", "forum.ban_forbidden");
        await db.ExecuteSerializableAsync(async token =>
        {
            var ban = await db.ForumBans.SingleOrDefaultAsync(b => b.UserId == userId, token);
            if (ban is null)
            {
                ban = new ForumBan { UserId = userId, Reason = "" };
                db.ForumBans.Add(ban);
            }
            ban.UntilUtc = DateTime.SpecifyKind(request.UntilUtc, DateTimeKind.Utc);
            ban.Reason = request.Reason?.Trim() ?? "";
            ban.CreatedById = adminId;
            ban.CreatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }, ct);
    }

    public async Task UnbanAsync(Guid adminId, Guid userId, CancellationToken ct = default)
    {
        await RequireAdminAsync(adminId, ct);
        var ban = await db.ForumBans.SingleOrDefaultAsync(b => b.UserId == userId, ct);
        if (ban is null) return;
        db.ForumBans.Remove(ban);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ForumViewer> RequireAdminAsync(Guid adminId, CancellationToken ct)
    {
        var viewer = await access.GetViewerAsync(adminId, ct);
        if (!viewer.IsAdmin) throw new ForbiddenException("Only teachers can moderate the forum.", "forum.moderation_required");
        return viewer;
    }
}

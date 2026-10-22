using System.Text.RegularExpressions;
using FluentValidation;
using FunAndChecks.Application.Attachments;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Forum;

public interface IForumService
{
    Task<IReadOnlyList<ForumSectionDto>> GetSectionsAsync(Guid userId, CancellationToken ct = default);
    Task<ForumTopicPageDto> GetTopicsAsync(Guid userId, int? subjectId, bool generalOnly, string? search, int page, int pageSize,
        CancellationToken ct = default);
    Task<ForumUnreadDto> GetUnreadAsync(Guid userId, CancellationToken ct = default);
    Task<ForumTopicDto> GetTopicAsync(Guid userId, int topicId, CancellationToken ct = default);
    Task<ForumPostPageDto> GetPostsAsync(Guid userId, int topicId, int page, int pageSize, long? aroundPostId, CancellationToken ct = default);
    Task<ForumTopicDto> CreateTopicAsync(Guid userId, CreateForumTopicRequest request, CancellationToken ct = default);
    Task<ForumTopicDto> UpdateTopicAsync(Guid userId, int topicId, UpdateForumTopicRequest request, CancellationToken ct = default);
    Task DeleteTopicAsync(Guid userId, int topicId, CancellationToken ct = default);
    Task<ForumPostDto> CreatePostAsync(Guid userId, int topicId, CreateForumPostRequest request, CancellationToken ct = default);
    Task<ForumPostDto> UpdatePostAsync(Guid userId, long postId, UpdateForumPostRequest request, CancellationToken ct = default);
    Task DeletePostAsync(Guid userId, long postId, CancellationToken ct = default);
    Task SetPostPinnedAsync(Guid userId, long postId, bool pinned, CancellationToken ct = default);
    Task<IReadOnlyList<ForumReactionSummaryDto>> SetReactionAsync(Guid userId, long postId, ForumReactionKind kind, bool active,
        CancellationToken ct = default);
    Task MarkReadAsync(Guid userId, int topicId, long lastPostId, CancellationToken ct = default);
    Task SetSubscriptionAsync(Guid userId, int topicId, bool subscribed, CancellationToken ct = default);
    Task<IReadOnlyList<ForumMentionCandidateDto>> GetMentionCandidatesAsync(Guid userId, int topicId, string? query,
        CancellationToken ct = default);
}

public partial class ForumService(
    IApplicationDbContext db,
    IForumAccess access,
    IIdentityService identity,
    IAttachmentUrlSigner signer,
    INotificationService notifications,
    IForumNotifier forumNotifier,
    IValidator<CreateForumTopicRequest> createTopicValidator,
    IValidator<UpdateForumTopicRequest> updateTopicValidator,
    IValidator<CreateForumPostRequest> createPostValidator,
    IValidator<UpdateForumPostRequest> updatePostValidator,
    ILogger<ForumService> logger) : IForumService
{
    private const int QuoteLength = 160;

    [GeneratedRegex(@"(?:@\[|\[@)(?<name>[^\]\r\n]{1,100})\]\(user:(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)")]
    private static partial Regex MentionPattern();

    public async Task<IReadOnlyList<ForumSectionDto>> GetSectionsAsync(Guid userId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topics = await Visible(viewer).Select(t => new { t.Id, t.SubjectId }).ToListAsync(ct);
        var unread = (await UnreadCountsAsync(viewer, Visible(viewer).Select(t => t.Id), ct)).Keys.ToHashSet();
        var subjects = await db.Subjects.AsNoTracking().Where(s => viewer.SubjectIds.Contains(s.Id))
            .OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var sections = new List<ForumSectionDto>
        {
            new(null, null, topics.Count(t => t.SubjectId == null), topics.Count(t => t.SubjectId == null && unread.Contains(t.Id)),
                viewer.CanModerate(null)),
        };
        sections.AddRange(subjects.Select(s => new ForumSectionDto(s.Id, s.Name, topics.Count(t => t.SubjectId == s.Id),
            topics.Count(t => t.SubjectId == s.Id && unread.Contains(t.Id)), viewer.CanModerate(s.Id))));
        return sections;
    }

    public async Task<ForumTopicPageDto> GetTopicsAsync(Guid userId, int? subjectId, bool generalOnly, string? search,
        int page, int pageSize, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        var viewer = await access.GetViewerAsync(userId, ct);
        var topics = Visible(viewer);
        if (generalOnly) topics = topics.Where(t => t.SubjectId == null);
        else if (subjectId is int id) topics = topics.Where(t => t.SubjectId == id);
        var term = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(term))
            topics = topics.Where(t => t.Title.ToLower().Contains(term) ||
                t.Posts.Any(p => !p.IsDeleted && p.Body.ToLower().Contains(term)));
        var total = await topics.CountAsync(ct);
        var entries = await topics.Include(t => t.Subject)
            .OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.LastActivityAtUtc).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = await SummariesAsync(viewer, entries, ct);
        return new(items, total, page, pageSize);
    }

    public async Task<ForumUnreadDto> GetUnreadAsync(Guid userId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        return new((await UnreadCountsAsync(viewer, Visible(viewer).Select(t => t.Id), ct)).Count);
    }

    public async Task<ForumTopicDto> GetTopicAsync(Guid userId, int topicId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topic = await access.RequireTopicAsync(viewer, topicId, ct);
        return await TopicDtoAsync(viewer, topic, ct);
    }

    public async Task<ForumPostPageDto> GetPostsAsync(Guid userId, int topicId, int page, int pageSize, long? aroundPostId,
        CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        var viewer = await access.GetViewerAsync(userId, ct);
        var topic = await access.RequireTopicAsync(viewer, topicId, ct);
        var posts = db.ForumPosts.AsNoTracking().Where(p => p.TopicId == topicId);
        var total = await posts.CountAsync(ct);
        // Страница, где лежит сообщение (или первое после него — так открывается первое непрочитанное).
        if (aroundPostId is long around)
            page = Math.Min(await posts.CountAsync(p => p.Id < around, ct) / pageSize + 1, Math.Max(1, (total + pageSize - 1) / pageSize));
        var entries = await posts.OrderBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(await PostDtosAsync(viewer, topic, entries, ct), total, page, pageSize);
    }

    public async Task<ForumTopicDto> CreateTopicAsync(Guid userId, CreateForumTopicRequest request, CancellationToken ct = default)
    {
        await createTopicValidator.ValidateAndThrowAsync(request, ct);
        var viewer = await access.GetViewerAsync(userId, ct);
        if (request.SubjectId is int subjectId && !await db.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            throw new NotFoundException("Subject not found.", "subject.not_found");
        if (!viewer.CanModerate(request.SubjectId))
            throw new ForbiddenException("Only teachers of this subject can start topics.", "forum.moderation_required");
        await EnsureNotBannedAsync(userId, ct);
        ForumTopic topic = null!;
        ForumPost post = null!;
        await db.ExecuteSerializableAsync(async token =>
        {
            // Создаём сущности внутри попытки: при повторе транзакции трекер очищается.
            var now = DateTime.UtcNow;
            topic = new ForumTopic
            {
                SubjectId = request.SubjectId, Title = request.Title!.Trim(), AuthorId = userId, AuthorName = viewer.FullName,
                CreatedAtUtc = now, LastActivityAtUtc = now, PostCount = 1,
                IsAnnouncement = request.IsAnnouncement, IsPinned = request.IsPinned,
            };
            var files = await AttachmentService.ClaimAsync(db, userId, request.AttachmentIds ?? [], null, token);
            post = new ForumPost
            {
                Topic = topic, AuthorId = userId, AuthorName = viewer.FullName, Body = request.Body!.Trim(),
                CreatedAtUtc = topic.CreatedAtUtc,
            };
            db.ForumTopics.Add(topic);
            db.ForumPosts.Add(post);
            await db.SaveChangesAsync(token);
            foreach (var file in files) file.ForumPostId = post.Id;
            db.ForumSubscriptions.Add(new ForumSubscription { TopicId = topic.Id, UserId = userId });
            db.ForumTopicReads.Add(new ForumTopicRead { TopicId = topic.Id, UserId = userId, LastReadPostId = post.Id });
            await db.SaveChangesAsync(token);
        }, ct);

        var audience = await access.AudienceAsync(topic.SubjectId, ct);
        var mentioned = Mentions(post.Body).Where(audience.Contains).Where(id => id != userId).ToHashSet();
        if (topic.IsAnnouncement)
            await notifications.NotifyAsync(audience.Where(id => id != userId && !mentioned.Contains(id)),
                new(NotificationType.ForumAnnouncement, topic.Title, viewer.FullName, Link(topic.Id, post.Id)), ct);
        await notifications.NotifyAsync(mentioned, new(NotificationType.ForumMention, topic.Title, viewer.FullName, Link(topic.Id, post.Id)), ct);
        return await TopicDtoAsync(viewer, topic, ct);
    }

    public async Task<ForumTopicDto> UpdateTopicAsync(Guid userId, int topicId, UpdateForumTopicRequest request, CancellationToken ct = default)
    {
        await updateTopicValidator.ValidateAndThrowAsync(request, ct);
        var viewer = await access.GetViewerAsync(userId, ct);
        ForumTopic topic = null!;
        await db.ExecuteSerializableAsync(async token =>
        {
            topic = await access.RequireTopicAsync(viewer, topicId, token);
            RequireModerator(viewer, topic.SubjectId);
            if (request.ChangeSubject && request.SubjectId != topic.SubjectId)
            {
                if (request.SubjectId is int target && !await db.Subjects.AnyAsync(s => s.Id == target, token))
                    throw new NotFoundException("Subject not found.", "subject.not_found");
                RequireModerator(viewer, request.SubjectId);
                topic.SubjectId = request.SubjectId;
            }
            if (request.Title is not null) topic.Title = request.Title.Trim();
            if (request.IsClosed is bool closed) topic.IsClosed = closed;
            if (request.IsPinned is bool pinned) topic.IsPinned = pinned;
            if (request.IsAnnouncement is bool announcement) topic.IsAnnouncement = announcement;
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.TopicChanged, null), ct);
        return await TopicDtoAsync(viewer, topic, ct);
    }

    public async Task DeleteTopicAsync(Guid userId, int topicId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        await db.ExecuteSerializableAsync(async token =>
        {
            var topic = await access.RequireTopicAsync(viewer, topicId, token);
            RequireModerator(viewer, topic.SubjectId);
            topic.IsDeleted = true;
            topic.DeletedAtUtc = DateTime.UtcNow;
            topic.DeletedById = userId;
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.TopicChanged, null), ct);
    }

    public async Task<ForumPostDto> CreatePostAsync(Guid userId, int topicId, CreateForumPostRequest request, CancellationToken ct = default)
    {
        await createPostValidator.ValidateAndThrowAsync(request, ct);
        var viewer = await access.GetViewerAsync(userId, ct);
        ForumTopic topic = null!;
        ForumPost post = null!;
        Guid? repliedAuthor = null;
        await db.ExecuteSerializableAsync(async token =>
        {
            topic = await access.RequireTopicAsync(viewer, topicId, token);
            await EnsureCanReplyAsync(viewer, topic, token);
            if (request.ReplyToPostId is long replyId)
            {
                var replied = await db.ForumPosts.Where(p => p.Id == replyId && p.TopicId == topicId)
                    .Select(p => new { p.AuthorId }).SingleOrDefaultAsync(token)
                    ?? throw new NotFoundException("The quoted message is not in this topic.", "forum.post_not_found");
                repliedAuthor = replied.AuthorId;
            }
            var files = await AttachmentService.ClaimAsync(db, userId, request.AttachmentIds ?? [], null, token);
            post = new ForumPost
            {
                TopicId = topicId, AuthorId = userId, AuthorName = viewer.FullName, Body = request.Body?.Trim() ?? "",
                ReplyToPostId = request.ReplyToPostId, CreatedAtUtc = DateTime.UtcNow,
            };
            db.ForumPosts.Add(post);
            topic.PostCount++;
            topic.LastActivityAtUtc = post.CreatedAtUtc;
            await db.SaveChangesAsync(token);
            foreach (var file in files) file.ForumPostId = post.Id;
            if (!await db.ForumSubscriptions.AnyAsync(s => s.TopicId == topicId && s.UserId == userId, token))
                db.ForumSubscriptions.Add(new ForumSubscription { TopicId = topicId, UserId = userId });
            await UpsertReadAsync(topicId, userId, post.Id, token);
            await db.SaveChangesAsync(token);
        }, ct);

        await PublishAsync(new(topicId, ForumEventKinds.PostCreated, post.Id), ct);
        await NotifyRepliesAsync(viewer, topic, post, repliedAuthor, ct);
        return (await PostDtosAsync(viewer, topic, [post], ct))[0];
    }

    public async Task<ForumPostDto> UpdatePostAsync(Guid userId, long postId, UpdateForumPostRequest request, CancellationToken ct = default)
    {
        await updatePostValidator.ValidateAndThrowAsync(request, ct);
        var viewer = await access.GetViewerAsync(userId, ct);
        ForumTopic topic = null!;
        ForumPost post = null!;
        List<Mention> before = [];
        await db.ExecuteSerializableAsync(async token =>
        {
            post = await RequirePostAsync(postId, token);
            topic = await access.RequireTopicAsync(viewer, post.TopicId, token);
            if (post.IsDeleted || post.AuthorId != userId)
                throw new ForbiddenException("Only the author can edit this message.", "forum.not_author");
            if (topic.IsClosed && !viewer.CanModerate(topic.SubjectId))
                throw new ConflictException("The topic is closed.", "forum.topic_closed");
            await EnsureNotBannedAsync(userId, token);
            before = MentionPattern().Matches(post.Body).Select(m => new Mention(Guid.Parse(m.Groups["id"].Value))).ToList();
            var ids = request.AttachmentIds ?? [];
            var keep = await AttachmentService.ClaimAsync(db, userId, ids, post.Id, token);
            var current = await db.Attachments.Where(a => a.ForumPostId == post.Id).ToListAsync(token);
            foreach (var removed in current.Where(a => !ids.Contains(a.Id))) removed.ForumPostId = null;
            foreach (var file in keep) file.ForumPostId = post.Id;
            post.Body = request.Body?.Trim() ?? "";
            post.EditedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }, ct);

        await PublishAsync(new(topic.Id, ForumEventKinds.PostChanged, post.Id), ct);
        var audience = await access.AudienceAsync(topic.SubjectId, ct);
        var added = Mentions(post.Body).Except(before.Select(m => m.UserId)).Where(audience.Contains).Where(id => id != userId);
        await notifications.NotifyAsync(added, new(NotificationType.ForumMention, topic.Title, viewer.FullName, Link(topic.Id, post.Id)), ct);
        return (await PostDtosAsync(viewer, topic, [post], ct))[0];
    }

    public async Task DeletePostAsync(Guid userId, long postId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topicId = 0;
        await db.ExecuteSerializableAsync(async token =>
        {
            var post = await RequirePostAsync(postId, token);
            var topic = await access.RequireTopicAsync(viewer, post.TopicId, token);
            topicId = topic.Id;
            if (post.IsDeleted) return;
            if (post.AuthorId != userId && !viewer.CanModerate(topic.SubjectId))
                throw new ForbiddenException("Only the author or a moderator can delete this message.", "forum.not_author");
            await SoftDeleteAsync(db, post, topic, userId, token);
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.PostChanged, postId), ct);
    }

    public async Task SetPostPinnedAsync(Guid userId, long postId, bool pinned, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topicId = 0;
        await db.ExecuteSerializableAsync(async token =>
        {
            var post = await RequirePostAsync(postId, token);
            var topic = await access.RequireTopicAsync(viewer, post.TopicId, token);
            topicId = topic.Id;
            RequireModerator(viewer, topic.SubjectId);
            if (post.IsDeleted && pinned) throw new ConflictException("A deleted message cannot be pinned.", "forum.post_deleted");
            post.IsPinned = pinned;
            post.PinnedAtUtc = pinned ? DateTime.UtcNow : null;
            post.PinnedById = pinned ? userId : null;
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.TopicChanged, postId), ct);
    }

    public async Task<IReadOnlyList<ForumReactionSummaryDto>> SetReactionAsync(Guid userId, long postId, ForumReactionKind kind,
        bool active, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind)) throw new ValidationException("Unknown reaction.");
        var viewer = await access.GetViewerAsync(userId, ct);
        var post = await RequirePostAsync(postId, ct);
        await access.RequireTopicAsync(viewer, post.TopicId, ct);
        if (post.IsDeleted) throw new ConflictException("The message was deleted.", "forum.post_deleted");
        var existing = await db.ForumReactions.SingleOrDefaultAsync(r => r.PostId == postId && r.UserId == userId && r.Kind == kind, ct);
        if (active && existing is null) db.ForumReactions.Add(new ForumReaction { PostId = postId, UserId = userId, Kind = kind });
        if (!active && existing is not null) db.ForumReactions.Remove(existing);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (active) { /* Двойной клик: реакция уже стоит. */ }
        await PublishAsync(new(post.TopicId, ForumEventKinds.PostChanged, postId), ct);
        var reactions = await db.ForumReactions.AsNoTracking().Where(r => r.PostId == postId).ToListAsync(ct);
        return Summaries(reactions, userId);
    }

    public async Task MarkReadAsync(Guid userId, int topicId, long lastPostId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        await access.RequireTopicAsync(viewer, topicId, ct);
        var max = await db.ForumPosts.Where(p => p.TopicId == topicId).MaxAsync(p => (long?)p.Id, ct) ?? 0;
        await db.ExecuteSerializableAsync(async token =>
        {
            await UpsertReadAsync(topicId, userId, Math.Min(lastPostId, max), token);
            await db.SaveChangesAsync(token);
        }, ct);
    }

    public async Task SetSubscriptionAsync(Guid userId, int topicId, bool subscribed, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        await access.RequireTopicAsync(viewer, topicId, ct);
        var existing = await db.ForumSubscriptions.SingleOrDefaultAsync(s => s.TopicId == topicId && s.UserId == userId, ct);
        if (subscribed && existing is null) db.ForumSubscriptions.Add(new ForumSubscription { TopicId = topicId, UserId = userId });
        if (!subscribed && existing is not null) db.ForumSubscriptions.Remove(existing);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (subscribed) { }
    }

    public async Task<IReadOnlyList<ForumMentionCandidateDto>> GetMentionCandidatesAsync(Guid userId, int topicId, string? query,
        CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topic = await access.RequireTopicAsync(viewer, topicId, ct);
        var term = query?.Trim().ToLowerInvariant() ?? "";
        if (term.Length < 2) return [];
        var page = await identity.SearchAchievementRecipientsAsync(term, 1, 50, ct);
        var audience = await access.AudienceAsync(topic.SubjectId, ct);
        return page.Items.Where(u => audience.Contains(u.UserId) && u.UserId != userId).Take(8)
            .Select(u => new ForumMentionCandidateDto(u.UserId, u.FullName, u.GroupName, u.IsAdmin)).ToList();
    }

    internal static async Task SoftDeleteAsync(IApplicationDbContext db, ForumPost post, ForumTopic topic, Guid actorId, CancellationToken ct)
    {
        post.IsDeleted = true;
        post.IsPinned = false;
        post.DeletedAtUtc = DateTime.UtcNow;
        post.DeletedById = actorId;
        topic.PostCount = Math.Max(0, topic.PostCount - 1);
        // Файлы удалённого сообщения становятся висячими и стираются с диска фоновой очисткой.
        foreach (var file in await db.Attachments.Where(a => a.ForumPostId == post.Id).ToListAsync(ct))
            file.ForumPostId = null;
        var reactions = await db.ForumReactions.Where(r => r.PostId == post.Id).ToListAsync(ct);
        db.ForumReactions.RemoveRange(reactions);
    }

    private IQueryable<ForumTopic> Visible(ForumViewer viewer)
    {
        var subjects = viewer.SubjectIds.ToList();
        return db.ForumTopics.AsNoTracking().Where(t => !t.IsDeleted && (t.SubjectId == null || subjects.Contains(t.SubjectId.Value)));
    }

    /// <summary>Непрочитанные чужие сообщения по темам; темы без непрочитанного в результат не попадают.</summary>
    private async Task<Dictionary<int, int>> UnreadCountsAsync(ForumViewer viewer, IQueryable<int> topicIds, CancellationToken ct)
    {
        var me = viewer.UserId;
        var rows = await (from p in db.ForumPosts.AsNoTracking()
            where topicIds.Contains(p.TopicId) && !p.IsDeleted && p.AuthorId != me &&
                  p.Id > (db.ForumTopicReads.Where(r => r.TopicId == p.TopicId && r.UserId == me)
                      .Select(r => (long?)r.LastReadPostId).FirstOrDefault() ?? 0)
            group p by p.TopicId into g
            select new { TopicId = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.TopicId, r => r.Count);
    }

    private async Task<List<ForumTopicSummaryDto>> SummariesAsync(ForumViewer viewer, IReadOnlyList<ForumTopic> topics, CancellationToken ct)
    {
        var ids = topics.Select(t => t.Id).ToList();
        var unread = await UnreadCountsAsync(viewer, db.ForumTopics.Where(t => ids.Contains(t.Id)).Select(t => t.Id), ct);
        var subjectNames = await SubjectNamesAsync(topics.Select(t => t.SubjectId), ct);
        var authors = await identity.GetProfileAccountsAsync(topics.Where(t => t.AuthorId.HasValue).Select(t => t.AuthorId!.Value), ct);
        return topics.Select(t => new ForumTopicSummaryDto(t.Id, t.SubjectId,
            t.SubjectId is int s ? subjectNames.GetValueOrDefault(s) : null, t.Title, Author(t.AuthorId, t.AuthorName, authors),
            t.CreatedAtUtc, t.LastActivityAtUtc, t.PostCount, unread.GetValueOrDefault(t.Id), t.IsPinned, t.IsClosed,
            t.IsAnnouncement)).ToList();
    }

    private async Task<Dictionary<int, string>> SubjectNamesAsync(IEnumerable<int?> subjectIds, CancellationToken ct)
    {
        var ids = subjectIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        return await db.Subjects.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
    }

    private async Task<ForumTopicDto> TopicDtoAsync(ForumViewer viewer, ForumTopic topic, CancellationToken ct)
    {
        var summary = (await SummariesAsync(viewer, [topic], ct))[0];
        var pinned = await db.ForumPosts.AsNoTracking().Where(p => p.TopicId == topic.Id && p.IsPinned && !p.IsDeleted)
            .OrderBy(p => p.PinnedAtUtc).ToListAsync(ct);
        var ban = await ActiveBanAsync(viewer.UserId, ct);
        var moderator = viewer.CanModerate(topic.SubjectId);
        var blocked = ban is not null ? "forum.banned" :
            topic.IsClosed && !moderator ? "forum.topic_closed" :
            topic.IsAnnouncement && !moderator ? "forum.announcement" : null;
        var subscribed = await db.ForumSubscriptions.AnyAsync(s => s.TopicId == topic.Id && s.UserId == viewer.UserId, ct);
        var lastRead = await db.ForumTopicReads.Where(r => r.TopicId == topic.Id && r.UserId == viewer.UserId)
            .Select(r => r.LastReadPostId).FirstOrDefaultAsync(ct);
        return new(summary, await PostDtosAsync(viewer, topic, pinned, ct),
            new(blocked is null, moderator, subscribed, blocked, ban?.UntilUtc), lastRead);
    }

    private async Task<List<ForumPostDto>> PostDtosAsync(ForumViewer viewer, ForumTopic topic, IReadOnlyList<ForumPost> posts,
        CancellationToken ct)
    {
        var ids = posts.Select(p => p.Id).ToList();
        var files = (await db.Attachments.AsNoTracking().Where(a => a.ForumPostId != null && ids.Contains(a.ForumPostId.Value))
                .OrderBy(a => a.CreatedAtUtc).ToListAsync(ct))
            .GroupBy(a => a.ForumPostId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var reactions = (await db.ForumReactions.AsNoTracking().Where(r => ids.Contains(r.PostId)).ToListAsync(ct))
            .GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var replyIds = posts.Where(p => p.ReplyToPostId.HasValue).Select(p => p.ReplyToPostId!.Value).Distinct().ToList();
        var replies = await db.ForumPosts.AsNoTracking().Where(p => replyIds.Contains(p.Id))
            .Select(p => new { p.Id, p.AuthorId, p.AuthorName, p.Body, p.IsDeleted }).ToDictionaryAsync(p => p.Id, ct);
        var authors = await identity.GetProfileAccountsAsync(posts.Where(p => p.AuthorId.HasValue).Select(p => p.AuthorId!.Value), ct);
        var moderator = viewer.CanModerate(topic.SubjectId);
        return posts.Select(p =>
        {
            var reply = p.ReplyToPostId is long r && replies.TryGetValue(r, out var quoted)
                ? new ForumQuoteDto(quoted.Id, quoted.AuthorName, quoted.IsDeleted ? "" : Excerpt(quoted.Body), quoted.IsDeleted, quoted.AuthorId) : null;
            var mine = p.AuthorId == viewer.UserId;
            return new ForumPostDto(p.Id, p.TopicId, Author(p.AuthorId, p.AuthorName, authors), p.IsDeleted ? "" : p.Body, reply,
                p.CreatedAtUtc, p.EditedAtUtc, p.IsPinned, p.IsDeleted,
                p.IsDeleted ? [] : files.GetValueOrDefault(p.Id, []).Select(a => AttachmentService.ToDto(a, signer)).ToList(),
                Summaries(reactions.GetValueOrDefault(p.Id, []), viewer.UserId),
                !p.IsDeleted && mine && (!topic.IsClosed || moderator), !p.IsDeleted && (mine || moderator));
        }).ToList();
    }

    private static List<ForumReactionSummaryDto> Summaries(IEnumerable<ForumReaction> reactions, Guid viewerId) =>
        reactions.GroupBy(r => r.Kind).OrderBy(g => g.Key)
            .Select(g => new ForumReactionSummaryDto(g.Key, g.Count(), g.Any(r => r.UserId == viewerId))).ToList();

    private static ForumUserDto Author(Guid? id, string snapshot, IReadOnlyDictionary<Guid, ProfileAccountInfo> accounts) =>
        id is Guid userId && accounts.TryGetValue(userId, out var account)
            ? new(userId, account.FullName, account.IsAdmin)
            : new(null, snapshot, false);

    internal static string Excerpt(string body)
    {
        var plain = MentionPattern().Replace(body, m => "@" + m.Groups["name"].Value).ReplaceLineEndings(" ").Trim();
        return plain.Length <= QuoteLength ? plain : plain[..QuoteLength].TrimEnd() + "…";
    }

    private static IEnumerable<Guid> Mentions(string body) =>
        MentionPattern().Matches(body).Select(m => Guid.Parse(m.Groups["id"].Value)).Distinct();

    private static string Link(int topicId, long postId) => $"forum/{topicId}?post={postId}";

    private static void RequireModerator(ForumViewer viewer, int? subjectId)
    {
        if (!viewer.CanModerate(subjectId))
            throw new ForbiddenException("Only teachers of this subject can moderate it.", "forum.moderation_required");
    }

    private async Task EnsureCanReplyAsync(ForumViewer viewer, ForumTopic topic, CancellationToken ct)
    {
        await EnsureNotBannedAsync(viewer.UserId, ct);
        var moderator = viewer.CanModerate(topic.SubjectId);
        if (topic.IsClosed && !moderator) throw new ConflictException("The topic is closed.", "forum.topic_closed");
        if (topic.IsAnnouncement && !moderator)
            throw new ForbiddenException("Only teachers can reply to announcements.", "forum.announcement");
    }

    private async Task EnsureNotBannedAsync(Guid userId, CancellationToken ct)
    {
        var ban = await ActiveBanAsync(userId, ct);
        if (ban is not null)
            throw new ForbiddenException("You cannot post on the forum right now.", "forum.banned",
                new Dictionary<string, object?> { ["untilUtc"] = ban.UntilUtc });
    }

    private Task<ForumBan?> ActiveBanAsync(Guid userId, CancellationToken ct) =>
        db.ForumBans.AsNoTracking().SingleOrDefaultAsync(b => b.UserId == userId && b.UntilUtc > DateTime.UtcNow, ct);

    private async Task<ForumPost> RequirePostAsync(long postId, CancellationToken ct) =>
        await db.ForumPosts.SingleOrDefaultAsync(p => p.Id == postId, ct)
        ?? throw new NotFoundException("Message not found.", "forum.post_not_found");

    private async Task UpsertReadAsync(int topicId, Guid userId, long postId, CancellationToken ct)
    {
        var read = await db.ForumTopicReads.SingleOrDefaultAsync(r => r.TopicId == topicId && r.UserId == userId, ct);
        if (read is null) db.ForumTopicReads.Add(new ForumTopicRead { TopicId = topicId, UserId = userId, LastReadPostId = postId });
        else if (read.LastReadPostId < postId) read.LastReadPostId = postId;
    }

    private async Task NotifyRepliesAsync(ForumViewer viewer, ForumTopic topic, ForumPost post, Guid? repliedAuthor, CancellationToken ct)
    {
        try
        {
            var audience = await access.AudienceAsync(topic.SubjectId, ct);
            var mentioned = Mentions(post.Body).Where(audience.Contains).Where(id => id != viewer.UserId).ToHashSet();
            var subscribers = await db.ForumSubscriptions.Where(s => s.TopicId == topic.Id).Select(s => s.UserId).ToListAsync(ct);
            if (repliedAuthor is Guid replied) subscribers.Add(replied);
            var link = Link(topic.Id, post.Id);
            await notifications.NotifyAsync(mentioned, new(NotificationType.ForumMention, topic.Title, viewer.FullName, link), ct);
            await notifications.NotifyAsync(subscribers.Where(id => id != viewer.UserId && !mentioned.Contains(id) && audience.Contains(id)),
                new(NotificationType.ForumReply, topic.Title, viewer.FullName, link), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Post {PostId} was saved, but reply notifications failed.", post.Id);
        }
    }

    private async Task PublishAsync(ForumTopicEventDto update, CancellationToken ct)
    {
        try { await forumNotifier.TopicChangedAsync(update, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Forum change committed, but realtime notification failed for topic {TopicId}.", update.TopicId);
        }
    }

    private sealed record Mention(Guid UserId);
}

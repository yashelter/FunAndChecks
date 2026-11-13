using System.Text.RegularExpressions;
using FluentValidation;
using FunAndChecks.Application.Attachments;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Forum;

public interface IForumService
{
    Task SendAnnouncementAsync(Guid userId, CreateForumAnnouncementRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ForumSectionDto>> GetSectionsAsync(Guid userId, CancellationToken ct = default);
    Task<ForumTopicPageDto> GetTopicsAsync(Guid userId, int? subjectId, bool generalOnly, string? search, int page, int pageSize,
        bool personalOnly = false, CancellationToken ct = default);
    Task<ForumFoldersDto> GetFoldersAsync(Guid userId, CancellationToken ct = default);
    Task<ForumTopicDto> OpenDirectAsync(Guid userId, OpenDirectChatRequest request, CancellationToken ct = default);
    Task<ForumTopicDto> OpenSavedAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ForumMentionCandidateDto>> GetContactsAsync(Guid userId, string? query, CancellationToken ct = default);
    Task<ForumPostDto> ForwardPostAsync(Guid userId, long postId, ForwardForumPostRequest request, CancellationToken ct = default);
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
    Task<IReadOnlyList<ForumReactionSummaryDto>> SetReactionAsync(Guid userId, long postId, int reactionId, bool active,
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
    private const int ANNOUNCEMENT_MAX_LENGTH = 300;

    public async Task SendAnnouncementAsync(Guid userId, CreateForumAnnouncementRequest request, CancellationToken ct = default)
    {
        IReadOnlyCollection<string> roles = await identity.GetCurrentRolesAsync(userId, ct);
        if (!roles.Contains(Roles.SuperAdmin))
        {
            throw new ForbiddenException("Only a super admin can send a system announcement.", "forum.super_admin_required");
        }

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > ANNOUNCEMENT_MAX_LENGTH)
        {
            throw new ValidationException($"Announcement text must contain 1–{ANNOUNCEMENT_MAX_LENGTH} characters.");
        }

        IReadOnlySet<Guid> recipients = await access.AudienceAsync(subjectId: null, ct: ct);
        await notifications.SendAsync(recipients,
            new NotificationRequest(NotificationType.ForumAnnouncement, request.Text.Trim(), ActorName: null, Link: null), ct);
    }

    private const int QuoteLength = 160;

    [GeneratedRegex(@"(?:@\[|\[@)(?<name>[^\]\r\n]{1,100})\]\(user:(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\)")]
    private static partial Regex MentionPattern();

    public async Task<IReadOnlyList<ForumSectionDto>> GetSectionsAsync(Guid userId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var sectionTopics = Visible(viewer).Where(t => t.Kind == ForumTopicKind.Section);
        var topics = await sectionTopics.Select(t => new { t.Id, t.SubjectId }).ToListAsync(ct);
        var unread = (await UnreadCountsAsync(viewer, sectionTopics.Select(t => t.Id), ct)).Keys.ToHashSet();
        var subjects = await db.Subjects.AsNoTracking().Where(s => viewer.SubjectIds.Contains(s.Id))
            .OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var sections = new List<ForumSectionDto>
        {
            new(null, null, topics.Count(t => t.SubjectId == null), topics.Count(t => t.SubjectId == null && unread.Contains(t.Id)),
                viewer.CanModerate((int?)null)),
        };
        sections.AddRange(subjects.Select(s => new ForumSectionDto(s.Id, s.Name, topics.Count(t => t.SubjectId == s.Id),
            topics.Count(t => t.SubjectId == s.Id && unread.Contains(t.Id)), viewer.CanModerate(s.Id))));
        return sections;
    }

    public async Task<ForumTopicPageDto> GetTopicsAsync(Guid userId, int? subjectId, bool generalOnly, string? search,
        int page, int pageSize, bool personalOnly = false, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        var viewer = await access.GetViewerAsync(userId, ct);
        var topics = Visible(viewer);
        if (personalOnly) topics = topics.Where(t => t.Kind != ForumTopicKind.Section);
        else if (generalOnly) topics = topics.Where(t => t.Kind == ForumTopicKind.Section && t.SubjectId == null);
        else if (subjectId is int id) topics = topics.Where(t => t.SubjectId == id);
        var term = search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(term))
        {
            // Личный чат ищется и по имени собеседника: своего заголовка у него нет.
            var people = await PeopleMatchingAsync(term, ct);
            var me = viewer.UserId;
            topics = topics.Where(t => t.Title.ToLower().Contains(term) ||
                (t.Kind == ForumTopicKind.Direct && t.Members.Any(m => m.UserId != me && people.Contains(m.UserId))) ||
                t.Posts.Any(p => !p.IsDeleted && p.Body.ToLower().Contains(term)));
        }
        var total = await topics.CountAsync(ct);
        // «Избранное» всегда первым, затем закреплённые темы, затем по последней активности — как в мессенджере.
        var entries = await topics.Include(t => t.Subject)
            .OrderByDescending(t => t.Kind == ForumTopicKind.Saved).ThenByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.LastActivityAtUtc).ThenByDescending(t => t.Id)
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
        await PublishChatsAsync(topic, ct);
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
            RequireModerator(viewer, topic);
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
        await PublishChatsAsync(topic, ct);
        return await TopicDtoAsync(viewer, topic, ct);
    }

    public async Task DeleteTopicAsync(Guid userId, int topicId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        ForumTopic topic = null!;
        await db.ExecuteSerializableAsync(async token =>
        {
            topic = await access.RequireTopicAsync(viewer, topicId, token);
            RequireModerator(viewer, topic);
            topic.IsDeleted = true;
            topic.DeletedAtUtc = DateTime.UtcNow;
            topic.DeletedById = userId;
            // Как при удалении отдельного сообщения: файлы удалённой темы становятся висячими
            // и стираются фоновой очисткой, а не занимают диск навсегда.
            foreach (var file in await db.Attachments.Where(a => a.ForumPost != null && a.ForumPost.TopicId == topicId).ToListAsync(token))
                file.ForumPostId = null;
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.TopicChanged, null), ct);
        await PublishChatsAsync(topic, ct);
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
            if (request.StickerId is int stickerId && !await db.ForumStickers.AnyAsync(s => s.Id == stickerId && !s.IsDeleted &&
                    s.Pack.IsPublished && !s.Pack.IsDeleted, token))
                throw new NotFoundException("Sticker not found.", "forum.sticker_not_found");
            var files = await AttachmentService.ClaimAsync(db, userId, request.AttachmentIds ?? [], null, token);
            post = new ForumPost
            {
                TopicId = topicId, AuthorId = userId, AuthorName = viewer.FullName, Body = request.Body?.Trim() ?? "",
                ReplyToPostId = request.ReplyToPostId, CreatedAtUtc = DateTime.UtcNow, StickerId = request.StickerId,
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
        await PublishChatsAsync(topic, ct);
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
            if (post.StickerId is not null || post.ForwardedFromPostId is not null)
                throw new ConflictException("Stickers and forwarded messages cannot be edited.", "forum.post_not_editable");
            if (topic.IsClosed && !viewer.CanModerate(topic))
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
        var audience = await access.AudienceAsync(topic, ct);
        var added = Mentions(post.Body).Except(before.Select(m => m.UserId)).Where(audience.Contains).Where(id => id != userId);
        await notifications.NotifyAsync(added, new(NotificationType.ForumMention, topic.Title, viewer.FullName, Link(topic.Id, post.Id)), ct);
        return (await PostDtosAsync(viewer, topic, [post], ct))[0];
    }

    public async Task DeletePostAsync(Guid userId, long postId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topicId = 0;
        ForumTopic topic = null!;
        await db.ExecuteSerializableAsync(async token =>
        {
            var post = await RequirePostAsync(postId, token);
            topic = await access.RequireTopicAsync(viewer, post.TopicId, token);
            topicId = topic.Id;
            if (post.IsDeleted) return;
            if (post.AuthorId != userId && !viewer.CanModerate(topic))
                throw new ForbiddenException("Only the author or a moderator can delete this message.", "forum.not_author");
            await SoftDeleteAsync(db, post, topic, userId, token);
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.PostChanged, postId), ct);
        await PublishChatsAsync(topic, ct);
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
            RequireModerator(viewer, topic);
            if (post.IsDeleted && pinned) throw new ConflictException("A deleted message cannot be pinned.", "forum.post_deleted");
            post.IsPinned = pinned;
            post.PinnedAtUtc = pinned ? DateTime.UtcNow : null;
            post.PinnedById = pinned ? userId : null;
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topicId, ForumEventKinds.TopicChanged, postId), ct);
    }

    public async Task<IReadOnlyList<ForumReactionSummaryDto>> SetReactionAsync(Guid userId, long postId, int reactionId,
        bool active, CancellationToken ct = default)
    {
        var type = await db.ForumReactionTypes.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reactionId, ct)
            ?? throw new NotFoundException("Unknown reaction.", "forum.reaction_not_found");
        if (active && !type.IsEnabled) throw new ConflictException("This reaction is turned off.", "forum.reaction_disabled");
        var viewer = await access.GetViewerAsync(userId, ct);
        var post = await RequirePostAsync(postId, ct);
        await access.RequireTopicAsync(viewer, post.TopicId, ct);
        if (post.IsDeleted) throw new ConflictException("The message was deleted.", "forum.post_deleted");
        var existing = await db.ForumReactions.SingleOrDefaultAsync(r => r.PostId == postId && r.UserId == userId &&
            r.ReactionTypeId == reactionId, ct);
        if (active && existing is null) db.ForumReactions.Add(new ForumReaction { PostId = postId, UserId = userId, ReactionTypeId = reactionId });
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
        var audience = await access.AudienceAsync(topic, ct);
        return page.Items.Where(u => audience.Contains(u.UserId) && u.UserId != userId).Take(8)
            .Select(u => new ForumMentionCandidateDto(u.UserId, u.FullName, u.GroupName, u.IsAdmin)).ToList();
    }

    public async Task<ForumFoldersDto> GetFoldersAsync(Guid userId, CancellationToken ct = default)
    {
        var sections = await GetSectionsAsync(userId, ct);
        var viewer = await access.GetViewerAsync(userId, ct);
        var personal = Visible(viewer).Where(t => t.Kind != ForumTopicKind.Section);
        var count = await personal.CountAsync(ct);
        var unread = (await UnreadCountsAsync(viewer, personal.Select(t => t.Id), ct)).Count;
        var savedKey = SavedKey(userId);
        int? saved = viewer.IsAdmin
            ? await db.ForumTopics.Where(t => t.DirectKey == savedKey && !t.IsDeleted).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct)
            : null;
        return new(count, unread, sections, viewer.IsAdmin, saved);
    }

    public async Task<ForumTopicDto> OpenSavedAsync(Guid userId, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var topic = await SavedTopicAsync(viewer, ct);
        return await TopicDtoAsync(viewer, topic, ct);
    }

    public async Task<ForumTopicDto> OpenDirectAsync(Guid userId, OpenDirectChatRequest request, CancellationToken ct = default)
    {
        if (request.UserId == userId || request.UserId == Guid.Empty)
            throw new ValidationException("Choose someone else to write to.");
        var viewer = await access.GetViewerAsync(userId, ct);
        var key = DirectKey(userId, request.UserId);
        // Уже начатый чат открывается всегда: группа студента могла смениться, а переписка остаётся.
        var topic = await db.ForumTopics.AsNoTracking().SingleOrDefaultAsync(t => t.DirectKey == key && !t.IsDeleted, ct);
        if (topic is null)
        {
            if (!await CanStartDirectAsync(viewer, request.UserId, ct))
                throw new ForbiddenException("You cannot start a chat with this person.", "forum.direct_not_allowed");
            topic = await MemberTopicAsync(key, ForumTopicKind.Direct, viewer, [userId, request.UserId], ct);
        }
        return await TopicDtoAsync(viewer, topic, ct);
    }

    public async Task<IReadOnlyList<ForumMentionCandidateDto>> GetContactsAsync(Guid userId, string? query, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var term = query?.Trim() ?? "";
        if (viewer.IsAdmin)
        {
            // Преподавателю доступен любой подтверждённый аккаунт — список слишком велик без поиска.
            if (term.Length < 2) return [];
            var page = await identity.SearchAchievementRecipientsAsync(term, 1, 30, ct);
            return page.Items.Where(u => u.UserId != userId).Take(20)
                .Select(u => new ForumMentionCandidateDto(u.UserId, u.FullName, u.GroupName, u.IsAdmin)).ToList();
        }
        var accounts = await identity.GetProfileAccountsAsync(await AllowedContactsAsync(viewer, ct), ct);
        return accounts.Values
            .Where(a => term.Length == 0 || a.FullName.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(a => a.IsAdmin).ThenBy(a => a.LastName).ThenBy(a => a.FirstName).Take(50)
            .Select(a => new ForumMentionCandidateDto(a.UserId, a.FullName, a.GroupName, a.IsAdmin)).ToList();
    }

    public async Task<ForumPostDto> ForwardPostAsync(Guid userId, long postId, ForwardForumPostRequest request, CancellationToken ct = default)
    {
        var viewer = await access.GetViewerAsync(userId, ct);
        var source = await db.ForumPosts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == postId, ct)
            ?? throw new NotFoundException("Message not found.", "forum.post_not_found");
        await access.RequireTopicAsync(viewer, source.TopicId, ct);
        if (source.IsDeleted) throw new ConflictException("The message was deleted.", "forum.post_deleted");
        var targetId = request.ToSaved ? (await SavedTopicAsync(viewer, ct)).Id
            : request.TopicId ?? throw new ValidationException("Choose where to forward the message.");
        ForumTopic topic = null!;
        ForumPost post = null!;
        await db.ExecuteSerializableAsync(async token =>
        {
            topic = await access.RequireTopicAsync(viewer, targetId, token);
            await EnsureCanReplyAsync(viewer, topic, token);
            post = new ForumPost
            {
                TopicId = topic.Id, AuthorId = userId, AuthorName = viewer.FullName, Body = source.Body, StickerId = source.StickerId,
                // Пересылка пересланного ведёт к первоисточнику: там файлы и автор.
                ForwardedFromPostId = source.ForwardedFromPostId ?? source.Id, CreatedAtUtc = DateTime.UtcNow,
            };
            db.ForumPosts.Add(post);
            topic.PostCount++;
            topic.LastActivityAtUtc = post.CreatedAtUtc;
            await db.SaveChangesAsync(token);
            await UpsertReadAsync(topic.Id, userId, post.Id, token);
            await db.SaveChangesAsync(token);
        }, ct);
        await PublishAsync(new(topic.Id, ForumEventKinds.PostCreated, post.Id), ct);
        await PublishChatsAsync(topic, ct);
        await NotifyRepliesAsync(viewer, topic, post, null, ct);
        return (await PostDtosAsync(viewer, topic, [post], ct))[0];
    }

    private async Task<ForumTopic> SavedTopicAsync(ForumViewer viewer, CancellationToken ct)
    {
        if (!viewer.IsAdmin) throw new ForbiddenException("Saved messages are available to teachers.", "forum.saved_admins_only");
        return await MemberTopicAsync(SavedKey(viewer.UserId), ForumTopicKind.Saved, viewer, [viewer.UserId], ct);
    }

    /// <summary>Личный чат или «Избранное» по ключу; создаётся при первом обращении.</summary>
    private async Task<ForumTopic> MemberTopicAsync(string key, ForumTopicKind kind, ForumViewer viewer, IReadOnlyList<Guid> members,
        CancellationToken ct)
    {
        var existing = await db.ForumTopics.AsNoTracking().SingleOrDefaultAsync(t => t.DirectKey == key, ct);
        if (existing is not null) return existing;
        ForumTopic topic = null!;
        try
        {
            await db.ExecuteSerializableAsync(async token =>
            {
                var now = DateTime.UtcNow;
                topic = new ForumTopic
                {
                    Kind = kind, DirectKey = key, Title = "", AuthorId = viewer.UserId, AuthorName = viewer.FullName,
                    CreatedAtUtc = now, LastActivityAtUtc = now,
                };
                db.ForumTopics.Add(topic);
                await db.SaveChangesAsync(token);
                foreach (var member in members.Distinct())
                {
                    db.ForumTopicMembers.Add(new ForumTopicMember { TopicId = topic.Id, UserId = member });
                    // Собеседник получает уведомления о сообщениях, хотя сам ещё ничего не писал.
                    if (kind == ForumTopicKind.Direct) db.ForumSubscriptions.Add(new ForumSubscription { TopicId = topic.Id, UserId = member });
                }
                await db.SaveChangesAsync(token);
            }, ct);
            return topic;
        }
        catch (DbUpdateException)
        {
            // Чат одновременно открыли двое: берём созданный другим запросом.
            return await db.ForumTopics.AsNoTracking().SingleAsync(t => t.DirectKey == key, ct);
        }
    }

    private async Task<bool> CanStartDirectAsync(ForumViewer viewer, Guid targetId, CancellationToken ct)
    {
        if (await identity.GetProfileAccountAsync(targetId, ct) is null) return false;
        return viewer.IsAdmin || (await AllowedContactsAsync(viewer, ct)).Contains(targetId);
    }

    /// <summary>Кому студент может написать первым: преподавателям своих предметов и одногруппникам.</summary>
    private async Task<HashSet<Guid>> AllowedContactsAsync(ForumViewer viewer, CancellationToken ct)
    {
        var subjects = viewer.SubjectIds.ToList();
        var result = (await db.AdminSubjectAccesses.Where(a => subjects.Contains(a.SubjectId) && !a.IsRestricted)
            .Select(a => a.AdminId).Distinct().ToListAsync(ct)).ToHashSet();
        result.UnionWith(await identity.GetUserIdsInRoleAsync(Roles.SuperAdmin, ct));
        var groupId = await db.Students.Where(s => s.Id == viewer.UserId).Select(s => s.GroupId).FirstOrDefaultAsync(ct);
        if (groupId is not null)
            result.UnionWith(await db.Students.Where(s => s.GroupId == groupId && s.IsActive).Select(s => s.Id).ToListAsync(ct));
        result.Remove(viewer.UserId);
        return result;
    }

    private async Task<List<Guid>> PeopleMatchingAsync(string term, CancellationToken ct)
    {
        if (term.Length < 2) return [];
        var page = await identity.SearchAchievementRecipientsAsync(term, 1, 50, ct);
        return page.Items.Select(u => u.UserId).ToList();
    }

    private static string SavedKey(Guid userId) => $"saved:{userId}";

    private static string DirectKey(Guid a, Guid b)
    {
        var (first, second) = string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? (a, b) : (b, a);
        return $"direct:{first}:{second}";
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
        var me = viewer.UserId;
        // Пустой личный чат виден только тому, кто его открыл: собеседник увидит его с первым сообщением.
        return db.ForumTopics.AsNoTracking().Where(t => !t.IsDeleted && (
            (t.Kind == ForumTopicKind.Section && (t.SubjectId == null || subjects.Contains(t.SubjectId.Value))) ||
            (t.Kind != ForumTopicKind.Section && t.Members.Any(m => m.UserId == me) &&
                (t.Kind != ForumTopicKind.Direct || t.PostCount > 0 || t.AuthorId == me))));
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
        var me = viewer.UserId;
        var directIds = topics.Where(t => t.Kind == ForumTopicKind.Direct).Select(t => t.Id).ToList();
        var peers = (await db.ForumTopicMembers.AsNoTracking().Where(m => directIds.Contains(m.TopicId) && m.UserId != me)
                .Select(m => new { m.TopicId, m.UserId }).ToListAsync(ct))
            .GroupBy(m => m.TopicId).ToDictionary(g => g.Key, g => g.First().UserId);
        var lastIds = await db.ForumPosts.AsNoTracking().Where(p => ids.Contains(p.TopicId) && !p.IsDeleted)
            .GroupBy(p => p.TopicId).Select(g => g.Max(p => p.Id)).ToListAsync(ct);
        var lasts = (await db.ForumPosts.AsNoTracking().Where(p => lastIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id, p.TopicId, p.AuthorId, p.AuthorName, p.Body, p.StickerId, p.CreatedAtUtc,
                    HasFiles = db.Attachments.Any(a => a.ForumPostId == p.Id) ||
                        (p.ForwardedFromPostId != null && db.Attachments.Any(a => a.ForumPostId == p.ForwardedFromPostId)),
                })
                .ToListAsync(ct))
            .ToDictionary(p => p.TopicId);
        var authors = await identity.GetProfileAccountsAsync(topics.Where(t => t.AuthorId.HasValue).Select(t => t.AuthorId!.Value)
            .Concat(peers.Values).Distinct(), ct);
        return topics.Select(t =>
        {
            var peer = peers.TryGetValue(t.Id, out var peerId) && authors.ContainsKey(peerId) ? Author(peerId, "", authors) : null;
            var title = t.Kind switch
            {
                ForumTopicKind.Direct => peer?.Name ?? "",
                ForumTopicKind.Saved => "",
                _ => t.Title,
            };
            var last = lasts.TryGetValue(t.Id, out var l)
                ? new ForumLastPostDto(l.Id, l.AuthorName, l.AuthorId, Excerpt(l.Body), l.StickerId != null, l.HasFiles, l.CreatedAtUtc)
                : null;
            return new ForumTopicSummaryDto(t.Id, t.SubjectId,
                t.SubjectId is int s ? subjectNames.GetValueOrDefault(s) : null, title, Author(t.AuthorId, t.AuthorName, authors),
                t.CreatedAtUtc, t.LastActivityAtUtc, t.PostCount, unread.GetValueOrDefault(t.Id), t.IsPinned, t.IsClosed,
                t.IsAnnouncement, t.Kind, peer, last);
        }).ToList();
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
        var moderator = viewer.CanModerate(topic);
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
        // Пересланное сообщение показывает файлы оригинала: один файл привязан только к одному сообщению.
        var forwardIds = posts.Where(p => p.ForwardedFromPostId.HasValue).Select(p => p.ForwardedFromPostId!.Value).Distinct().ToList();
        var origins = await db.ForumPosts.AsNoTracking().Where(p => forwardIds.Contains(p.Id))
            .Select(p => new { p.Id, p.TopicId, p.AuthorId, p.AuthorName, p.IsDeleted, TopicTitle = p.Topic.Title, TopicKind = p.Topic.Kind })
            .ToDictionaryAsync(p => p.Id, ct);
        var fileOwners = ids.Concat(forwardIds).ToList();
        var files = (await db.Attachments.AsNoTracking().Where(a => a.ForumPostId != null && fileOwners.Contains(a.ForumPostId.Value))
                .OrderBy(a => a.CreatedAtUtc).ToListAsync(ct))
            .GroupBy(a => a.ForumPostId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var stickerIds = posts.Where(p => p.StickerId.HasValue).Select(p => p.StickerId!.Value).Distinct().ToList();
        var stickers = await db.ForumStickers.AsNoTracking().Where(s => stickerIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, ct);
        var reactions = (await db.ForumReactions.AsNoTracking().Where(r => ids.Contains(r.PostId)).ToListAsync(ct))
            .GroupBy(r => r.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var replyIds = posts.Where(p => p.ReplyToPostId.HasValue).Select(p => p.ReplyToPostId!.Value).Distinct().ToList();
        var replies = await db.ForumPosts.AsNoTracking().Where(p => replyIds.Contains(p.Id))
            .Select(p => new { p.Id, p.AuthorId, p.AuthorName, p.Body, p.IsDeleted }).ToDictionaryAsync(p => p.Id, ct);
        var authors = await identity.GetProfileAccountsAsync(posts.Where(p => p.AuthorId.HasValue).Select(p => p.AuthorId!.Value), ct);
        var moderator = viewer.CanModerate(topic);
        return posts.Select(p =>
        {
            var reply = p.ReplyToPostId is long r && replies.TryGetValue(r, out var quoted)
                ? new ForumQuoteDto(quoted.Id, quoted.AuthorName, quoted.IsDeleted ? "" : Excerpt(quoted.Body), quoted.IsDeleted, quoted.AuthorId) : null;
            var mine = p.AuthorId == viewer.UserId;
            var origin = p.ForwardedFromPostId is long f && origins.TryGetValue(f, out var o) ? o : null;
            var forward = origin is null ? null
                : new ForumForwardDto(origin.Id, origin.TopicId, origin.TopicKind,
                    origin.TopicKind == ForumTopicKind.Section ? origin.TopicTitle : "", origin.AuthorName, origin.AuthorId);
            var fileOwner = origin is { IsDeleted: false } ? origin.Id : p.Id;
            var sticker = p.StickerId is int sid && stickers.TryGetValue(sid, out var st)
                ? new ForumStickerDto(st.Id, st.Name, StickerUrl(st.Id)) : null;
            var editable = p.StickerId is null && p.ForwardedFromPostId is null;
            return new ForumPostDto(p.Id, p.TopicId, Author(p.AuthorId, p.AuthorName, authors), p.IsDeleted ? "" : p.Body, reply,
                p.CreatedAtUtc, p.EditedAtUtc, p.IsPinned, p.IsDeleted,
                p.IsDeleted ? [] : files.GetValueOrDefault(fileOwner, []).Select(a => AttachmentService.ToDto(a, signer)).ToList(),
                Summaries(reactions.GetValueOrDefault(p.Id, []), viewer.UserId),
                !p.IsDeleted && mine && editable && (!topic.IsClosed || moderator), !p.IsDeleted && (mine || moderator),
                p.IsDeleted ? null : sticker, p.IsDeleted ? null : forward);
        }).ToList();
    }

    private static List<ForumReactionSummaryDto> Summaries(IEnumerable<ForumReaction> reactions, Guid viewerId) =>
        reactions.GroupBy(r => r.ReactionTypeId).OrderBy(g => g.Key)
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

    internal static string StickerUrl(int stickerId) => $"api/forum/stickers/{stickerId}/image";

    private static void RequireModerator(ForumViewer viewer, ForumTopic topic)
    {
        if (!viewer.CanModerate(topic))
            throw new ForbiddenException("Only teachers of this subject can moderate it.", "forum.moderation_required");
    }

    private static void RequireModerator(ForumViewer viewer, int? subjectId)
    {
        if (!viewer.CanModerate(subjectId))
            throw new ForbiddenException("Only teachers of this subject can moderate it.", "forum.moderation_required");
    }

    private async Task EnsureCanReplyAsync(ForumViewer viewer, ForumTopic topic, CancellationToken ct)
    {
        await EnsureNotBannedAsync(viewer.UserId, ct);
        var moderator = viewer.CanModerate(topic);
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
            if (topic.Kind == ForumTopicKind.Saved) return;
            var audience = await access.AudienceAsync(topic, ct);
            if (topic.Kind == ForumTopicKind.Direct)
            {
                // В личном чате одно уведомление собеседнику, без «ответов» и упоминаний.
                await notifications.NotifyAsync(audience.Where(id => id != viewer.UserId),
                    new(NotificationType.DirectMessage, viewer.FullName, viewer.FullName, Link(topic.Id, post.Id)), ct);
                return;
            }
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

    private async Task PublishChatsAsync(ForumTopic topic, CancellationToken ct)
    {
        try
        {
            IReadOnlyCollection<Guid>? members = topic.Kind == ForumTopicKind.Section ? null
                : await db.ForumTopicMembers.Where(m => m.TopicId == topic.Id).Select(m => m.UserId).ToListAsync(ct);
            await forumNotifier.ChatsChangedAsync(members, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Forum change committed, but the chat list hint failed for topic {TopicId}.", topic.Id);
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

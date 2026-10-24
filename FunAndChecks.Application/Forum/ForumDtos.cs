using FunAndChecks.Application.Attachments;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Forum;

public record ForumUserDto(Guid? UserId, string Name, bool IsAdmin);

/// <summary>Раздел форума: «Общее» (SubjectId = null) или предмет.</summary>
public record ForumSectionDto(int? SubjectId, string? SubjectName, int TopicCount, int UnreadTopicCount, bool CanCreateTopics);

public record ForumTopicSummaryDto(int Id, int? SubjectId, string? SubjectName, string Title, ForumUserDto Author,
    DateTime CreatedAtUtc, DateTime LastActivityAtUtc, int PostCount, int UnreadCount,
    bool IsPinned, bool IsClosed, bool IsAnnouncement);

public record ForumTopicPageDto(IReadOnlyList<ForumTopicSummaryDto> Items, int TotalCount, int Page, int PageSize);

public record ForumPermissionsDto(bool CanReply, bool CanModerate, bool IsSubscribed, string? ReplyBlockedCode,
    DateTime? BannedUntilUtc);

public record ForumTopicDto(ForumTopicSummaryDto Topic, IReadOnlyList<ForumPostDto> PinnedPosts,
    ForumPermissionsDto Permissions, long LastReadPostId);

public record ForumReactionSummaryDto(ForumReactionKind Kind, int Count, bool Mine);

public record ForumQuoteDto(long Id, string AuthorName, string Excerpt, bool IsDeleted, Guid? AuthorId = null);

public record ForumPostDto(long Id, int TopicId, ForumUserDto Author, string Body, ForumQuoteDto? ReplyTo,
    DateTime CreatedAtUtc, DateTime? EditedAtUtc, bool IsPinned, bool IsDeleted,
    IReadOnlyList<AttachmentDto> Attachments, IReadOnlyList<ForumReactionSummaryDto> Reactions,
    bool CanEdit, bool CanDelete);

public record ForumPostPageDto(IReadOnlyList<ForumPostDto> Items, int TotalCount, int Page, int PageSize);

public record CreateForumTopicRequest(int? SubjectId, string? Title, string? Body, IReadOnlyList<Guid>? AttachmentIds,
    bool IsAnnouncement = false, bool IsPinned = false);

/// <summary>Частичное изменение темы модератором; null — не менять. Перенос — через ChangeSubject.</summary>
public record UpdateForumTopicRequest(string? Title = null, bool? IsClosed = null, bool? IsPinned = null,
    bool? IsAnnouncement = null, bool ChangeSubject = false, int? SubjectId = null);

public record CreateForumPostRequest(string? Body, long? ReplyToPostId, IReadOnlyList<Guid>? AttachmentIds);

public record UpdateForumPostRequest(string? Body, IReadOnlyList<Guid>? AttachmentIds);

public record MarkTopicReadRequest(long LastPostId);

public record ReportForumPostRequest(string? Reason);

public record ForumReportDto(int Id, long PostId, int TopicId, string TopicTitle, string PostExcerpt, bool PostDeleted,
    ForumUserDto PostAuthor, string ReporterName, string Reason, DateTime CreatedAtUtc);

public record ForumReportPageDto(IReadOnlyList<ForumReportDto> Items, int TotalCount, int Page, int PageSize);

public record ResolveForumReportRequest(bool DeletePost = false);

public record BanForumUserRequest(DateTime UntilUtc, string? Reason);

public record ForumBanDto(Guid UserId, string FullName, string? GroupName, DateTime UntilUtc, string Reason, DateTime CreatedAtUtc);

public record ForumMentionCandidateDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin);

public record ForumUnreadDto(int UnreadTopicCount);

/// <summary>Событие для открытой темы; клиент перечитывает нужное сам.</summary>
public record ForumTopicEventDto(int TopicId, string Kind, long? PostId);

public static class ForumEventKinds
{
    public const string PostCreated = "post-created";
    public const string PostChanged = "post-changed";
    public const string TopicChanged = "topic-changed";
}

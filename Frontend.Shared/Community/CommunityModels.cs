namespace Frontend.Shared.Community;

// Контракты форума, опросов и уведомлений. Значения перечислений совпадают с бэкендом.

public enum NotificationType { AchievementGranted = 0, ForumReply = 1, ForumMention = 2, ForumAnnouncement = 3, SurveyAvailable = 4, VariantAssigned = 5 }

public record NotificationDto(long Id, NotificationType Type, string Subject, string? ActorName, string? Link, DateTime CreatedAtUtc, bool IsRead);
public record NotificationPageDto(List<NotificationDto> Items, int TotalCount, int UnreadCount, int Page, int PageSize);

public enum ForumReactionKind { Like = 0, Heart = 1, Laugh = 2, Party = 3, Thinking = 4, Eyes = 5 }

public record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, bool IsImage, string Url);
public record ForumUserDto(Guid? UserId, string Name, bool IsAdmin);
public record ForumSectionDto(int? SubjectId, string? SubjectName, int TopicCount, int UnreadTopicCount, bool CanCreateTopics);
public record ForumTopicSummaryDto(int Id, int? SubjectId, string? SubjectName, string Title, ForumUserDto Author,
    DateTime CreatedAtUtc, DateTime LastActivityAtUtc, int PostCount, int UnreadCount, bool IsPinned, bool IsClosed, bool IsAnnouncement);
public record ForumTopicPageDto(List<ForumTopicSummaryDto> Items, int TotalCount, int Page, int PageSize);
public record ForumPermissionsDto(bool CanReply, bool CanModerate, bool IsSubscribed, string? ReplyBlockedCode, DateTime? BannedUntilUtc);
public record ForumTopicDto(ForumTopicSummaryDto Topic, List<ForumPostDto> PinnedPosts, ForumPermissionsDto Permissions, long LastReadPostId);
public record ForumReactionSummaryDto(ForumReactionKind Kind, int Count, bool Mine);
public record ForumQuoteDto(long Id, string AuthorName, string Excerpt, bool IsDeleted, Guid? AuthorId = null);
public record ForumPostDto(long Id, int TopicId, ForumUserDto Author, string Body, ForumQuoteDto? ReplyTo, DateTime CreatedAtUtc,
    DateTime? EditedAtUtc, bool IsPinned, bool IsDeleted, List<AttachmentDto> Attachments, List<ForumReactionSummaryDto> Reactions,
    bool CanEdit, bool CanDelete);
public record ForumPostPageDto(List<ForumPostDto> Items, int TotalCount, int Page, int PageSize);
public record CreateForumTopicRequest(int? SubjectId, string Title, string Body, List<Guid> AttachmentIds, bool IsAnnouncement, bool IsPinned);
public record UpdateForumTopicRequest(string? Title = null, bool? IsClosed = null, bool? IsPinned = null, bool? IsAnnouncement = null,
    bool ChangeSubject = false, int? SubjectId = null);
public record CreateForumPostRequest(string Body, long? ReplyToPostId, List<Guid> AttachmentIds);
public record UpdateForumPostRequest(string Body, List<Guid> AttachmentIds);
public record ForumReportDto(int Id, long PostId, int TopicId, string TopicTitle, string PostExcerpt, bool PostDeleted,
    ForumUserDto PostAuthor, string ReporterName, string Reason, DateTime CreatedAtUtc);
public record ForumReportPageDto(List<ForumReportDto> Items, int TotalCount, int Page, int PageSize);
public record ForumBanDto(Guid UserId, string FullName, string? GroupName, DateTime UntilUtc, string Reason, DateTime CreatedAtUtc);
public record ForumMentionCandidateDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin);
public record ForumUnreadDto(int UnreadTopicCount);
public record ForumTopicEventDto(int TopicId, string Kind, long? PostId);

public enum SurveyStatus { Draft = 0, Published = 1, Closed = 2 }
public enum SurveyTriggerType { Immediate = 0, AcceptedInSubject = 1, AttemptsInSubject = 2 }
public enum SurveyQuestionType { SingleChoice = 0, MultipleChoice = 1, Dropdown = 2, ShortText = 3, LongText = 4, Scale = 5, Number = 6 }

public record SurveyOptionDto(int Id, string Text);
public record SurveyQuestionDto(int Id, int Order, string Text, string? Description, SurveyQuestionType Type, bool IsRequired, bool AllowOther,
    int? MaxLength, int? ScaleMin, int? ScaleMax, string? ScaleMinLabel, string? ScaleMaxLabel, List<SurveyOptionDto> Options);
public record SurveyQuestionInput(string Text, string? Description, SurveyQuestionType Type, bool IsRequired, bool AllowOther, int? MaxLength,
    int? ScaleMin, int? ScaleMax, string? ScaleMinLabel, string? ScaleMaxLabel, List<string>? Options);
public record SaveSurveyRequest(string Title, string? Description, int? SubjectId, List<int> TargetGroupIds, bool IsAnonymous, bool AllowSnooze,
    int SnoozeHours, int? MaxSnoozes, DateTime? OpensAtUtc, DateTime? ClosesAtUtc, SurveyTriggerType TriggerType, int TriggerThreshold,
    List<SurveyQuestionInput> Questions);
public record UpdateSurveyDetailsRequest(string Title, string? Description, DateTime? ClosesAtUtc);
public record SurveySummaryDto(int Id, string Title, int? SubjectId, string? SubjectName, SurveyStatus Status, bool IsAnonymous,
    SurveyTriggerType TriggerType, int TriggerThreshold, int QuestionCount, int ResponseCount, string CreatedByName, DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc, DateTime? ClosesAtUtc);
public record SurveyPageDto(List<SurveySummaryDto> Items, int TotalCount, int Page, int PageSize);
public record SurveyAdminDto(int Id, string Title, string Description, int? SubjectId, string? SubjectName, List<int> TargetGroupIds,
    SurveyStatus Status, bool IsAnonymous, bool AllowSnooze, int SnoozeHours, int? MaxSnoozes, DateTime? OpensAtUtc, DateTime? ClosesAtUtc,
    SurveyTriggerType TriggerType, int TriggerThreshold, string CreatedByName, DateTime CreatedAtUtc, DateTime? PublishedAtUtc,
    int ResponseCount, List<SurveyQuestionDto> Questions);
public record SurveyOptionResultDto(int? OptionId, string Text, int Count);
public record SurveyQuestionResultDto(int QuestionId, string Text, SurveyQuestionType Type, int AnswerCount, List<SurveyOptionResultDto> Options,
    List<string> TextAnswers, decimal? Average, decimal? Median);
public record SurveyResultsDto(int SurveyId, string Title, bool IsAnonymous, int ResponseCount, int AudienceCount, bool HiddenForAnonymity,
    int AnonymityThreshold, List<SurveyQuestionResultDto> Questions, List<SurveyGroupCountDto>? Groups = null, int? GroupId = null);
public record SurveyGroupCountDto(int GroupId, string Name, int ResponseCount, int AudienceCount);
public record SurveyAnswerViewDto(int QuestionId, string Value);
public record SurveyIndividualResponseDto(int ResponseId, Guid? RespondentId, string RespondentName, string? GroupName, DateTime SubmittedAtUtc,
    List<SurveyAnswerViewDto> Answers);
public record PendingSurveyDto(int Id, string Title, string? SubjectName, bool IsAnonymous, bool CanSnooze, int SnoozeHours, int QuestionCount,
    DateTime? ClosesAtUtc);
public record SurveyFormDto(int Id, string Title, string Description, string? SubjectName, bool IsAnonymous, bool CanSnooze, int SnoozeHours,
    DateTime? ClosesAtUtc, List<SurveyQuestionDto> Questions);
public record SubmitSurveyAnswer(int QuestionId, List<int>? OptionIds, string? OtherText, string? Text, decimal? Number);
public record SubmitSurveyRequest(List<SubmitSurveyAnswer> Answers);
public record MySurveyDto(int Id, string Title, string? SubjectName, bool IsAnonymous, DateTime? CompletedAtUtc);

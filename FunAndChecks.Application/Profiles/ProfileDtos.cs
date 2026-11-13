namespace FunAndChecks.Application.Profiles;

public record MediaAssetDto(Guid Id, string ContentType, int Width, int Height,
    double DurationSeconds, bool IsAnimated, bool HasPoster);

public record UserProfileDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin,
    string AboutMe, string? GitHubUrl, string? PortfolioUrl, MediaAssetDto? Avatar,
    IReadOnlyList<AchievementAwardDto> Awards, ActiveAchievementDto? ActiveAchievement = null);

public record UpdateProfileRequest(string? AboutMe, string? GitHubUrl, string? PortfolioUrl);

/// <param name="AwardId">Null clears the choice: an award marked "active by default" takes over, if there is one.</param>
public record SetActiveAwardRequest(int? AwardId);

public record UpdateAvatarRequest(Guid MediaSessionId, MediaRenderRequest Media);

public record AchievementAwardDto(int Id, int AchievementId, string Name, string Description,
    MediaAssetDto? Image, DateTime GrantedAtUtc, Guid? IssuedByUserId, string IssuedByName,
    bool IsActive, bool CanRevoke);

/// <summary>
/// The one achievement a user wears. <see cref="AchievementId"/> is also the <c>data-fx</c> value
/// that the effects stylesheet (<c>api/achievements/effects.css</c>) is keyed by.
/// </summary>
public record ActiveAchievementDto(int AwardId, int AchievementId, string Name, MediaAssetDto? Image);

/// <summary>How a user looks in lists: avatar and the achievement whose effects apply.</summary>
public record UserLookDto(MediaAssetDto? Avatar, int? Fx);

/// <summary>Everything the editor page needs. <see cref="Media"/> is keyed by slot name in lower case.</summary>
public record AchievementEditDto(AchievementDto Achievement, IReadOnlyDictionary<string, MediaAssetDto> Media);

public record EffectPreviewRequest(AchievementEffects? Effects, string? CustomCss, int? AchievementId = null);

/// <summary>CSS for <c>[data-fx="preview"]</c>, or the reasons it cannot be built.</summary>
public record EffectPreviewDto(string Css, IReadOnlyList<EffectCssError> Errors);

public record SetAchievementMediaRequest(Guid MediaSessionId, MediaRenderRequest Media);

public record AchievementDto(int Id, string Name, string Description, MediaAssetDto? Image,
    bool IsArchived, int RecipientCount, DateTime CreatedAtUtc, bool ActiveByDefault = false,
    AchievementEffects? Effects = null, string? CustomCss = null);

public record AchievementPageDto(IReadOnlyList<AchievementDto> Items, int TotalCount, int Page, int PageSize);

public record AchievementRecipientDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin,
    int AwardId, DateTime GrantedAtUtc);

public record AchievementRecipientPageDto(IReadOnlyList<AchievementRecipientDto> Items, int TotalCount, int Page, int PageSize);

public record AchievementRecipientAccountDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin);

public record AchievementRecipientAccountPageDto(IReadOnlyList<AchievementRecipientAccountDto> Items,
    int TotalCount, int Page, int PageSize);

public record UpsertAchievementRequest(string? Name, string? Description, Guid? MediaSessionId = null,
    MediaRenderRequest? Media = null, bool RemoveImage = false, bool ActiveByDefault = false,
    AchievementEffects? Effects = null, string? CustomCss = null);

public record ArchiveAchievementRequest(bool Archived);
public record GrantAchievementRequest(Guid UserId);

public record ProfileAccountInfo(Guid UserId, string FirstName, string LastName, string? GroupName, bool IsAdmin)
{
    public string FullName => $"{LastName} {FirstName}";
}

public record MediaFileDto(byte[] Data, string ContentType);

public record SubjectProgressDto(int SubjectId, string Name, int AcceptedTasks, int TotalTasks, int Points, int MaxPoints);

public record AttendanceSummaryDto(int Present, int Absent, int Marked);

/// <summary>
/// Активность на профиле. Учебный прогресс (<see cref="Detailed"/>) видят только владелец
/// и преподаватель с доступом к группе; остальным — только активность на форуме.
/// </summary>
public record ProfileStatsDto(bool Detailed, IReadOnlyList<SubjectProgressDto> Subjects, AttendanceSummaryDto? Attendance,
    int ForumTopics, int ForumPosts, int SurveysCompleted);

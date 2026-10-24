namespace FunAndChecks.Application.Profiles;

public record MediaAssetDto(Guid Id, string ContentType, int Width, int Height,
    double DurationSeconds, bool IsAnimated, bool HasPoster);

public record UserProfileDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin,
    string AboutMe, string? GitHubUrl, string? PortfolioUrl, MediaAssetDto? Avatar,
    IReadOnlyList<AchievementAwardDto> Awards);

public record UpdateProfileRequest(string? AboutMe, string? GitHubUrl, string? PortfolioUrl,
    IReadOnlyList<int>? PinnedAwardIds);

public record UpdateAvatarRequest(Guid MediaSessionId, MediaRenderRequest Media);

public record AchievementAwardDto(int Id, int AchievementId, string Name, string Description,
    MediaAssetDto? Image, DateTime GrantedAtUtc, Guid? IssuedByUserId, string IssuedByName,
    int? PinPosition, bool CanRevoke);

public record AchievementDto(int Id, string Name, string Description, MediaAssetDto? Image,
    bool IsArchived, int RecipientCount, DateTime CreatedAtUtc);

public record AchievementPageDto(IReadOnlyList<AchievementDto> Items, int TotalCount, int Page, int PageSize);

public record AchievementRecipientDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin,
    int AwardId, DateTime GrantedAtUtc);

public record AchievementRecipientPageDto(IReadOnlyList<AchievementRecipientDto> Items, int TotalCount, int Page, int PageSize);

public record AchievementRecipientAccountDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin);

public record AchievementRecipientAccountPageDto(IReadOnlyList<AchievementRecipientAccountDto> Items,
    int TotalCount, int Page, int PageSize);

public record UpsertAchievementRequest(string? Name, string? Description, Guid? MediaSessionId = null,
    MediaRenderRequest? Media = null, bool RemoveImage = false);

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

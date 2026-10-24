namespace Frontend.Shared.Profiles;

public record MediaDto(Guid Id, string ContentType, int Width, int Height, double? DurationSeconds, bool IsAnimated, bool HasPoster);
public record UserProfileDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin, string? AboutMe,
    string? GitHubUrl, string? PortfolioUrl, MediaDto? Avatar, List<AchievementAwardDto> Awards);
public record AchievementAwardDto(int Id, int AchievementId, string Name, string Description, MediaDto? Image,
    DateTime GrantedAtUtc, Guid? IssuedByUserId, string IssuedByName, int? PinPosition, bool CanRevoke);
public record AchievementDefinitionDto(int Id, string Name, string Description, MediaDto? Image, bool IsArchived,
    int RecipientCount, DateTime CreatedAtUtc);
public record AchievementRecipientDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin, DateTime GrantedAtUtc);
public record AchievementUserDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin);
public record ProfilePageDto<T>(List<T> Items, int TotalCount, int Page, int PageSize);
public record SubjectProgressDto(int SubjectId, string Name, int AcceptedTasks, int TotalTasks, int Points, int MaxPoints);
public record AttendanceSummaryDto(int Present, int Absent, int Marked);
public record ProfileStatsDto(bool Detailed, List<SubjectProgressDto> Subjects, AttendanceSummaryDto? Attendance, int ForumTopics, int ForumPosts, int SurveysCompleted);
public record UpdateProfileRequest(string? AboutMe, string? GitHubUrl, string? PortfolioUrl, List<int> PinnedAwardIds);
public record SaveAchievementRequest(string Name, string Description, Guid? MediaSessionId, MediaRenderRequest? Media, bool RemoveImage = false);

// Upload processing contracts are deliberately separate from persisted media.
public record MediaSessionDto(Guid Id, int Width, int Height, double DurationSeconds, string Kind, bool CanAnimate, DateTime ExpiresAtUtc);
public record MediaRenderRequest(double X, double Y, double Width, double Height, double StartSeconds, double EndSeconds,
    double FrameSeconds, string Mode);
public record MediaSelection(Guid SessionId, MediaRenderRequest Media);

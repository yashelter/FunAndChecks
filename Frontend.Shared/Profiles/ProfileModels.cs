namespace Frontend.Shared.Profiles;

public record MediaDto(Guid Id, string ContentType, int Width, int Height, double? DurationSeconds, bool IsAnimated, bool HasPoster);
/// <summary>Достижение, которое пользователь «носит». <c>AchievementId</c> — это и значение <c>data-fx</c> для таблицы стилей эффектов.</summary>
public record ActiveAchievementDto(int AwardId, int AchievementId, string Name, MediaDto? Image);
/// <summary>Вид пользователя в списках: аватар и достижение, чьи эффекты к нему применяются.</summary>
public record UserLookDto(MediaDto? Avatar, int? Fx);
public record UserProfileDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin, string? AboutMe,
    string? GitHubUrl, string? PortfolioUrl, MediaDto? Avatar, List<AchievementAwardDto> Awards, ActiveAchievementDto? ActiveAchievement = null);
public record AchievementAwardDto(int Id, int AchievementId, string Name, string Description, MediaDto? Image,
    DateTime GrantedAtUtc, Guid? IssuedByUserId, string IssuedByName, bool IsActive, bool CanRevoke);
public record AchievementDefinitionDto(int Id, string Name, string Description, MediaDto? Image, bool IsArchived,
    int RecipientCount, DateTime CreatedAtUtc, bool ActiveByDefault = false, AchievementEffects? Effects = null, string? CustomCss = null);
/// <summary>Достижение для страницы редактора; <c>Media</c> — картинки эффектов по имени слота (frame, pattern, banner, decor, badge).</summary>
public record AchievementEditDto(AchievementDefinitionDto Achievement, Dictionary<string, MediaDto> Media);
public record EffectPreviewRequest(AchievementEffects? Effects, string? CustomCss, int? AchievementId);
public record EffectCssError(int Line, string Message);
public record EffectPreviewDto(string Css, List<EffectCssError> Errors);
public record AchievementRecipientDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin, DateTime GrantedAtUtc);
public record AchievementUserDto(Guid UserId, string FullName, string? GroupName, bool IsAdmin);
public record ProfilePageDto<T>(List<T> Items, int TotalCount, int Page, int PageSize);
public record SubjectProgressDto(int SubjectId, string Name, int AcceptedTasks, int TotalTasks, int Points, int MaxPoints);
public record AttendanceSummaryDto(int Present, int Absent, int Marked);
public record ProfileStatsDto(bool Detailed, List<SubjectProgressDto> Subjects, AttendanceSummaryDto? Attendance, int ForumTopics, int ForumPosts, int SurveysCompleted);
public record UpdateProfileRequest(string? AboutMe, string? GitHubUrl, string? PortfolioUrl);
public record SaveAchievementRequest(string Name, string Description, Guid? MediaSessionId, MediaRenderRequest? Media, bool RemoveImage = false,
    bool ActiveByDefault = false, AchievementEffects? Effects = null, string? CustomCss = null);

// Настройки конструктора эффектов. Классы, а не записи: редактор привязывает поля напрямую.
// Пустой цвет — «не задано»; сервер проверяет значения и собирает из них CSS.
public sealed class AchievementEffects
{
    public AvatarEffect Avatar { get; set; } = new();
    public NameEffect Name { get; set; } = new();
    public RowEffect Row { get; set; } = new();
    public ProfileEffect Profile { get; set; } = new();
    public SiteEffect Site { get; set; } = new();
}
public sealed class AvatarEffect
{
    public bool Frame { get; set; }
    public string? RingColor { get; set; }
    public int RingWidth { get; set; } = 2;
    public string? GlowColor { get; set; }
    public string Animation { get; set; } = "none";
}
public sealed class NameEffect
{
    public string? Color { get; set; }
    public string? Color2 { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string? GlowColor { get; set; }
    public string Animation { get; set; } = "none";
}
public sealed class RowEffect
{
    public string? Color { get; set; }
    public string? Color2 { get; set; }
    public int Opacity { get; set; } = 18;
    public bool Pattern { get; set; }
    public string? BarColor { get; set; }
}
public sealed class ProfileEffect
{
    public bool Banner { get; set; }
    public string? Color { get; set; }
    public string? Color2 { get; set; }
    public string? AccentColor { get; set; }
    public string? GlowColor { get; set; }
}
public sealed class SiteEffect
{
    public int Particles { get; set; }
    public string Motion { get; set; } = "fall";
    public int Opacity { get; set; } = 60;
    public int Size { get; set; } = 28;
}

// Upload processing contracts are deliberately separate from persisted media.
public record MediaSessionDto(Guid Id, int Width, int Height, double DurationSeconds, string Kind, bool CanAnimate, DateTime ExpiresAtUtc);
public record MediaRenderRequest(double X, double Y, double Width, double Height, double StartSeconds, double EndSeconds,
    double FrameSeconds, string Mode);
public record MediaSelection(Guid SessionId, MediaRenderRequest Media);

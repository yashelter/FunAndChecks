namespace FunAndChecks.Common;

public static class AuthorizationPolicies
{
    public const string SuperAdmin = "RequireSuperAdminRole";
}

public static class RateLimitPolicies
{
    /// <summary>Лимит на эндпоинты аутентификации (защита от перебора).</summary>
    public const string Auth = "auth";

    /// <summary>Лимит на записи форума и загрузку файлов — по пользователю.</summary>
    public const string ForumWrite = "forum-write";

    /// <summary>
    /// Лимит на операции редактора медиа, запускающие FFmpeg (загрузка исходника, кадр), — по пользователю.
    /// Число процессов ограничено и так, а этот лимит не даёт одному пользователю занять очередь обработки.
    /// </summary>
    public const string MediaProcessing = "media-processing";
}

namespace FunAndChecks.Common;

public static class AuthorizationPolicies
{
    public const string SuperAdmin = "RequireSuperAdminRole";
}

public static class RateLimitPolicies
{
    /// <summary>Лимит на эндпоинты аутентификации (защита от перебора).</summary>
    public const string Auth = "auth";
}

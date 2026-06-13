namespace FunAndChecks.Domain.Constants;

/// <summary>
/// Имена ролей системы. Хранятся в Identity (Infrastructure),
/// но имена нужны на всех слоях для авторизации.
/// </summary>
public static class Roles
{
    public const string Student = "User";
    public const string Admin = "Admin";
    public const string SuperAdmin = "SuperAdmin";
}

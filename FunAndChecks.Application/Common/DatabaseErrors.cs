namespace FunAndChecks.Application.Common;

public static class DatabaseErrors
{
    /// <summary>
    /// Нарушение уникального ключа: Postgres 23505 или SQLite 2067/1555. Через отражение,
    /// чтобы слой Application не зависел от провайдера.
    /// </summary>
    public static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().GetProperty("SqlState")?.GetValue(current) as string == "23505") return true;
            if (current.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(current) is int code && code is 2067 or 1555) return true;
        }
        return false;
    }
}

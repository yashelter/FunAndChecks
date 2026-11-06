using FunAndChecks.Application.Backups;

namespace FunAndChecks.Middleware;

/// <summary>
/// Пока идёт восстановление БД из бэкапа, API и хабы отвечают 503 — запросы не должны ни читать
/// полузаменённую БД, ни писать в неё. Доступен только опрос статуса восстановления.
/// </summary>
public sealed class MaintenanceModeMiddleware(RequestDelegate next, MaintenanceModeState maintenance)
{
    internal const string RestoreStatusPath = "/api/admin/backup/restore/status";

    public async Task InvokeAsync(HttpContext context)
    {
        if (maintenance.IsActive && IsBlocked(context.Request))
        {
            context.Response.Headers.RetryAfter = "10";
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status503ServiceUnavailable,
                "The service is temporarily unavailable while the database is being restored from a backup.",
                "maintenance.restore_in_progress");
            return;
        }

        // Восстановление после входа в техрежим ждёт, пока завершатся уже начатые API-запросы,
        // чтобы они не писали в базу во время страховочного бэкапа и замены. Хабы (долгие соединения)
        // и опрос статуса не считаются.
        if (!IsCounted(context.Request))
        {
            await next(context);
            return;
        }
        maintenance.RequestStarted();
        try
        {
            await next(context);
        }
        finally
        {
            maintenance.RequestFinished();
        }
    }

    internal static bool IsCounted(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !(HttpMethods.IsGet(request.Method) && request.Path.Equals(RestoreStatusPath, StringComparison.OrdinalIgnoreCase));

    internal static bool IsBlocked(HttpRequest request)
    {
        var path = request.Path;
        if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/apiHub"))
            return false;
        return !(HttpMethods.IsGet(request.Method) && path.Equals(RestoreStatusPath, StringComparison.OrdinalIgnoreCase));
    }
}

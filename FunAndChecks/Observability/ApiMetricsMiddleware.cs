using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Routing;

namespace FunAndChecks.Observability;

public sealed class ApiMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ApiMetrics metrics)
    {
        // Boundary matching excludes SignalR /apiHub and all monitoring/static traffic.
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/api/monitoring"))
        {
            await next(context);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var failedStatus = 0;
        metrics.InFlight.Inc();
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            failedStatus = 499;
            throw;
        }
        catch
        {
            failedStatus = 500;
            throw;
        }
        finally
        {
            var route = context.GetEndpoint() is RouteEndpoint endpoint &&
                        endpoint.RoutePattern.RawText is { } pattern &&
                        pattern.TrimStart('/').StartsWith("api/", StringComparison.OrdinalIgnoreCase)
                ? "/" + pattern.TrimStart('/') : "unmatched";
            var method = context.Request.Method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"
                ? context.Request.Method : "OTHER";
            var status = (failedStatus != 0 ? failedStatus : context.Response.StatusCode).ToString(CultureInfo.InvariantCulture);
            metrics.Requests.WithLabels(route, method, status).Inc();
            metrics.Duration.WithLabels(route, method, status).Observe(Stopwatch.GetElapsedTime(started).TotalSeconds);
            metrics.InFlight.Dec();
        }
    }
}

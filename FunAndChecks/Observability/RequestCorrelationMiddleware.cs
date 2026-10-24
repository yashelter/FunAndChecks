using Serilog.Context;

namespace FunAndChecks.Observability;

public sealed class RequestCorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // The server's identifier cannot be replaced by an incoming header.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
            await next(context);
    }
}

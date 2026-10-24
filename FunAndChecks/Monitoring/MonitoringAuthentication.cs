using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FunAndChecks.Monitoring;

public static class MonitoringAuthentication
{
    public const string Scheme = "MonitoringCookie";
    public const string CookieName = "__Secure-fc-monitoring";
    public const string ProxyKeyHeader = "X-Monitoring-Proxy-Key";
    public const string OriginalUriHeader = "X-Monitoring-Original-Uri";
    public const string UserHeader = "X-Monitoring-User";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static IServiceCollection AddMonitoringAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .Validate(options => !options.Enabled || options.ProxyKey is { Length: >= 32 },
                "Monitoring:ProxyKey must contain at least 32 characters when monitoring is enabled.")
            .Validate(options => !options.Enabled || options.ScrapeKey is { Length: >= 32 },
                "Monitoring:ScrapeKey must contain at least 32 characters when monitoring is enabled.")
            .ValidateOnStart();

        // Calling AddAuthentication without defaults keeps the site's Bearer scheme.
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.Path = "/metrics";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = SessionLifetime;
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        return services;
    }
}

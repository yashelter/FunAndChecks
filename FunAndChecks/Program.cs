using System.Text;
using System.Threading.RateLimiting;
using System.Globalization;
using System.Security.Claims;
using FunAndChecks.Application;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Hubs;
using FunAndChecks.Infrastructure;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Infrastructure.Persistence.Seeding;
using FunAndChecks.Middleware;
using FunAndChecks.Monitoring;
using FunAndChecks.Observability;
using FunAndChecks.OpenApi;
using FunAndChecks.Realtime;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Prometheus;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(LogEventLevel.Warning)
    .CreateBootstrapLogger();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Секреты (ConnectionStrings, Jwt:Key, Smtp, InitialAdmins) — реальный файл не в репозитории,
// шаблон рядом: secrets.template.json. Можно заменить user-secrets/переменными окружения.
builder.Configuration.AddJsonFile(
    "secrets.json",
    optional: true,
    reloadOnChange: true);

// Слои приложения
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.Configure<AdminAuditOptions>(builder.Configuration.GetSection("AdminAudit"));
if (builder.Environment.IsEnvironment("Testing"))
    builder.Services.PostConfigure<AdminAuditOptions>(options => options.Enabled = false);
builder.Services.AddSingleton<FileAdminAuditWriter>();
builder.Services.AddSingleton<IAdminAuditWriter>(services => services.GetRequiredService<FileAdminAuditWriter>());
builder.Services.AddHostedService(services => services.GetRequiredService<FileAdminAuditWriter>());
builder.Services.AddScoped<AdminAuditContext>();

// SignalR-нотификаторы — реализация прикладных интерфейсов на уровне Presentation
builder.Services.AddSignalR();
builder.Services.AddScoped<IQueueNotifier, QueueNotifier>();
builder.Services.AddScoped<IResultsNotifier, ResultsNotifier>();
builder.Services.AddScoped<IUserNotifier, UserNotifier>();
builder.Services.AddScoped<IForumNotifier, ForumNotifier>();

// Настройки JWT из appsettings (секция "Jwt")
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                 ?? throw new InvalidOperationException("Jwt configuration section is missing.");
if (string.IsNullOrEmpty(jwtOptions.Key))
    throw new InvalidOperationException("Jwt:Key is not configured.");

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        };

        // SignalR передаёт токен в query-string — достаём его для хабов.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/apiHub"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                // Во время восстановления БД заблокирована транзакцией restore; пропускает только опрос
                // статуса (MaintenanceModeMiddleware), поэтому достаточно подписи и ролей из токена.
                if (context.HttpContext.RequestServices.GetRequiredService<MaintenanceModeState>().IsActive)
                    return;
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                if (!Guid.TryParse(id, out var userId) ||
                    !await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, context.HttpContext.RequestAborted))
                    context.Fail("The account no longer exists.");
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                // RFC 9110 §11.6.1: 401 обязан сопровождаться WWW-Authenticate.
                context.HttpContext.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
                    ? "Bearer"
                    : "Bearer error=\"invalid_token\"";
                await ApiProblemDetails.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "Authentication is required.", "auth.unauthorized");
            },
            OnForbidden = context => ApiProblemDetails.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                "You do not have permission to perform this action.", "access.forbidden"),
        };
    });
builder.Services.AddMonitoringAuthentication(builder.Configuration);
builder.Services.AddAuthentication()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, MetricsScrapeHandler>(MetricsScrapeHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.SuperAdmin,
        policy => policy.RequireRole(Roles.SuperAdmin));
    options.AddPolicy(MetricsScrapeHandler.SchemeName, policy =>
        policy.AddAuthenticationSchemes(MetricsScrapeHandler.SchemeName).RequireAuthenticatedUser());
});

// Защита эндпоинтов аутентификации от перебора: лимит по IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await ApiProblemDetails.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests,
            "Too many requests. Try again later.", "rate_limit.exceeded");
    };
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    // Форум и загрузки: по пользователю, чтобы общий NAT аудитории не делил один бакет.
    options.AddPolicy(RateLimitPolicies.ForumWrite, httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }));
});

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ApiProblemDetails.ValidationResult);
builder.Services.AddLocalization();

// OpenAPI-документ (отдаётся на /openapi/v1.json), просматривается через Scalar.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "PostgreSQL",
        failureStatus: HealthStatus.Unhealthy);

// CORS: в проде — только origins из конфигурации (Cors:AllowedOrigins),
// в Development — любой origin (чтобы фронт можно было запускать локально
// из IDE отдельно от бэкенда, а не только через общий compose).
const string corsPolicy = "AppCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();

        if (builder.Environment.IsDevelopment())
            policy.SetIsOriginAllowed(_ => true);
        else
            policy.WithOrigins(allowedOrigins);
    });
});

var app = builder.Build();

// Единственный входящий прокси — Caddy в docker-сети. Дефолтные KnownProxies/KnownNetworks
// (только loopback) заставляют игнорировать X-Forwarded-For от Caddy, и RemoteIpAddress
// для всех клиентов совпадает с адресом прокси — per-IP rate-limit авторизации
// (20 req/min) схлопывается в один общий бакет. Доверяем всей сети за портом.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);
app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseMiddleware<ApiMetricsMiddleware>();

var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("ru-RU") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
});

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} " +
        "in {Elapsed:0.0000} ms | UserId={UserId} | IP={ClientIp}";

    options.EnrichDiagnosticContext = (diagnosticContext, context) =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? context.User.FindFirstValue("sub")
              ?? "unknown"
            : "anonymous";

        diagnosticContext.Set("ClientIp", ip);
        diagnosticContext.Set("UserId", userId);
        diagnosticContext.Set("RequestId", context.TraceIdentifier);
    };
});
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePages(async statusContext =>
{
    var http = statusContext.HttpContext;
    if (http.Request.Path.StartsWithSegments("/api"))
    {
        var status = http.Response.StatusCode;
        var code = ApiProblemDetails.CodeForStatus(status);
        await ApiProblemDetails.WriteAsync(http, status, ApiProblemDetails.DetailForStatus(status), code);
    }
});

// OpenAPI + Scalar UI доступны всегда (документация по API на /scalar).
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("FunAndChecks API")
        .WithTheme(ScalarTheme.Mars)
        .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl);
});

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

app.UseHttpsRedirection();

// Имена файлов интерфейса без отпечатков (index.html, css, js, _content/*). Без Cache-Control браузер кеширует их
// эвристически и после обновления смешивает новый код со старыми стилями и скриптами: «ломаются стили»,
// страница не догружается. no-cache = «можно хранить, но перед использованием спросить» (ETag -> дешёвый 304).
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (!path.StartsWithSegments("/api") && !path.StartsWithSegments("/apiHub") && !path.StartsWithSegments("/internal"))
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.CacheControl))
                context.Response.Headers.CacheControl = "no-cache";
            return Task.CompletedTask;
        });
    await next(context);
});

// Хостинг Blazor WASM (AdminUI) — статические файлы и SPA-fallback.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

// Endpoint routing does not produce a status code for an unknown path. Handle it
// before the SPA fallback, while keeping the framework's proper 405 handling.
app.Use(async (context, next) =>
{
    var endpoint = context.GetEndpoint();
    var isSpaFallback = endpoint?.Metadata.Any(metadata => metadata.GetType().Name == "FallbackMetadata") == true;
    if ((context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/internal")) && (endpoint is null || isSpaFallback))
    {
        await ApiProblemDetails.WriteAsync(context, StatusCodes.Status404NotFound,
            ApiProblemDetails.DetailForStatus(StatusCodes.Status404NotFound),
            ApiProblemDetails.CodeForStatus(StatusCodes.Status404NotFound));
        return;
    }

    await next(context);
});

app.UseCors(corsPolicy);

// Лимитер после аутентификации: политика форума делит бакеты по пользователю.
app.UseAuthentication();
app.UseMiddleware<MaintenanceModeMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<AdminAuditMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics("/internal/metrics", app.Services.GetRequiredService<ApiMetrics>().Registry)
    .RequireAuthorization(MetricsScrapeHandler.SchemeName);

app.MapHub<QueueHub>("/apiHub/queueHub");
app.MapHub<ResultsHub>("/apiHub/resultsHub");
app.MapHub<NotificationsHub>("/apiHub/notificationsHub");
app.MapHub<ForumHub>("/apiHub/forumHub");

// Миграции и сидинг при старте (в тестовой среде БД готовит сам тест-хост).
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        if (context.Database.GetPendingMigrations().Any())
        {
            logger.LogInformation("Applying database migrations...");
            context.Database.Migrate();
            logger.LogInformation("Database migrations applied successfully.");
        }

        var seeder = services.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync();
        logger.LogInformation("Database seeding completed.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Database migration or seeding failed; application startup is aborted.");
        throw;
    }
}

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

// Список ассетов экрана загрузки (wwwroot/loading) — фронт выбирает случайный.
// Достаточно просто положить файлы (gif/mp4/webm/png/...) в папку.
app.MapGet("/api/loading-assets", () =>
{
    var contents = app.Environment.WebRootFileProvider.GetDirectoryContents("loading");
    var files = contents
        .Where(f => !f.IsDirectory)
        .Where(f => new[] { ".gif", ".mp4", ".webm", ".ogg", ".png", ".jpg", ".jpeg", ".webp", ".svg", ".avif" }
            .Contains(Path.GetExtension(f.Name), StringComparer.OrdinalIgnoreCase))
        .Select(f => $"loading/{f.Name}")
        .ToList();
    return Results.Ok(files);
});

app.MapFallbackToFile("index.html");

try
{
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception");
}
finally
{
    Log.Information("Shut down complete");
    Log.CloseAndFlush();
}

/// <summary>
/// Для интеграционных тестов
/// </summary>
public partial class Program;

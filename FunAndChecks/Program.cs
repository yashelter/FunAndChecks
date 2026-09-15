using System.Text;
using System.Threading.RateLimiting;
using System.Globalization;
using FunAndChecks.Application;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Hubs;
using FunAndChecks.Infrastructure;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Infrastructure.Persistence.Seeding;
using FunAndChecks.Middleware;
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

// SignalR-нотификаторы — реализация прикладных интерфейсов на уровне Presentation
builder.Services.AddSignalR();
builder.Services.AddScoped<IQueueNotifier, QueueNotifier>();
builder.Services.AddScoped<IResultsNotifier, ResultsNotifier>();

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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.SuperAdmin,
        policy => policy.RequireRole(Roles.SuperAdmin));
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
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
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
// (10 req/min) схлопывается в один общий бакет. Доверяем всей сети за портом.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

var supportedCultures = new[] { new CultureInfo("en-US"), new CultureInfo("ru-RU") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
});

app.UseSerilogRequestLogging();
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
    if (context.Request.Path.StartsWithSegments("/api") && (endpoint is null || isSpaFallback))
    {
        await ApiProblemDetails.WriteAsync(context, StatusCodes.Status404NotFound,
            ApiProblemDetails.DetailForStatus(StatusCodes.Status404NotFound),
            ApiProblemDetails.CodeForStatus(StatusCodes.Status404NotFound));
        return;
    }

    await next(context);
});

app.UseCors(corsPolicy);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<QueueHub>("/apiHub/queueHub");
app.MapHub<ResultsHub>("/apiHub/resultsHub");

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

using System.Text;
using System.Threading.RateLimiting;
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

builder.Services.AddControllers();

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

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

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
        logger.LogError(ex, "An error occurred during database migration or seeding.");
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

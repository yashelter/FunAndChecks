using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Infrastructure.Backup;
using FunAndChecks.Infrastructure.Caching;
using FunAndChecks.Infrastructure.Email;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Infrastructure.Persistence.Seeding;
using FunAndChecks.Infrastructure.Workers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FunAndChecks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;

                // Логин — это email; он должен быть уникальным и обязательно подтверждённым.
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;

                // Защита от перебора пароля.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            // TOTP-провайдер «Email» даёт короткие 6-значные коды для подтверждения почты
            // и сброса пароля — без зависимости от DataProtection.
            .AddTokenProvider<EmailTokenProvider<ApplicationUser>>(TokenOptions.DefaultEmailProvider);

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));

        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IDatabaseBackupService, PgDumpBackupService>();

        services.AddSingleton<IResultsCacheService, ResultsCacheService>();
        services.AddSingleton<IEmailThrottle, EmailThrottle>();

        // Фоновая очистка неподтверждённых аккаунтов.
        services.AddHostedService<UnconfirmedAccountCleanupService>();

        services.AddScoped<DataSeeder>();

        return services;
    }
}

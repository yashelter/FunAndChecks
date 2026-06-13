using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FunAndChecks.Tests.Integration;

/// <summary>
/// Поднимает реальное API на in-memory SQLite, подменяет отправку писем
/// и задаёт тестовую конфигурацию JWT.
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CapturingEmailSender Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting попадает в host-конфигурацию до того, как Program прочитает Jwt:Key.
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("Jwt:Issuer", "TestIssuer");
        builder.UseSetting("Jwt:Audience", "TestAudience");
        // HS512 требует ключ не короче 64 байт.
        builder.UseSetting("Jwt:Key", "super-secret-test-key-of-sufficient-length-0123456789-abcdef-ABCDEF-xyz");
        builder.UseSetting("Jwt:TokenLifetimeDays", "1");

        builder.ConfigureTestServices(services =>
        {
            _connection.Open();

            // Снимаем регистрацию EF/Npgsql целиком, иначе в контейнере окажутся два провайдера.
            var efDescriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(ApplicationDbContext) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration")) ||
                    (d.ServiceType.FullName?.Contains("Npgsql") ?? false))
                .ToList();
            foreach (var d in efDescriptors)
                services.Remove(d);

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        // Сидинг после полной сборки хоста — иначе промежуточный ServiceProvider «замораживает» логгер.
        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<ApplicationDbContext>().Database.EnsureCreated();

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in new[] { Roles.Student, Roles.Admin, Roles.SuperAdmin })
        {
            if (!roleManager.RoleExistsAsync(role).GetAwaiter().GetResult())
                roleManager.CreateAsync(new ApplicationRole(role)).GetAwaiter().GetResult();
        }

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

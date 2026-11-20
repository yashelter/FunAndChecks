using FluentAssertions;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class DataSeederTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task SeedAsync_BootstrapsOnlyOnceAfterRoleRevokedAndEmailChangedAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        UserManager<ApplicationUser> users = services.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<ApplicationRole> roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
        ApplicationDbContext db = services.GetRequiredService<ApplicationDbContext>();
        const string ORIGINAL_EMAIL = "initial@example.com";
        const string NEW_EMAIL = "renamed@example.com";
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InitialAdmins:0:FirstName"] = "First",
            ["InitialAdmins:0:LastName"] = "Admin",
            ["InitialAdmins:0:Email"] = ORIGINAL_EMAIL,
            ["InitialAdmins:0:Password"] = "Password123!",
            ["InitialAdmins:0:IsSuperAdmin"] = "true",
        }).Build();
        DataSeeder seeder = new(users, roles, db, configuration, NullLogger<DataSeeder>.Instance);
        await seeder.SeedAsync();

        ApplicationUser? original = await users.FindByEmailAsync(ORIGINAL_EMAIL);
        original.Should().NotBeNull();
        (await users.GetRolesAsync(original!)).Should().BeEquivalentTo([Roles.Admin, Roles.SuperAdmin]);
        (await users.RemoveFromRoleAsync(original!, Roles.SuperAdmin)).Succeeded.Should().BeTrue();
        (await users.SetEmailAsync(original!, NEW_EMAIL)).Succeeded.Should().BeTrue();
        (await users.SetUserNameAsync(original!, NEW_EMAIL)).Succeeded.Should().BeTrue();

        await seeder.SeedAsync();

        (await users.FindByEmailAsync(ORIGINAL_EMAIL)).Should().BeNull();
        (await users.GetRolesAsync(original!)).Should().Equal(Roles.Admin);
        (await db.Admins.CountAsync()).Should().Be(1);
        (await db.AdminBootstrapStates.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SeedAsync_LeavesExistingAccountUntouchedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var db = services.GetRequiredService<ApplicationDbContext>();
        var id = Guid.NewGuid();
        var email = $"partial-seed-{id:N}@example.com";
        var account = new ApplicationUser
        {
            Id = id,
            UserName = email,
            Email = email,
            EmailConfirmed = false,
        };
        (await users.CreateAsync(account, "Password123!")).Succeeded.Should().BeTrue();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InitialAdmins:0:FirstName"] = "Repaired",
            ["InitialAdmins:0:LastName"] = "Admin",
            ["InitialAdmins:0:Email"] = email,
            ["InitialAdmins:0:Password"] = "Password123!",
            ["InitialAdmins:0:Color"] = "#112233",
            ["InitialAdmins:0:Letter"] = "RA",
            ["InitialAdmins:0:IsSuperAdmin"] = "true",
        }).Build();
        var seeder = new DataSeeder(users, roles, db, configuration, NullLogger<DataSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        ApplicationUser? existing = await users.FindByEmailAsync(email);
        existing!.EmailConfirmed.Should().BeFalse();
        (await users.GetRolesAsync(existing)).Should().BeEmpty();
        (await db.Admins.AnyAsync(admin => admin.Id == id)).Should().BeFalse();
        (await db.AdminBootstrapStates.CountAsync()).Should().Be(1);
    }
}

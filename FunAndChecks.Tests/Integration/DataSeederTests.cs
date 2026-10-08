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
public class DataSeederTests(TestWebAppFactory factory)
{
    [Fact]
    public async Task SeedAsync_RepairsExistingAccountWithoutRolesOrProfile_AndIsIdempotent()
    {
        using var scope = factory.Services.CreateScope();
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

        var repaired = await users.FindByEmailAsync(email);
        repaired!.EmailConfirmed.Should().BeTrue();
        (await users.GetRolesAsync(repaired)).Should().Contain([Roles.Admin, Roles.SuperAdmin]);
        var profile = await db.Admins.AsNoTracking().SingleAsync(admin => admin.Id == id);
        profile.FirstName.Should().Be("Repaired");
        profile.LastName.Should().Be("Admin");
        profile.Color.Should().Be("#112233");
        profile.Letter.Should().Be("RA");
    }
}

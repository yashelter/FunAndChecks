using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Infrastructure.Persistence.Seeding;

public class DataSeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext dbContext,
    IConfiguration configuration,
    ILogger<DataSeeder> logger)
{
    public async Task SeedAsync()
    {
        await SeedRolesAsync();
        await SeedAdminsAsync();
    }

    private async Task SeedRolesAsync()
    {
        string[] roles = [Roles.Student, Roles.Admin, Roles.SuperAdmin];

        foreach (var role in roles)
        {
            if (await roleManager.FindByNameAsync(role) == null)
            {
                await roleManager.CreateAsync(new ApplicationRole(role));
                logger.LogInformation("Role '{Role}' created.", role);
            }
        }
    }

    private async Task SeedAdminsAsync()
    {
        var adminsToCreate = configuration.GetSection("InitialAdmins").Get<List<AdminSeedModel>>();

        if (adminsToCreate == null || adminsToCreate.Count == 0)
        {
            logger.LogWarning("No initial admins found in configuration. Skipping admin seeding.");
            return;
        }

        foreach (var model in adminsToCreate)
        {
            if (await userManager.FindByEmailAsync(model.Email) != null)
            {
                logger.LogInformation("Admin with email {Email} already exists. Skipping.", model.Email);
                continue;
            }

            var account = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true,
            };

            var result = await userManager.CreateAsync(account, model.Password);
            if (!result.Succeeded)
            {
                logger.LogError("Failed to create admin account {Email}. Errors: {Errors}",
                    model.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                continue;
            }

            var roles = model.IsSuperAdmin ? new[] { Roles.Admin, Roles.SuperAdmin } : [Roles.Admin];
            await userManager.AddToRolesAsync(account, roles);

            dbContext.Admins.Add(new Admin
            {
                Id = account.Id,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Color = model.Color,
                Letter = model.Letter,
            });
            await dbContext.SaveChangesAsync();

            logger.LogInformation("Admin {Email} created and assigned roles: {Roles}.",
                model.Email, string.Join(", ", roles));
        }
    }
}

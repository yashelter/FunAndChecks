using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Infrastructure.Persistence.Seeding;

public class DataSeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    ApplicationDbContext dbContext,
    IConfiguration configuration,
    ILogger<DataSeeder> logger)
{
    private const int BOOTSTRAP_ID = 1;

    public async Task SeedAsync(bool initializeAdmins = true, CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync();
        if (!initializeAdmins)
        {
            return;
        }

        await dbContext.ExecuteSerializableAsync(async token =>
        {
            if (await dbContext.AdminBootstrapStates.AnyAsync(token))
            {
                return;
            }

            if (!await dbContext.Users.AnyAsync(token))
            {
                bool seeded = await SeedAdminsAsync(token);
                if (!seeded)
                {
                    return;
                }
            }

            dbContext.AdminBootstrapStates.Add(new AdminBootstrapState { Id = BOOTSTRAP_ID });
            await dbContext.SaveChangesAsync(token);
        }, cancellationToken);
    }

    private async Task SeedRolesAsync()
    {
        string[] roles = [Roles.Student, Roles.Admin, Roles.SuperAdmin];

        foreach (var role in roles)
        {
            if (await roleManager.FindByNameAsync(role) == null)
            {
                EnsureSucceeded(await roleManager.CreateAsync(new ApplicationRole(role)), $"create role '{role}'");
                logger.LogInformation("Role '{Role}' created.", role);
            }
        }
    }

    private async Task<bool> SeedAdminsAsync(CancellationToken cancellationToken)
    {
        var adminsToCreate = configuration.GetSection("InitialAdmins").Get<List<AdminSeedModel>>();

        if (adminsToCreate == null || adminsToCreate.Count == 0)
        {
            logger.LogWarning("No initial admins found in configuration. Skipping admin seeding.");
            return false;
        }

        foreach (AdminSeedModel model in adminsToCreate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplicationUser account = new()
            {
                Id = Guid.NewGuid(),
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true,
            };
            EnsureSucceeded(await userManager.CreateAsync(account, model.Password), "create initial admin");
            string[] requiredRoles = model.IsSuperAdmin ? [Roles.Admin, Roles.SuperAdmin] : [Roles.Admin];
            EnsureSucceeded(await userManager.AddToRolesAsync(account, requiredRoles), "assign initial admin roles");

            dbContext.Admins.Add(new Admin
            {
                Id = account.Id,
                FirstName = model.FirstName,
                LastName = model.LastName,
                Color = model.Color,
                Letter = model.Letter,
            });
        }

        return true;
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to {operation}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}

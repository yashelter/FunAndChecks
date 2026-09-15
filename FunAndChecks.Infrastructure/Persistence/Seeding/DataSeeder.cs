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
                EnsureSucceeded(await roleManager.CreateAsync(new ApplicationRole(role)), $"create role '{role}'");
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
            await dbContext.ExecuteSerializableAsync(async cancellationToken =>
            {
                var account = await userManager.FindByEmailAsync(model.Email);
                if (account is null)
                {
                    account = new ApplicationUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = model.Email,
                        Email = model.Email,
                        EmailConfirmed = true,
                    };
                    EnsureSucceeded(await userManager.CreateAsync(account, model.Password), $"create admin '{model.Email}'");
                }
                else
                {
                    if (await dbContext.Students.AnyAsync(s => s.Id == account.Id, cancellationToken))
                        throw new InvalidOperationException($"Initial admin email '{model.Email}' belongs to a student account.");

                    account.EmailConfirmed = true;
                    EnsureSucceeded(await userManager.UpdateAsync(account), $"update admin '{model.Email}'");
                }

                var requiredRoles = model.IsSuperAdmin ? new[] { Roles.Admin, Roles.SuperAdmin } : [Roles.Admin];
                var currentRoles = await userManager.GetRolesAsync(account);
                var missingRoles = requiredRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
                if (missingRoles.Length > 0)
                    EnsureSucceeded(await userManager.AddToRolesAsync(account, missingRoles), $"assign roles to '{model.Email}'");

                var profile = await dbContext.Admins.FindAsync([account.Id], cancellationToken);
                if (profile is null)
                {
                    profile = new Admin
                    {
                        Id = account.Id,
                        FirstName = model.FirstName,
                        LastName = model.LastName,
                    };
                    dbContext.Admins.Add(profile);
                }

                profile.FirstName = model.FirstName;
                profile.LastName = model.LastName;
                profile.Color = model.Color;
                profile.Letter = model.Letter;
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Admin {Email} is consistent and assigned roles: {Roles}.",
                    model.Email, string.Join(", ", requiredRoles));
            });
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to {operation}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }
}

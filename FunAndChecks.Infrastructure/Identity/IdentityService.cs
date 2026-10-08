using FunAndChecks.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Constants;

namespace FunAndChecks.Infrastructure.Identity;

public class IdentityService(
    UserManager<ApplicationUser> userManager,
    IRefreshTokenService refreshTokenService,
    ApplicationDbContext dbContext) : IIdentityService
{
    // Назначения токенов (провайдер «Email» — EmailCodeTokenProvider — даёт короткие
    // 6-значные коды со сроком жизни 10 минут).
    private const string EmailConfirmationPurpose = "EmailConfirmation";
    private const string PasswordResetPurpose = "ResetPassword";

    public async Task<AccountResult> CreateAccountAsync(
        Guid id,
        string email,
        string password,
        IEnumerable<string> roles,
        bool emailConfirmed,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
            return AccountResult.Failure(createResult.Errors.Select(e => e.Description));

        var rolesResult = await userManager.AddToRolesAsync(user, roles);
        if (!rolesResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return AccountResult.Failure(rolesResult.Errors.Select(e => e.Description));
        }

        return AccountResult.Success();
    }

    public async Task DeleteAccountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user != null)
            await userManager.DeleteAsync(user);
    }

    public async Task<UserAccountPageDto> GetNonAdminAccountsAsync(Guid actingAdminId, string? query,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await EnsureSuperAdminAsync(actingAdminId, cancellationToken);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new FluentValidation.ValidationException("Page must be positive and page size must be between 1 and 100.");

        var accounts = dbContext.Users.AsNoTracking()
            .Where(u => !dbContext.Admins.Any(a => a.Id == u.Id))
            .Where(u => !dbContext.UserRoles.Any(ur => ur.UserId == u.Id && dbContext.Roles.Any(r =>
                r.Id == ur.RoleId && (r.Name == Roles.Admin || r.Name == Roles.SuperAdmin))))
            .Select(u => new
            {
                u.Id,
                FirstName = dbContext.Students.Where(s => s.Id == u.Id).Select(s => s.FirstName).FirstOrDefault(),
                LastName = dbContext.Students.Where(s => s.Id == u.Id).Select(s => s.LastName).FirstOrDefault(),
                u.Email,
                GroupName = dbContext.Students.Where(s => s.Id == u.Id).Select(s => s.Group == null ? null : s.Group.Name).FirstOrDefault(),
                u.EmailConfirmed,
                IsActive = dbContext.Students.Any(s => s.Id == u.Id && s.IsActive),
                HasStudentProfile = dbContext.Students.Any(s => s.Id == u.Id)
            });
        var term = query?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(term))
            accounts = accounts.Where(a => (a.Email != null && a.Email.ToLower().Contains(term)) ||
                ((a.LastName ?? "") + " " + (a.FirstName ?? "")).ToLower().Contains(term) ||
                ((a.FirstName ?? "") + " " + (a.LastName ?? "")).ToLower().Contains(term));

        var total = await accounts.CountAsync(cancellationToken);
        var items = await accounts.OrderBy(a => a.LastName).ThenBy(a => a.FirstName).ThenBy(a => a.Email).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new UserAccountDto(a.Id, a.FirstName, a.LastName, a.Email, a.GroupName,
                a.EmailConfirmed, a.IsActive, a.HasStudentProfile)).ToListAsync(cancellationToken);
        return new UserAccountPageDto(items, total, page, pageSize);
    }

    public Task DeleteNonAdminAccountAsync(Guid actingAdminId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.ExecuteSerializableAsync(async ct =>
        {
            await EnsureSuperAdminAsync(actingAdminId, ct);
            var isProtected = await dbContext.Admins.AnyAsync(a => a.Id == userId, ct) ||
                await dbContext.UserRoles.AnyAsync(ur => ur.UserId == userId && dbContext.Roles.Any(r =>
                    r.Id == ur.RoleId && (r.Name == Roles.Admin || r.Name == Roles.SuperAdmin)), ct);
            if (isProtected)
                throw new ForbiddenException("Administrator accounts cannot be deleted here.", "account.admin_protected");

            var account = await userManager.FindByIdAsync(userId.ToString())
                ?? throw new NotFoundException("User account not found.", "account.not_found");
            var deleted = await userManager.DeleteAsync(account);
            EnsureSucceeded(deleted, "Account");
        }, cancellationToken);

    private async Task EnsureSuperAdminAsync(Guid actingAdminId, CancellationToken cancellationToken)
    {
        var allowed = await dbContext.UserRoles.AnyAsync(ur => ur.UserId == actingAdminId &&
            dbContext.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.SuperAdmin), cancellationToken);
        if (!allowed)
            throw new ForbiddenException("Only a super administrator can manage user accounts.");
    }

    public async Task<AccountInfo?> FindByEmailAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : new AccountInfo(user.Id, user.EmailConfirmed);
    }

    public async Task<LoginResult> ValidateCredentialsAsync(string email, string password)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return new LoginResult(LoginStatus.InvalidCredentials, null);

        if (await userManager.IsLockedOutAsync(user))
            return new LoginResult(LoginStatus.LockedOut, null);

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            // Счётчик неудач + автоблокировка средствами Identity (защита от перебора).
            await userManager.AccessFailedAsync(user);
            return await userManager.IsLockedOutAsync(user)
                ? new LoginResult(LoginStatus.LockedOut, null)
                : new LoginResult(LoginStatus.InvalidCredentials, null);
        }

        if (!await userManager.IsEmailConfirmedAsync(user))
            return new LoginResult(LoginStatus.EmailNotConfirmed, null);

        await userManager.ResetAccessFailedCountAsync(user);
        return new LoginResult(LoginStatus.Success, user.Id);
    }

    public async Task<string?> GetEmailAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user?.Email;
    }

    public async Task<string?> GetPreferredCultureAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user?.PreferredCulture;
    }

    public async Task SetPreferredCultureAsync(Guid userId, string culture)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
                   ?? throw new FunAndChecks.Application.Common.Exceptions.NotFoundException("User account not found.", "account.not_found");
        user.PreferredCulture = culture;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> GetEmailsAsync(IEnumerable<Guid> userIds)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string?>();

        return await userManager.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);
    }

    public async Task<string?> GenerateEmailConfirmationCodeAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null || user.EmailConfirmed)
            return null;

        return await userManager.GenerateUserTokenAsync(
            user, TokenOptions.DefaultEmailProvider, EmailConfirmationPurpose);
    }

    public async Task<bool> ConfirmEmailAsync(string email, string code)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return false;

        if (user.EmailConfirmed)
            return true;

        var valid = await userManager.VerifyUserTokenAsync(
            user, TokenOptions.DefaultEmailProvider, EmailConfirmationPurpose, code);
        if (!valid)
            return false;

        user.EmailConfirmed = true;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<string?> GeneratePasswordResetCodeAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return null;

        return await userManager.GenerateUserTokenAsync(
            user, TokenOptions.DefaultEmailProvider, PasswordResetPurpose);
    }

    public async Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
            return AccountResult.Failure(["Invalid or expired reset code."]);

        var valid = await userManager.VerifyUserTokenAsync(
            user, TokenOptions.DefaultEmailProvider, PasswordResetPurpose, code);
        if (!valid)
            return AccountResult.Failure(["Invalid or expired reset code."]);

        AccountResult outcome = AccountResult.Success();
        await dbContext.ExecuteSerializableAsync(async _ =>
        {
            var resetResult = await userManager.ResetPasswordAsync(user, code, newPassword);
            if (!resetResult.Succeeded)
            {
                outcome = AccountResult.Failure(resetResult.Errors.Select(e => e.Description));
                return;
            }

            user.EmailConfirmed = true;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", updateResult.Errors.Select(e => e.Description)));

            var unlockResult = await userManager.ResetAccessFailedCountAsync(user);
            if (!unlockResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", unlockResult.Errors.Select(e => e.Description)));
        });

        return outcome;
    }

    public async Task UpdateAccountAdminAsync(Guid userId, string email, string? newPassword)
    {
        await dbContext.ExecuteSerializableAsync(async _ =>
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user == null)
                throw new FunAndChecks.Application.Common.Exceptions.NotFoundException($"User {userId} not found.");

            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var existing = await userManager.FindByEmailAsync(email);
                if (existing != null && existing.Id != userId)
                    throw new FunAndChecks.Application.Common.Exceptions.ConflictException(
                        $"Email '{email}' is already taken.", "account.email_taken");
                user.Email = email;
                user.UserName = email;
            }

            user.EmailConfirmed = true;
            var updateResult = await userManager.UpdateAsync(user);
            EnsureSucceeded(updateResult, "Account");

            if (!string.IsNullOrEmpty(newPassword))
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
                var passwordResult = await userManager.ResetPasswordAsync(user, resetToken, newPassword);
                EnsureSucceeded(passwordResult, "Password");
            }

            EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null), "Account");
            EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user), "Account");
            await refreshTokenService.RevokeAllAsync(userId);
        });
    }

    private static void EnsureSucceeded(IdentityResult result, string property)
    {
        if (result.Succeeded)
            return;

        throw new FluentValidation.ValidationException(result.Errors.Select(e =>
            new FluentValidation.Results.ValidationFailure(property, e.Description)).ToList());
    }
}

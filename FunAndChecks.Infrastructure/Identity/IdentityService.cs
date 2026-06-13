using FunAndChecks.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Infrastructure.Identity;

public class IdentityService(UserManager<ApplicationUser> userManager) : IIdentityService
{
    // Назначения токенов (TOTP-провайдер «Email» даёт короткие 6-значные коды).
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

        // Код проверен нашим TOTP-провайдером — меняем пароль напрямую
        // (без DataProtector-токена). Смена пароля обновит security stamp,
        // что инвалидирует уже выданные коды.
        await userManager.RemovePasswordAsync(user);
        var result = await userManager.AddPasswordAsync(user, newPassword);
        if (!result.Succeeded)
            return AccountResult.Failure(result.Errors.Select(e => e.Description));

        // Владение ящиком доказано — подтверждаем почту, если не была, и снимаем блокировку.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }
        await userManager.ResetAccessFailedCountAsync(user);

        return AccountResult.Success();
    }
}

using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Auth;

public class AuthService(
    IApplicationDbContext db,
    IIdentityService identityService,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IEmailSender emailSender,
    IEmailThrottle emailThrottle,
    IValidator<RegisterStudentRequest> registerValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator,
    ILogger<AuthService> logger)
    : IAuthService
{
    public async Task<Guid> RegisterStudentAsync(RegisterStudentRequest request, CancellationToken cancellationToken = default)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);

        var groupExists = await db.Groups.AnyAsync(g => g.Id == request.GroupId, cancellationToken);
        if (!groupExists)
            throw new NotFoundException($"Group with ID {request.GroupId} not found.");

        // Повторная регистрация на тот же email: подтверждённый занят, неподтверждённый — затираем.
        var existing = await identityService.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            if (existing.EmailConfirmed)
                throw new ConflictException("Этот email уже зарегистрирован.");

            await DeleteAccountAndProfileAsync(existing.Id, cancellationToken);
        }

        var studentId = Guid.NewGuid();
        var accountResult = await identityService.CreateAccountAsync(
            studentId,
            request.Email,
            request.Password,
            [Roles.Student],
            emailConfirmed: false,
            cancellationToken);

        if (!accountResult.Succeeded)
            throw new ValidationException(string.Join(" ", accountResult.Errors));

        try
        {
            db.Students.Add(new Student
            {
                Id = studentId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                GroupId = request.GroupId,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Компенсация: профиль не создался — учётную запись не оставляем.
            await identityService.DeleteAccountAsync(studentId, CancellationToken.None);
            throw;
        }

        await SendConfirmationCodeAsync(request.Email, cancellationToken);

        return studentId;
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (!await identityService.ConfirmEmailAsync(request.Email, request.Code))
            throw new ForbiddenException("Invalid or expired confirmation code.");

        // Активируем профиль студента — теперь он виден в рейтинге и не подлежит очистке.
        var account = await identityService.FindByEmailAsync(request.Email);
        if (account is not null)
        {
            var student = await db.Students.FindAsync([account.Id], cancellationToken);
            if (student is { IsActive: false })
            {
                student.IsActive = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task ResendConfirmationAsync(ResendConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        // Намеренно не сообщаем, существует ли такая почта.
        await SendConfirmationCodeAsync(request.Email, cancellationToken);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var result = await identityService.ValidateCredentialsAsync(request.Email, request.Password);

        return result.Status switch
        {
            LoginStatus.Success => await IssueTokensAsync(result.UserId!.Value, cancellationToken),
            LoginStatus.EmailNotConfirmed => throw new ForbiddenException("Email is not confirmed. Check your inbox for the confirmation code."),
            LoginStatus.LockedOut => throw new ForbiddenException("Account is temporarily locked due to too many failed attempts. Try again later."),
            _ => throw new ForbiddenException("Invalid credentials."),
        };
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var rotation = await refreshTokenService.RotateAsync(request.RefreshToken, cancellationToken)
                       ?? throw new ForbiddenException("Invalid or expired refresh token.");

        // Новый access выпускается с актуальными ролями; если права сняты/аккаунт удалён —
        // refresh уже был бы отозван (каскад/RevokeAll), и RotateAsync вернул бы null.
        var accessToken = await tokenService.CreateTokenAsync(rotation.UserId);
        return new AuthResponse(accessToken, rotation.NewRefreshToken);
    }

    public Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default) =>
        refreshTokenService.RevokeAsync(request.RefreshToken, cancellationToken);

    private async Task<AuthResponse> IssueTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accessToken = await tokenService.CreateTokenAsync(userId);
        var refreshToken = await refreshTokenService.IssueAsync(userId, cancellationToken);
        return new AuthResponse(accessToken, refreshToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        EnsureEmailNotThrottled(request.Email);

        var code = await identityService.GeneratePasswordResetCodeAsync(request.Email);

        // Намеренно не сообщаем, существует ли такая почта.
        if (code == null)
        {
            logger.LogInformation("Password reset requested for unknown email.");
            return;
        }

        await emailSender.SendAsync(
            request.Email,
            EmailTemplates.PasswordResetSubject,
            EmailTemplates.PasswordReset(code),
            cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        await resetPasswordValidator.ValidateAndThrowAsync(request, cancellationToken);

        var result = await identityService.ResetPasswordAsync(request.Email, request.Code, request.NewPassword);
        if (!result.Succeeded)
            throw new ForbiddenException("Invalid or expired reset code, or the password does not meet requirements.");

        // Смена пароля обесценивает все ранее выданные refresh-токены (защита при компрометации).
        var account = await identityService.FindByEmailAsync(request.Email);
        if (account is not null)
            await refreshTokenService.RevokeAllAsync(account.Id, cancellationToken);
    }

    private async Task SendConfirmationCodeAsync(string email, CancellationToken cancellationToken)
    {
        EnsureEmailNotThrottled(email);

        var code = await identityService.GenerateEmailConfirmationCodeAsync(email);
        if (code == null)
            return; // почты нет или она уже подтверждена

        await emailSender.SendAsync(
            email,
            EmailTemplates.ConfirmationSubject,
            EmailTemplates.Confirmation(code),
            cancellationToken);
    }

    private void EnsureEmailNotThrottled(string email)
    {
        if (!emailThrottle.TryAcquire(email, out var retryAfter))
            throw new RateLimitException(
                $"Письмо можно отправлять не чаще одного раза в минуту. Повторите через {Math.Ceiling(retryAfter.TotalSeconds)} с.");
    }

    private async Task DeleteAccountAndProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        var student = await db.Students.FindAsync([id], cancellationToken);
        if (student is not null)
        {
            db.Students.Remove(student);
            await db.SaveChangesAsync(cancellationToken);
        }

        await identityService.DeleteAccountAsync(id, cancellationToken);
    }
}

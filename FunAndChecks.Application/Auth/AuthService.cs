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
    IEmailSender emailSender,
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
                GitHubUrl = request.GitHubUrl,
                Color = request.Color,
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
            LoginStatus.Success => new AuthResponse(await tokenService.CreateTokenAsync(result.UserId!.Value)),
            LoginStatus.EmailNotConfirmed => throw new ForbiddenException("Email is not confirmed. Check your inbox for the confirmation code."),
            LoginStatus.LockedOut => throw new ForbiddenException("Account is temporarily locked due to too many failed attempts. Try again later."),
            _ => throw new ForbiddenException("Invalid credentials."),
        };
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
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
    }

    private async Task SendConfirmationCodeAsync(string email, CancellationToken cancellationToken)
    {
        var code = await identityService.GenerateEmailConfirmationCodeAsync(email);
        if (code == null)
            return; // почты нет или она уже подтверждена

        await emailSender.SendAsync(
            email,
            EmailTemplates.ConfirmationSubject,
            EmailTemplates.Confirmation(code),
            cancellationToken);
    }
}

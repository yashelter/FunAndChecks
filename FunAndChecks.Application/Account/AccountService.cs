using System.Net;
using FluentValidation;
using FunAndChecks.Application.Auth;
using FunAndChecks.Application.Common;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Account;

public class AccountService(
    IApplicationDbContext db,
    IIdentityService identityService,
    IEmailSender emailSender,
    IEmailTemplateRenderer emailTemplates,
    IEmailThrottle emailThrottle,
    IResultsCacheService resultsCache,
    IValidator<ChangeNameRequest> nameValidator,
    IValidator<RequestEmailChangeRequest> requestEmailValidator,
    IValidator<ConfirmEmailChangeRequest> confirmEmailValidator,
    ILogger<AccountService> logger)
    : IAccountService
{
    public async Task ChangeNameAsync(Guid studentId, ChangeNameRequest request, CancellationToken cancellationToken = default)
    {
        await nameValidator.ValidateAndThrowAsync(request, cancellationToken);
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken)
                      ?? throw new NotFoundException("Student profile not found.");
        student.FirstName = request.FirstName.Trim();
        student.LastName = request.LastName.Trim();
        await db.SaveChangesAsync(cancellationToken);

        // Имя показывается в кэшированных таблицах результатов предметов его группы.
        if (student.GroupId is int groupId)
        {
            var subjectIds = await db.GroupSubjects.Where(gs => gs.GroupId == groupId).Select(gs => gs.SubjectId)
                .ToListAsync(cancellationToken);
            foreach (var subjectId in subjectIds)
                resultsCache.Invalidate(subjectId);
        }
    }

    public async Task RequestEmailChangeAsync(Guid userId, RequestEmailChangeRequest request, CancellationToken cancellationToken = default)
    {
        await requestEmailValidator.ValidateAndThrowAsync(request, cancellationToken);
        var newEmail = request.NewEmail.Trim();
        var current = await identityService.GetEmailAsync(userId) ?? throw new NotFoundException("Account not found.");
        if (string.Equals(current, newEmail, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("This is already your email.", "account.email_unchanged");
        if (!await identityService.CheckPasswordAsync(userId, request.CurrentPassword))
            throw new ForbiddenException("The current password is incorrect.", "account.invalid_password");
        if (await identityService.FindByEmailAsync(newEmail) is not null)
            throw new ConflictException($"Email '{newEmail}' is already taken.", "account.email_taken");
        // Не даёт засыпать чужой ящик письмами.
        if (!emailThrottle.TryAcquire(newEmail, out var retryAfter))
            throw new RateLimitException(
                $"Email can be sent once per minute. Retry in {Math.Ceiling(retryAfter.TotalSeconds)} seconds.",
                "email.rate_limited",
                new Dictionary<string, object?> { ["retryAfterSeconds"] = Math.Ceiling(retryAfter.TotalSeconds) });

        var code = await identityService.GenerateEmailChangeCodeAsync(userId, newEmail)
                   ?? throw new NotFoundException("Account not found.");
        var template = emailTemplates.Render(EmailTemplateKind.EmailChange, await CultureAsync(userId), code);
        await emailSender.SendAsync(newEmail, template.Subject, template.HtmlBody, cancellationToken);
    }

    public async Task ConfirmEmailChangeAsync(Guid userId, ConfirmEmailChangeRequest request, CancellationToken cancellationToken = default)
    {
        await confirmEmailValidator.ValidateAndThrowAsync(request, cancellationToken);
        var newEmail = request.NewEmail.Trim();
        var oldEmail = await identityService.ChangeEmailAsync(userId, newEmail, request.Code.Trim())
                       ?? throw new ForbiddenException("Invalid or expired code.", "account.invalid_email_code");

        // Уведомление на прежний адрес — после смены и без влияния на результат: если ящик недоступен,
        // почта всё равно уже изменена.
        if (string.IsNullOrWhiteSpace(oldEmail)) return;
        try
        {
            var template = emailTemplates.Render(EmailTemplateKind.EmailChanged, await CultureAsync(userId), WebUtility.HtmlEncode(newEmail));
            await emailSender.SendAsync(oldEmail, template.Subject, template.HtmlBody, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to notify the previous address about an email change for {UserId}", userId);
        }
    }

    // Как в AuthService: язык из настроек получателя, иначе язык текущего запроса.
    private async Task<string> CultureAsync(Guid userId) =>
        SupportedCultures.TryNormalize(await identityService.GetPreferredCultureAsync(userId))
        ?? SupportedCultures.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name);
}

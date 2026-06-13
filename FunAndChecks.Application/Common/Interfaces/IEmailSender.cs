namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Отправка писем. Реализуется в Infrastructure (Resend).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FunAndChecks.Infrastructure.Email;

/// <summary>
/// Отправка писем по SMTP через MailKit. Подключается к собственному
/// почтовому серверу (например, Postfix на VDS) — без внешних API и лимитов.
/// </summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            // Email bodies can contain verification/reset codes. Never log them.
            logger.LogWarning("SMTP host is not configured. Email delivery skipped.");
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        var secureOptions = _options.UseStartTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, secureOptions, cancellationToken);

        if (!string.IsNullOrEmpty(_options.User))
        {
            if (string.IsNullOrEmpty(_options.Password))
                throw new InvalidOperationException("SMTP password must be configured when SMTP user is set.");
            await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken);
        }

        try
        {
            await client.SendAsync(message, cancellationToken);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted && (int)ex.StatusCode >= 500)
        {
            // Постоянный отказ по адресу (например, nullMX у домена) — это ошибка ввода, а не 500.
            // Временные 4xx и сбои соединения пробрасываем как есть.
            // Текст ответа сервера содержит адрес получателя — в лог его не пишем.
            logger.LogWarning("SMTP permanently rejected the recipient ({Status}).", ex.StatusCode);
            throw new EmailDeliveryException("The mail server does not accept email for this address.");
        }
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

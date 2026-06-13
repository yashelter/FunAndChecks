using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FunAndChecks.Application.Common.Interfaces;

namespace FunAndChecks.Tests.Integration;

/// <summary>Перехватывает письма и достаёт из них 6-значный код для проверок флоу.</summary>
public class CapturingEmailSender : IEmailSender
{
    public record SentEmail(string To, string Subject, string Body);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public string? LastCodeFor(string email)
    {
        var last = _sent.LastOrDefault(e => e.To == email);
        if (last == null)
            return null;

        var match = Regex.Match(last.Body, @"\b(\d{6})\b");
        return match.Success ? match.Groups[1].Value : null;
    }
}

using FluentAssertions;
using FunAndChecks.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FunAndChecks.Tests;

public sealed class SmtpLoggingTests
{
    [Fact]
    public async Task UnconfiguredSmtp_LogsDiagnosticWithoutEmailBodyOrVerificationSecrets()
    {
        var logger = new CaptureLogger();
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions()), logger);
        await sender.SendAsync("private@example.test", "Verification code 123456", "<div>123456</div> reset_token=secret-value");
        logger.Messages.Should().ContainSingle().Which.Should().Contain("SMTP host is not configured")
            .And.NotContain("123456").And.NotContain("secret-value").And.NotContain("private@example.test");
    }

    private sealed class CaptureLogger : ILogger<SmtpEmailSender>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

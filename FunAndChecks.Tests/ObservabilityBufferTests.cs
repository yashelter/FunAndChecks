using FluentAssertions;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Observability;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace FunAndChecks.Tests;

public sealed class ObservabilityBufferTests
{
    [Fact]
    public void FullAuditQueue_DropsWithoutThrowingOrBlocking()
    {
        var metrics = new ApiMetrics();
        using var writer = new FileAdminAuditWriter(Options.Create(new AdminAuditOptions { QueueCapacity = 1 }), metrics);
        writer.TryWrite(Event()).Should().BeTrue();
        writer.TryWrite(Event()).Should().BeFalse();
        metrics.AuditDropped.WithLabels("queue_full_or_stopping").Value.Should().Be(1);
    }

    [Fact]
    public async Task AuditFile_IsFlushedAsCamelCaseJson_AndSensitiveValuesAreRedacted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "funandchecks-audit-writer-" + Guid.NewGuid().ToString("N"));
        var metrics = new ApiMetrics();
        using var writer = new FileAdminAuditWriter(Options.Create(new AdminAuditOptions { Directory = directory }), metrics);
        try
        {
            await writer.StartAsync(CancellationToken.None);
            writer.TryWrite(Event() with
            {
                Before = new Dictionary<string, object?> { ["points"] = 3, ["password"] = "must-not-leak" },
                After = new Dictionary<string, object?> { ["points"] = 5, ["comment"] = "token=must-not-leak {\"password\":\"must-not-leak with spaces\"} pwd='must-not-leak with spaces'", ["passwordChanged"] = true }
            }).Should().BeTrue();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await writer.StopAsync(timeout.Token);
            var path = Directory.GetFiles(directory, "admin-audit-*.jsonl").Should().ContainSingle().Which;
            var text = await File.ReadAllTextAsync(path);
            text.Should().NotContain("must-not-leak");
            using var json = JsonDocument.Parse(text);
            json.RootElement.GetProperty("kind").GetString().Should().Be("admin_audit");
            json.RootElement.GetProperty("actorRole").GetString().Should().Be("Admin");
            json.RootElement.GetProperty("requestId").GetString().Should().Be("test-request");
            json.RootElement.GetProperty("before").GetProperty("points").GetInt32().Should().Be(3);
            json.RootElement.GetProperty("after").GetProperty("points").GetInt32().Should().Be(5);
            json.RootElement.GetProperty("after").GetProperty("passwordChanged").GetBoolean().Should().BeTrue();
            metrics.AuditWritten.Value.Should().Be(1);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task UnwritableAuditDestination_DoesNotFailHost_AndCountsLoss()
    {
        var path = Path.Combine(Path.GetTempPath(), "funandchecks-audit-not-directory-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "keep");
        var metrics = new ApiMetrics();
        using var writer = new FileAdminAuditWriter(Options.Create(new AdminAuditOptions { Directory = path }), metrics);
        try
        {
            await writer.StartAsync(CancellationToken.None);
            writer.TryWrite(Event()).Should().BeTrue();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await writer.StopAsync(timeout.Token);
            metrics.AuditDropped.WithLabels("write_error").Value.Should().Be(1);
            (await File.ReadAllTextAsync(path)).Should().Be("keep");
        }
        finally { File.Delete(path); }
    }

    private static AdminAuditEvent Event() => new(DateTimeOffset.UtcNow, Guid.NewGuid(), "Teacher", "Admin", "grades.set", "grade", "1", "success", "Grade changed", "test-request");
}

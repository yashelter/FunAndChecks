using FluentAssertions;
using FunAndChecks.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace FunAndChecks.Tests;

public sealed class ApiMetricsTests
{
    [Fact]
    public async Task Requests_UseRouteTemplatesAndFinalStatus_AndRestoreInFlight()
    {
        var metrics = new ApiMetrics();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/subjects/134";
        context.Request.Method = "GET";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/subjects/{subjectId:int}"), 0, EndpointMetadataCollection.Empty, "subject"));
        var middleware = new ApiMetricsMiddleware(http =>
        {
            metrics.InFlight.Value.Should().Be(1);
            http.Response.StatusCode = 403;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context, metrics);
        metrics.Requests.WithLabels("/api/subjects/{subjectId:int}", "GET", "403").Value.Should().Be(1);
        metrics.InFlight.Value.Should().Be(0);
        using var stream = new MemoryStream();
        await metrics.Registry.CollectAndExportAsTextAsync(stream);
        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        text.Should().Contain("process_working_set_bytes").And.Contain("dotnet_collection_count_total");
        text.Should().NotContain("/api/subjects/134");
    }

    [Theory]
    [InlineData("/metrics")]
    [InlineData("/internal/metrics")]
    [InlineData("/api/monitoring/session")]
    [InlineData("/apiHub/queueHub")]
    [InlineData("/health")]
    [InlineData("/_framework/blazor.webassembly.js")]
    public async Task MonitoringStaticAndSignalR_AreExcluded(string path)
    {
        var metrics = new ApiMetrics();
        var context = new DefaultHttpContext(); context.Request.Path = path;
        var called = false;
        await new ApiMetricsMiddleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context, metrics);
        called.Should().BeTrue();
        metrics.InFlight.Value.Should().Be(0);
        using var stream = new MemoryStream();
        await metrics.Registry.CollectAndExportAsTextAsync(stream);
        System.Text.Encoding.UTF8.GetString(stream.ToArray()).Should().NotContain("funandchecks_http_requests_total{");
    }

    [Fact]
    public async Task Failure_IsRecordedWithoutReplacingException()
    {
        var metrics = new ApiMetrics();
        var context = new DefaultHttpContext(); context.Request.Path = "/api/missing"; context.Request.Method = "CUSTOM-UNBOUNDED";
        var failure = new InvalidOperationException("failure");
        var exception = await Record.ExceptionAsync(() => new ApiMetricsMiddleware(_ => throw failure).InvokeAsync(context, metrics));
        exception.Should().BeSameAs(failure);
        metrics.Requests.WithLabels("unmatched", "OTHER", "500").Value.Should().Be(1);
        metrics.InFlight.Value.Should().Be(0);
    }
}

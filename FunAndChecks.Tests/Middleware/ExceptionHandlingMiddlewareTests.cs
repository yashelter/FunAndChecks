using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_UnhandledException_ReturnsSafeCoded500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var sut = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("sensitive database detail"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("code").GetString().Should().Be("server.internal_error");
        json.RootElement.GetProperty("detail").GetString().Should().NotContain("sensitive");
    }

    [Fact]
    public async Task InvokeAsync_Conflict_PreservesCodeAndNamedArguments()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var sut = new ExceptionHandlingMiddleware(
            _ => throw new ConflictException("Not enrolled.", "student.not_enrolled",
                new Dictionary<string, object?> { ["subjectId"] = 42 }),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        json.RootElement.GetProperty("code").GetString().Should().Be("student.not_enrolled");
        json.RootElement.GetProperty("args").GetProperty("subjectId").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task InvokeAsync_WhenResponseHasStarted_RethrowsException_AndDoesNotModifyStatusCode()
    {
        // Arrange
        var response = Substitute.For<HttpResponse>();
        response.HasStarted.Returns(true);

        var request = Substitute.For<HttpRequest>();
        request.Method.Returns("GET");
        request.Path.Returns(new PathString("/test"));

        var context = Substitute.For<HttpContext>();
        context.Response.Returns(response);
        context.Request.Returns(request);

        var exceptionToThrow = new InvalidOperationException("Test exception");

        RequestDelegate next = _ => throw exceptionToThrow;

        var sut = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        // Act
        var act = () => sut.InvokeAsync(context);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("Test exception");

        // Verify status code was not modified to 500 (0 — дефолт незатронутого заместителя)
        response.StatusCode.Should().Be(0);
    }
}

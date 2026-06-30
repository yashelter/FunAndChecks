using System;
using System.Threading.Tasks;
using FluentAssertions;
using FunAndChecks.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FunAndChecks.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenResponseHasStarted_RethrowsException_AndDoesNotModifyStatusCode()
    {
        // Arrange
        var contextMock = new Mock<HttpContext>();
        var responseMock = new Mock<HttpResponse>();
        
        responseMock.Setup(r => r.HasStarted).Returns(true);

        contextMock.Setup(c => c.Response).Returns(responseMock.Object);
        
        var requestMock = new Mock<HttpRequest>();
        requestMock.Setup(r => r.Method).Returns("GET");
        requestMock.Setup(r => r.Path).Returns("/test");
        contextMock.Setup(c => c.Request).Returns(requestMock.Object);

        var exceptionToThrow = new InvalidOperationException("Test exception");
        
        RequestDelegate next = _ => throw exceptionToThrow;

        var sut = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);

        // Act
        var act = () => sut.InvokeAsync(contextMock.Object);

        // Assert
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.WithMessage("Test exception");

        // Verify status code was not modified to 500
        responseMock.VerifySet(r => r.StatusCode = 500, Times.Never);
    }
}

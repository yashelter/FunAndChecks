using System;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using FunAndChecks.Infrastructure.Email;

namespace FunAndChecks.Tests;

public class EmailThrottleTests
{
    [Fact]
    public void TryAcquire_SecondCall_ReturnsFalse_AndSetsRetryAfter()
    {
        // Arrange
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new EmailThrottle(cache);
        var email = "test@example.com";

        // Act
        var firstCall = sut.TryAcquire(email, out var retryAfter1);
        var secondCall = sut.TryAcquire(email, out var retryAfter2);

        // Assert
        Assert.True(firstCall);
        Assert.Equal(TimeSpan.Zero, retryAfter1);
        
        Assert.False(secondCall);
        Assert.True(retryAfter2 > TimeSpan.Zero);
    }
}

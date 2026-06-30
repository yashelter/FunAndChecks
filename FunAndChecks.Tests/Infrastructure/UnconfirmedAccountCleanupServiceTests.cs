using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FunAndChecks.Infrastructure.Workers;
using FunAndChecks.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FunAndChecks.Tests;

public class UnconfirmedAccountCleanupServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();

    [Fact]
    public async Task CleanupAsync_DeletesOnlyOldUnconfirmedUsers()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        
        var confirmedUser = ctx.Student(group, "Confirmed");
        confirmedUser.IsActive = true;
        
        var recentUnconfirmed = ctx.Student(group, "RecentUnconfirmed");
        recentUnconfirmed.IsActive = false;
        recentUnconfirmed.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        
        var oldUnconfirmed = ctx.Student(group, "OldUnconfirmed");
        oldUnconfirmed.IsActive = false;
        oldUnconfirmed.CreatedAt = DateTime.UtcNow.AddHours(-25);

        await ctx.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        var sp = services.BuildServiceProvider();

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(sp);

        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        var sut = new UnconfirmedAccountCleanupService(
            scopeFactoryMock.Object, 
            NullLogger<UnconfirmedAccountCleanupService>.Instance);

        var cleanupMethod = typeof(UnconfirmedAccountCleanupService)
            .GetMethod("CleanupAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            
        await (Task)cleanupMethod!.Invoke(sut, [CancellationToken.None])!;

        var usersAfter = ctx.Users.ToList();
        usersAfter.Should().Contain(u => u.Id == confirmedUser.Id);
        usersAfter.Should().Contain(u => u.Id == recentUnconfirmed.Id);
        usersAfter.Should().NotContain(u => u.Id == oldUnconfirmed.Id);
    }

    public void Dispose() => _db.Dispose();
}

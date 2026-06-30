using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class IdentityServiceTests
{
    private readonly TestWebAppFactory _factory;

    public IdentityServiceTests(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateAccountAdminAsync_ValidChanges_UpdatesEmailAndPassword()
    {
        using var scope = _factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        var id = Guid.NewGuid();
        await identityService.CreateAccountAsync(id, "user_update@example.com", "OldPassword123!", [], true);

        await identityService.UpdateAccountAdminAsync(id, "new_update@example.com", "NewPassword123!");

        var info = await identityService.FindByEmailAsync("new_update@example.com");
        info.Should().NotBeNull();
        info!.Id.Should().Be(id);
        info.EmailConfirmed.Should().BeTrue();

        var loginResult = await identityService.ValidateCredentialsAsync("new_update@example.com", "NewPassword123!");
        loginResult.Status.Should().Be(LoginStatus.Success);
    }

    [Fact]
    public async Task UpdateAccountAdminAsync_EmailCollision_ThrowsConflictException()
    {
        using var scope = _factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await identityService.CreateAccountAsync(id1, "collision1@example.com", "Password123!", [], true);
        await identityService.CreateAccountAsync(id2, "collision2@example.com", "Password123!", [], true);

        var act = async () => await identityService.UpdateAccountAdminAsync(id1, "collision2@example.com", null);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*already taken*");
    }
}

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

    [Fact]
    public async Task UpdateAccountAdminAsync_InvalidPassword_RollsBackEmailAndKeepsOldPassword()
    {
        var id = Guid.NewGuid();
        var oldEmail = $"atomic-old-{id:N}@example.com";
        var newEmail = $"atomic-new-{id:N}@example.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            await identityService.CreateAccountAsync(id, oldEmail, "OldPassword123!", [], true);

            var act = () => identityService.UpdateAccountAdminAsync(id, newEmail, "short");
            await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        }

        using var verificationScope = _factory.Services.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<IIdentityService>();
        (await verification.FindByEmailAsync(newEmail)).Should().BeNull();
        (await verification.ValidateCredentialsAsync(oldEmail, "OldPassword123!")).Status.Should().Be(LoginStatus.Success);
    }

    [Fact]
    public async Task ResetPasswordAsync_InvalidNewPassword_KeepsOldPassword()
    {
        using var scope = _factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var id = Guid.NewGuid();
        var email = $"reset-atomic-{id:N}@example.com";
        await identityService.CreateAccountAsync(id, email, "OldPassword123!", [], true);
        var code = await identityService.GeneratePasswordResetCodeAsync(email);

        var result = await identityService.ResetPasswordAsync(email, code!, "short");

        result.Succeeded.Should().BeFalse();
        (await identityService.ValidateCredentialsAsync(email, "OldPassword123!")).Status.Should().Be(LoginStatus.Success);
    }

    [Fact]
    public async Task PreferredCulture_RoundTrips()
    {
        using var scope = _factory.Services.CreateScope();
        var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var id = Guid.NewGuid();
        await identityService.CreateAccountAsync(id, $"culture-{id:N}@example.com", "Password123!", [], true);

        await identityService.SetPreferredCultureAsync(id, "ru-RU");

        (await identityService.GetPreferredCultureAsync(id)).Should().Be("ru-RU");
    }
}

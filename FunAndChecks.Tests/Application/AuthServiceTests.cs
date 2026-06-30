using FluentAssertions;
using FluentValidation;
using FunAndChecks.Application.Auth;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class AuthServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly ITokenService _token = Substitute.For<ITokenService>();
    private readonly IRefreshTokenService _refresh = Substitute.For<IRefreshTokenService>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IEmailThrottle _throttle = Substitute.For<IEmailThrottle>();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();

    public AuthServiceTests()
    {
        // По умолчанию троттлинг пропускает.
        _throttle.TryAcquire(Arg.Any<string>(), out Arg.Any<TimeSpan>())
            .Returns(ci => { ci[1] = TimeSpan.Zero; return true; });

        _refresh.IssueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns("refresh-token");
    }

    private AuthService CreateSut(Infrastructure.Persistence.ApplicationDbContext ctx) =>
        new(ctx, _identity, _token, _refresh, _email, _throttle, _cache,
            new RegisterStudentRequestValidator(),
            new ResetPasswordRequestValidator(),
            NullLogger<AuthService>.Instance);

    private async Task<int> SeedGroupAsync()
    {
        await using var ctx = _db.NewContext();
        var group = ctx.Group();
        await ctx.SaveChangesAsync();
        return group.Id;
    }

    [Fact]
    public async Task RegisterStudent_Valid_CreatesProfileAndSendsConfirmation()
    {
        var groupId = await SeedGroupAsync();
        // Замокан identity — но FK требует строку аккаунта; создаём её при «создании учётки».
        _identity.CreateAccountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(), false, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                using var c = _db.NewContext();
                Seed.AddAccount(c, ci.ArgAt<Guid>(0));
                c.SaveChanges();
                return AccountResult.Success();
            });
        _identity.GenerateEmailConfirmationCodeAsync("stud@example.com").Returns("123456");

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var request = new RegisterStudentRequest("Ann", "Smith", "stud@example.com", "secret", groupId);
        var id = await sut.RegisterStudentAsync(request);

        (await ctx.Students.FindAsync(id)).Should().NotBeNull();
        await _email.Received(1).SendAsync("stud@example.com", Arg.Any<string>(), Arg.Is<string>(b => b.Contains("123456")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterStudent_UnknownGroup_ThrowsNotFound()
    {
        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var request = new RegisterStudentRequest("Ann", "Smith", "stud@example.com", "secret", 999);
        var act = () => sut.RegisterStudentAsync(request);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RegisterStudent_BlankEmail_FailsValidation()
    {
        var groupId = await SeedGroupAsync();
        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var request = new RegisterStudentRequest("Ann", "Smith", "", "secret", groupId);
        var act = () => sut.RegisterStudentAsync(request);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task RegisterStudent_WhenAccountFails_ThrowsAndCreatesNoProfile()
    {
        var groupId = await SeedGroupAsync();
        _identity.CreateAccountAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(), false, Arg.Any<CancellationToken>())
            .Returns(AccountResult.Failure(["Email already taken."]));

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var request = new RegisterStudentRequest("Ann", "Smith", "dup@example.com", "secret", groupId);
        var act = () => sut.RegisterStudentAsync(request);
        await act.Should().ThrowAsync<ValidationException>();
        (await ctx.Students.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterStudent_ConfirmedEmailExists_ReturnsEmptyGuid_AndSendsEmail()
    {
        var groupId = await SeedGroupAsync();
        _identity.FindByEmailAsync("taken@example.com").Returns(new AccountInfo(Guid.NewGuid(), EmailConfirmed: true));

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var request = new RegisterStudentRequest("Ann", "Smith", "taken@example.com", "secret", groupId);
        var result = await sut.RegisterStudentAsync(request);

        result.Should().Be(Guid.Empty);
        await _email.Received(1).SendAsync("taken@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForgotPassword_Throttled_ThrowsRateLimit()
    {
        _throttle.TryAcquire(Arg.Any<string>(), out Arg.Any<TimeSpan>())
            .Returns(ci => { ci[1] = TimeSpan.FromSeconds(30); return false; });

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var act = () => sut.ForgotPasswordAsync(new ForgotPasswordRequest("a@b.c"));
        await act.Should().ThrowAsync<RateLimitException>();
    }

    [Fact]
    public async Task Login_Success_ReturnsAccessAndRefresh()
    {
        _identity.ValidateCredentialsAsync("a@b.c", "pwd")
            .Returns(new LoginResult(LoginStatus.Success, Guid.NewGuid()));
        _token.CreateTokenAsync(Arg.Any<Guid>()).Returns("jwt-token");

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var response = await sut.LoginAsync(new LoginRequest("a@b.c", "pwd"));
        response.AccessToken.Should().Be("jwt-token");
        response.RefreshToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task Refresh_RotatesAndReturnsNewPair()
    {
        var userId = Guid.NewGuid();
        _refresh.RotateAsync("old-refresh", Arg.Any<CancellationToken>())
            .Returns(new RefreshRotation(userId, "new-refresh"));
        _token.CreateTokenAsync(userId).Returns("new-access");

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var response = await sut.RefreshAsync(new RefreshRequest("old-refresh"));
        response.AccessToken.Should().Be("new-access");
        response.RefreshToken.Should().Be("new-refresh");
    }

    [Fact]
    public async Task Refresh_InvalidToken_Throws()
    {
        _refresh.RotateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((RefreshRotation?)null);

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var act = () => sut.RefreshAsync(new RefreshRequest("bad"));
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ResetPassword_RevokesAllRefreshTokens()
    {
        var userId = Guid.NewGuid();
        _identity.ResetPasswordAsync("a@b.c", "000000", "newpass").Returns(AccountResult.Success());
        _identity.FindByEmailAsync("a@b.c").Returns(new AccountInfo(userId, EmailConfirmed: true));

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        await sut.ResetPasswordAsync(new ResetPasswordRequest("a@b.c", "000000", "newpass"));

        await _refresh.Received(1).RevokeAllAsync(userId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(LoginStatus.EmailNotConfirmed)]
    [InlineData(LoginStatus.LockedOut)]
    [InlineData(LoginStatus.InvalidCredentials)]
    public async Task Login_Failure_Throws(LoginStatus status)
    {
        _identity.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new LoginResult(status, null));

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var act = () => sut.LoginAsync(new LoginRequest("a@b.c", "pwd"));
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_DoesNotSend()
    {
        _identity.GeneratePasswordResetCodeAsync("ghost@example.com").Returns((string?)null);

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        await sut.ForgotPasswordAsync(new ForgotPasswordRequest("ghost@example.com"));
        await _email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_InvalidCode_Throws()
    {
        _identity.ResetPasswordAsync("a@b.c", "000000", "newpass")
            .Returns(AccountResult.Failure(["Invalid or expired reset code."]));

        await using var ctx = _db.NewContext();
        var sut = CreateSut(ctx);

        var act = () => sut.ResetPasswordAsync(new ResetPasswordRequest("a@b.c", "000000", "newpass"));
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    public void Dispose() => _db.Dispose();
}

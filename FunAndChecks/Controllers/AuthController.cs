using FunAndChecks.Application.Auth;
using FunAndChecks.Application.Common;
using FunAndChecks.Common;
using FunAndChecks.Monitoring;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public class AuthController(IAuthService authService, IOptions<FeatureFlags> flags) : ControllerBase
{
    private readonly FeatureFlags _flags = flags.Value;
    /// <summary>Регистрация студента. На указанную почту отправляется код подтверждения.</summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterStudentRequest request, CancellationToken cancellationToken)
    {
        await authService.RegisterStudentAsync(request, cancellationToken);
        // A neutral body avoids disclosing whether this address already had an account.
        return StatusCode(StatusCodes.Status201Created);
    }

    /// <summary>Подтверждение почты по коду из письма.</summary>
    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await authService.ConfirmEmailAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Повторно отправить код подтверждения почты.</summary>
    [HttpPost("resend-confirmation")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequest request, CancellationToken cancellationToken)
    {
        await authService.ResendConfirmationAsync(request, cancellationToken);
        return Accepted();
    }

    /// <summary>Вход по email и паролю. Требует подтверждённую почту.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.LoginAsync(request, cancellationToken));

    /// <summary>Обновить access-токен по refresh-токену (с ротацией refresh).</summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        Ok(await authService.RefreshAsync(request, cancellationToken));

    /// <summary>Выход: отзывает refresh-токен.</summary>
    [HttpPost("logout")]
    [DisableRateLimiting]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        // The cookie is scoped to /metrics, so it is not sent to this API path.
        // Signing out still expires that exact cookie in the browser.
        await HttpContext.SignOutAsync(MonitoringAuthentication.Scheme);
        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Запросить код для сброса пароля (отправляется на почту).</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await authService.ForgotPasswordAsync(request, cancellationToken);
        return Accepted();
    }

    /// <summary>Сбросить пароль по коду из письма.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await authService.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Получить статус согласия с политикой конфиденциальности и Пользовательским соглашением текущего пользователя.</summary>
    [HttpGet("privacy-policy-status")]
    [Authorize]
    [ProducesResponseType(typeof(PrivacyPolicyStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PrivacyPolicyStatusDto>> GetPrivacyPolicyStatus(CancellationToken cancellationToken)
    {
        if (!_flags.PrivacyPolicy)
            return NotFound();

        var userId = GetCurrentUserId();
        var status = await authService.GetPrivacyPolicyStatusAsync(userId, cancellationToken);
        return Ok(status);
    }

    /// <summary>Принять политику конфиденциальности и/или Пользовательское соглашение от имени текущего пользователя.</summary>
    [HttpPost("accept-privacy-policy")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptPrivacyPolicy(AcceptLegalRequest request, CancellationToken cancellationToken)
    {
        if (!_flags.PrivacyPolicy)
            return NotFound();

        var userId = GetCurrentUserId();
        await authService.AcceptLegalAsync(userId, request, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.Parse(userIdClaim!);
    }
}

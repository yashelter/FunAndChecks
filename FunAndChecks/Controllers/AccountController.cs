using FunAndChecks.Application.Account;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FunAndChecks.Controllers;

/// <summary>Студент сам меняет имя и почту своей учётной записи.</summary>
[ApiController]
[Route("api/me/account")]
[Authorize(Roles = Roles.Student)]
public class AccountController(IAccountService accountService) : ControllerBase
{
    /// <summary>Сменить фамилию и имя.</summary>
    [HttpPut("name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeName(ChangeNameRequest request, CancellationToken cancellationToken)
    {
        await accountService.ChangeNameAsync(User.GetUserId(), request, cancellationToken);
        return NoContent();
    }

    /// <summary>Шаг 1 смены почты: проверить пароль и отправить код на новый адрес.</summary>
    [HttpPost("email/change")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestEmailChange(RequestEmailChangeRequest request, CancellationToken cancellationToken)
    {
        await accountService.RequestEmailChangeAsync(User.GetUserId(), request, cancellationToken);
        return Accepted();
    }

    /// <summary>Шаг 2 смены почты: подтвердить код из письма.</summary>
    [HttpPost("email/confirm")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeRequest request, CancellationToken cancellationToken)
    {
        await accountService.ConfirmEmailChangeAsync(User.GetUserId(), request, cancellationToken);
        return NoContent();
    }
}

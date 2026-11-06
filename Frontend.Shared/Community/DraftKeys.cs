using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Frontend.Shared.Community;

/// <summary>
/// Ключи черновиков в localStorage привязаны к пользователю: на общем компьютере следующий аккаунт
/// не должен видеть чужие недописанные сообщения и опросы.
/// </summary>
public static class DraftKeys
{
    /// <summary>Ключ для текущего пользователя или null, если пользователь неизвестен (тогда черновик не храним).</summary>
    public static async Task<string?> ForCurrentUserAsync(AuthenticationStateProvider auth, string baseKey)
    {
        var state = await auth.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return string.IsNullOrEmpty(userId) ? null : $"{baseKey}:{userId}";
    }
}

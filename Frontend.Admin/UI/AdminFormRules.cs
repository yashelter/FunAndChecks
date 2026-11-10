using System.Net.Mail;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Admin.UI;

/// <summary>Клиентские проверки форм администратора; совпадают с серверными валидаторами.</summary>
public static class AdminFormRules
{
    public const int MinPasswordLength = 6;

    public static string? EmailError(string? email, IStringLocalizer<AppStrings> loc)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value))
            return loc["AdminMgmt_Required"].Value;
        return MailAddress.TryCreate(value, out var parsed) && parsed.Address == value ? null : loc["AdminMgmt_EmailInvalid"].Value;
    }

    /// <summary>Случайный пароль без похожих символов (0/O, 1/l).</summary>
    public static string GeneratePassword(int length = 12)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        return string.Create(length, alphabet, static (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }
}

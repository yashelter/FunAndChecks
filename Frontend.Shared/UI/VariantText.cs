using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.UI;

/// <summary>Подписи вариантов, общие для страниц преподавателя и студента.</summary>
public static class VariantText
{
    public static string Uniqueness(IStringLocalizer<AppStrings> loc, VariantUniqueness value) => value switch
    {
        VariantUniqueness.UniqueInSubject => loc["Variants_UniqSubject"],
        VariantUniqueness.UniqueInGroup => loc["Variants_UniqGroup"],
        _ => loc["Variants_UniqAllowRepeats"],
    };

    public static string UniquenessHint(IStringLocalizer<AppStrings> loc, VariantUniqueness value) => value switch
    {
        VariantUniqueness.UniqueInSubject => loc["Variants_UniqSubjectHint"],
        VariantUniqueness.UniqueInGroup => loc["Variants_UniqGroupHint"],
        _ => loc["Variants_UniqAllowRepeatsHint"],
    };

    /// <summary>«Выбор открыт до …», «Срок прошёл» или «Выбор закрыт».</summary>
    public static string Status(IStringLocalizer<AppStrings> loc, bool isOpen, bool claimsOpen, DateTime? deadline)
    {
        if (isOpen)
            return deadline is { } until ? loc["Variants_StatusOpenUntil", Moscow(until)] : loc["Variants_StatusOpen"];
        return claimsOpen && deadline is { } passed ? loc["Variants_StatusExpired", Moscow(passed)] : loc["Variants_StatusClosed"];
    }

    /// <summary>«7» или «7 · Графы».</summary>
    public static string Label(int number, string? title) => string.IsNullOrWhiteSpace(title) ? $"{number}" : $"{number} · {title}";

    public static string Moscow(DateTime utc) => AttendanceStyles.Moscow(utc).ToString("dd.MM.yyyy HH:mm");
}

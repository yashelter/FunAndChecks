using System.Text.RegularExpressions;
using FluentValidation;

namespace FunAndChecks.Application.Common.Validation;

public static partial class ValidationRules
{
    [GeneratedRegex("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColorRegex();

    /// <summary>Цвет в hex-формате #RRGGBB или #RRGGBBAA (правило применяется, только если значение задано).</summary>
    public static IRuleBuilderOptions<T, string?> HexColor<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(color => color == null || HexColorRegex().IsMatch(color))
            .WithMessage("Color must be a hex value in #RRGGBB or #RRGGBBAA format.");
}

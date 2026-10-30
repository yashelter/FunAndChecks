using Frontend.Shared.Community;
using Frontend.Shared.UI;

namespace Frontend.Shared.Seasons;

/// <summary>
/// Зима: снегопад, гирлянда вдоль верхнего края, иней в углах, сугробы и снеговик; снежинка вместо значка.
/// </summary>
public sealed class WinterTheme : ISeasonalTheme
{
    public const string Key = "winter";

    public string Id => Key;
    public string Title => CommunityText.T("Зима", "Winter");
    public string Description => CommunityText.T("Снегопад, гирлянда, иней, сугробы и снеговик с 1 декабря по 30 января.", "Snowfall, a garland, frost, snowdrifts and a snowman from December 1 to January 30.");
    public SeasonalWindow Window { get; } = new(12, 1, 1, 30);
    public Type? Decorations => typeof(WinterAnimations);
    public Type? BrandMark => typeof(WinterBrandMark);
}

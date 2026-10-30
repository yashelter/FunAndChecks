using Frontend.Shared.Community;
using Frontend.Shared.UI;

namespace Frontend.Shared.Seasons;

/// <summary>
/// Хэллоуин: паутина, призраки, паук на нити, летучие мыши, туман и тыква-фонарь; тыква вместо значка
/// и на кнопке записи в очередь.
/// </summary>
public sealed class HalloweenTheme : ISeasonalTheme
{
    public const string Key = "halloween";

    public string Id => Key;
    public string Title => CommunityText.T("Хэллоуин", "Halloween");
    public string Description => CommunityText.T("Тыквы, призраки, паук, летучие мыши и паутина до начала ноября.", "Pumpkins, ghosts, a spider, bats and cobwebs until early November.");
    public SeasonalWindow Window { get; } = new(10, 20, 11, 2);
    public Type? Decorations => typeof(HalloweenAnimations);
    public Type? BrandMark => typeof(HalloweenBrandMark);
}

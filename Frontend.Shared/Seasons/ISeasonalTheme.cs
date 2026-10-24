namespace Frontend.Shared.Seasons;

/// <summary>
/// Сезонное оформление. Реализации находятся и регистрируются автоматически (<see cref="SeasonalThemeRegistration"/>):
/// чтобы добавить тему, достаточно класса с этим интерфейсом и, при желании, компонентов для украшений и значка.
/// Тема показывается в своё окно по дате, если пользователь не выключил её в настройках внешнего вида.
/// </summary>
public interface ISeasonalTheme
{
    /// <summary>Постоянный ключ: по нему хранится выбор пользователя и проверяется тема в разметке.</summary>
    string Id { get; }

    /// <summary>Название в настройках внешнего вида (на языке интерфейса).</summary>
    string Title { get; }

    /// <summary>Пояснение под переключателем в настройках.</summary>
    string Description { get; }

    /// <summary>Когда тема активна — каждый год в эти даты.</summary>
    SeasonalWindow Window { get; }

    /// <summary>Компонент поверх страницы (кабинеты и экран входа); null — без украшений.</summary>
    Type? Decorations { get; }

    /// <summary>Компонент вместо значка FunAndChecks в шапке; null — обычный значок.</summary>
    Type? BrandMark { get; }
}

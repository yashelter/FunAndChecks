using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Frontend.Shared.Seasons;

public static class SeasonalThemeRegistration
{
    /// <summary>
    /// Регистрирует все реализации <see cref="ISeasonalTheme"/> из сборки Frontend.Shared (и переданных сборок)
    /// списком, плюс <see cref="SeasonalThemes"/>, который выбирает из них активные по дате.
    /// </summary>
    public static IServiceCollection AddSeasonalThemes(this IServiceCollection services, params Assembly[] assemblies)
    {
        var sources = assemblies.Append(typeof(ISeasonalTheme).Assembly).Distinct();
        foreach (var type in sources.SelectMany(a => a.GetTypes())
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISeasonalTheme).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(ISeasonalTheme), type));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<SeasonalThemes>();
        return services;
    }
}

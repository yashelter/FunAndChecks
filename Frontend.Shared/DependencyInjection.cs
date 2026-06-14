using Frontend.Shared.Api;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Frontend.Shared;

public static class DependencyInjection
{
    /// <summary>
    /// Регистрирует общие сервисы фронтенда: MudBlazor, аутентификацию, HTTP-клиент к API
    /// и типизированные API-клиенты.
    /// </summary>
    public static IServiceCollection AddFrontendShared(this IServiceCollection services, string apiBaseAddress)
    {
        services.AddMudServices();

        // Хранение токенов и аутентификация.
        services.AddSingleton<TokenStore>();
        services.AddSingleton<TokenRefresher>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddScoped<AuthService>();
        services.AddScoped<ThemeService>();
        services.AddScoped<FileDownloader>();

        services.AddAuthorizationCore();
        services.AddScoped<JwtAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());

        // «Сырой» клиент без AuthHeaderHandler — для обновления токена (иначе рекурсия).
        services.AddHttpClient("ApiRaw", client => client.BaseAddress = new Uri(apiBaseAddress));

        // HTTP-клиент к API (тот же origin, что и SPA) с подстановкой Bearer-токена и авто-refresh.
        services.AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBaseAddress))
            .AddHttpMessageHandler<AuthHeaderHandler>();
        services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

        // Типизированные клиенты.
        services.AddScoped<MeApi>();
        services.AddScoped<StudentsApi>();
        services.AddScoped<SubjectsApi>();
        services.AddScoped<GroupsApi>();
        services.AddScoped<QueuesApi>();
        services.AddScoped<SubmissionsApi>();
        services.AddScoped<ResultsApi>();
        services.AddScoped<GradesApi>();
        services.AddScoped<AdminsApi>();
        services.AddScoped<BackupApi>();

        return services;
    }
}

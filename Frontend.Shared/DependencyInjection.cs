using Frontend.Shared.Api;
using Frontend.Shared.Services;
using Frontend.Shared.Profiles;
using Frontend.Shared.Community;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Frontend.Shared;

public static class DependencyInjection
{
    /// <summary>Клиент API с долгим таймаутом — для загрузки больших файлов (бэкапы).</summary>
    public const string LongRunningApiClient = "ApiLong";

    /// <summary>
    /// Регистрирует общие сервисы фронтенда: MudBlazor, аутентификацию, HTTP-клиент к API
    /// и типизированные API-клиенты.
    /// </summary>
    public static IServiceCollection AddFrontendShared(this IServiceCollection services, string apiBaseAddress)
    {
        services.AddLocalization();
        services.AddMudServices();

        // Хранение токенов и аутентификация.
        services.AddSingleton<TokenStore>();
        services.AddSingleton<TokenRefresher>();
        services.AddScoped<FeatureFlagsService>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddScoped<AuthService>();
        services.AddScoped<ThemeService>();
        services.AddScoped<DashboardAppearanceService>();
        services.AddScoped<FileDownloader>();
        services.AddScoped<CultureService>();
        services.AddScoped<UnsavedChangesTracker>();
        services.AddScoped<ConfirmService>();
        services.AddScoped<CurrentSubject>();
        services.AddScoped<ResultsNavigationState>();
        services.AddScoped<ProfileApi>();
        services.AddScoped<ProfileMediaCache>();
        services.AddScoped<AvatarDirectory>();
        services.AddScoped<CommunityRealtime>();
        services.AddScoped<ForumListState>();

        services.AddAuthorizationCore();
        services.AddScoped<JwtAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());

        // «Сырой» клиент без AuthHeaderHandler — для обновления токена (иначе рекурсия).
        services.AddHttpClient("ApiRaw", client => client.BaseAddress = new Uri(apiBaseAddress));

        // HTTP-клиент к API (тот же origin, что и SPA) с подстановкой Bearer-токена и авто-refresh.
        services.AddHttpClient("Api", client => client.BaseAddress = new Uri(apiBaseAddress))
            .AddHttpMessageHandler<AuthHeaderHandler>();
        services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));
        services.AddHttpClient(LongRunningApiClient, client =>
            {
                client.BaseAddress = new Uri(apiBaseAddress);
                client.Timeout = TimeSpan.FromMinutes(30);
            })
            .AddHttpMessageHandler<AuthHeaderHandler>();

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
        services.AddScoped<AttendanceApi>();
        services.AddScoped<NotificationApi>();
        services.AddScoped<ForumApi>();
        services.AddScoped<SurveyApi>();

        return services;
    }
}

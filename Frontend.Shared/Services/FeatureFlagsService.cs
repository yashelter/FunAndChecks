using System.Net.Http.Json;

namespace Frontend.Shared.Services;

/// <summary>
/// Feature flags client. Source of truth is the backend API configuration.
/// </summary>
public class FeatureFlagsService(HttpClient http)
{
    /// <summary>
    /// Privacy Policy feature flag. Значение приходит с API — источник истины там.
    /// Фолбэк до ответа и при ошибке — true: если сервер включил фичу, а ответ не дошёл,
    /// форма согласия всё равно нужна (сервер иначе отклонит регистрацию).
    /// </summary>
    public bool PrivacyPolicy { get; private set; } = true;

    /// <summary>
    /// Halloween event feature flag. Значение приходит с API — источник истины там.
    /// Фолбэк до ответа и при ошибке — false: тема сезонная и включается только явно.
    /// </summary>
    public bool HalloweenEvent { get; private set; } = false;

    public async Task LoadAsync()
    {
        try
        {
            var dto = await http.GetFromJsonAsync<FeaturesDto>("api/features");
            if (dto is not null)
            {
                PrivacyPolicy = dto.PrivacyPolicy;
                HalloweenEvent = dto.HalloweenEvent;
            }
        }
        catch
        {
            // Оставляем значение по умолчанию — см. поля выше.
        }
    }
}

public record FeaturesDto(bool PrivacyPolicy, bool HalloweenEvent);
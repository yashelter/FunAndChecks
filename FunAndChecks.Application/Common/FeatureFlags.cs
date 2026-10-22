namespace FunAndChecks.Application.Common;

/// <summary>
/// Feature flags configuration section.
/// </summary>
public class FeatureFlags
{
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// Privacy Policy feature: consent checkbox, policy page, consent endpoints.
    /// Выключено по умолчанию — включается в конфигурации развёртывания
    /// (FeatureFlags:PrivacyPolicy) после утверждения текста документов.
    /// </summary>
    public bool PrivacyPolicy { get; set; } = false;

    /// <summary>
    /// Halloween event: seasonal pumpkin theme for the queue sign-up button.
    /// Выключено по умолчанию — включается в конфигурации развёртывания
    /// (FeatureFlags:HalloweenEvent) на время праздника.
    /// </summary>
    public bool HalloweenEvent { get; set; } = false;
}

/// <summary>
/// DTO for exposing feature flags to the frontend.
/// </summary>
public record FeaturesDto(bool PrivacyPolicy, bool HalloweenEvent);
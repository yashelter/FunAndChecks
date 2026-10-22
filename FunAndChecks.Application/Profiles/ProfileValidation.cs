using FluentValidation;

namespace FunAndChecks.Application.Profiles;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(r => r.AboutMe).MaximumLength(2000);
        RuleFor(r => r.GitHubUrl).MaximumLength(500).Must(url => SafeUrl(url, true))
            .WithErrorCode("profile_url_invalid").WithMessage("Use an HTTPS link to GitHub.");
        RuleFor(r => r.PortfolioUrl).MaximumLength(500).Must(url => SafeUrl(url, false))
            .WithErrorCode("profile_url_invalid").WithMessage("Use an HTTPS link.");
        RuleFor(r => r.PinnedAwardIds).Must(ids => ids is null ||
            (ids.Count <= 3 && ids.All(id => id > 0) && ids.Distinct().Count() == ids.Count))
            .WithErrorCode("profile_pins_invalid").WithMessage("Choose at most three different awards.");
    }

    private static bool SafeUrl(string? value, bool github)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
            (!github || uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase));
    }
}

public class UpsertAchievementRequestValidator : AbstractValidator<UpsertAchievementRequest>
{
    public UpsertAchievementRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(2000);
        RuleFor(r => r).Must(r => r.MediaSessionId.HasValue == (r.Media is not null) &&
            (!r.MediaSessionId.HasValue || r.MediaSessionId != Guid.Empty) &&
            !(r.RemoveImage && r.MediaSessionId.HasValue))
            .WithErrorCode("media_request_invalid").WithMessage("Choose an image session and its edit settings together.");
    }
}

internal static class ProfileValidation
{
    public static void Page(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw new ValidationException("Page must be positive and page size must be between 1 and 100.");
    }
}

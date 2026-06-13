using FluentValidation;
using FunAndChecks.Application.Common.Validation;

namespace FunAndChecks.Application.Students;

public class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(x => x.Color).HexColor();
        RuleFor(x => x.GitHubUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.GitHubUrl))
            .WithMessage("GitHubUrl must be a valid absolute URL.");
    }
}

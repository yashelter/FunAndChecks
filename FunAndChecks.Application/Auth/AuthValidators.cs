using FluentValidation;
using FunAndChecks.Application.Common;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Application.Auth;

public class RegisterStudentRequestValidator : AbstractValidator<RegisterStudentRequest>
{
    public RegisterStudentRequestValidator(IOptions<FeatureFlags> flags)
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(128);
        RuleFor(x => x.GroupId).GreaterThan(0);
        
        if (flags.Value.PrivacyPolicy)
        {
            RuleFor(x => x.AcceptedPrivacyPolicy).Equal(true).WithMessage("Необходимо согласиться с политикой конфиденциальности");
            RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage("Необходимо принять Пользовательское соглашение");
        }
    }
}

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6).MaximumLength(128);
    }
}

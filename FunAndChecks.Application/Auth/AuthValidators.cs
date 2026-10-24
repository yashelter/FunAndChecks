using FluentValidation;

namespace FunAndChecks.Application.Auth;

public class RegisterStudentRequestValidator : AbstractValidator<RegisterStudentRequest>
{
    public RegisterStudentRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(128);
        RuleFor(x => x.GroupId).GreaterThan(0);
        // Пользовательское соглашение (licence/agreement.md) принимается всегда; одна галочка ставит обе отметки.
        RuleFor(x => x.AcceptedPrivacyPolicy).Equal(true).WithMessage("Необходимо принять пользовательское соглашение");
        RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage("Необходимо принять пользовательское соглашение");
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

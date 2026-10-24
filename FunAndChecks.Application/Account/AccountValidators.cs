using FluentValidation;

namespace FunAndChecks.Application.Account;

public class ChangeNameRequestValidator : AbstractValidator<ChangeNameRequest>
{
    public ChangeNameRequestValidator()
    {
        // Те же ограничения, что при регистрации.
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
    }
}

public class RequestEmailChangeRequestValidator : AbstractValidator<RequestEmailChangeRequest>
{
    public RequestEmailChangeRequestValidator()
    {
        RuleFor(x => x.NewEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
    }
}

public class ConfirmEmailChangeRequestValidator : AbstractValidator<ConfirmEmailChangeRequest>
{
    public ConfirmEmailChangeRequestValidator()
    {
        RuleFor(x => x.NewEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16);
    }
}

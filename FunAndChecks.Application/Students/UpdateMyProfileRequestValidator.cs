using FluentValidation;
using FunAndChecks.Application.Common.Validation;

namespace FunAndChecks.Application.Students;

public class SetStudentColorRequestValidator : AbstractValidator<SetStudentColorRequest>
{
    public SetStudentColorRequestValidator()
    {
        RuleFor(x => x.Color).HexColor();
    }
}

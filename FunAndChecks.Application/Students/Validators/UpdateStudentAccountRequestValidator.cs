using FluentValidation;
using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Students.Validators;

public class UpdateStudentAccountRequestValidator : AbstractValidator<UpdateStudentAccountRequest>
{
    public UpdateStudentAccountRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.LastName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

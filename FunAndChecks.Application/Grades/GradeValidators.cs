using FluentValidation;

namespace FunAndChecks.Application.Grades;

public class CreateGradeComponentRequestValidator : AbstractValidator<CreateGradeComponentRequest>
{
    public CreateGradeComponentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MinPoints).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPoints).GreaterThan(0).GreaterThanOrEqualTo(x => x.MinPoints);
    }
}

public class UpdateGradeComponentRequestValidator : AbstractValidator<UpdateGradeComponentRequest>
{
    public UpdateGradeComponentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MinPoints).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPoints).GreaterThan(0).GreaterThanOrEqualTo(x => x.MinPoints);
    }
}

public class SetGradeRequestValidator : AbstractValidator<SetGradeRequest>
{
    public SetGradeRequestValidator()
    {
        RuleFor(x => x.Points).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

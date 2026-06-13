using FluentValidation;

namespace FunAndChecks.Application.Tasks;

public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotNull();
        RuleFor(x => x.MaxPoints).GreaterThanOrEqualTo(0);
    }
}

public class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotNull();
        RuleFor(x => x.MaxPoints).GreaterThanOrEqualTo(0);
    }
}

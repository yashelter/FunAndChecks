using FluentValidation;

namespace FunAndChecks.Application.Queues;

public class CreateQueueEventRequestValidator : AbstractValidator<CreateQueueEventRequest>
{
    public CreateQueueEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SubjectId).GreaterThan(0);
    }
}

public class UpdateQueueEventRequestValidator : AbstractValidator<UpdateQueueEventRequest>
{
    public UpdateQueueEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

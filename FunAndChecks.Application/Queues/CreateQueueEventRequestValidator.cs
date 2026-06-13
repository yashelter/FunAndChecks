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

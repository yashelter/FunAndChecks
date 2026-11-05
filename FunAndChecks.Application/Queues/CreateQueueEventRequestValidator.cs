using FluentValidation;

namespace FunAndChecks.Application.Queues;

public class CreateQueueEventRequestValidator : AbstractValidator<CreateQueueEventRequest>
{
    public CreateQueueEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SubjectId).GreaterThan(0);
        RuleFor(x => x.LateAfterMinutes).InclusiveBetween(0, QueueLateness.MaxLateAfterMinutes)
            .When(x => x.LateAfterMinutes.HasValue);
    }
}

public class UpdateQueueEventRequestValidator : AbstractValidator<UpdateQueueEventRequest>
{
    public UpdateQueueEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public class SetQueueLateRuleRequestValidator : AbstractValidator<SetQueueLateRuleRequest>
{
    public SetQueueLateRuleRequestValidator()
    {
        RuleFor(x => x.LateAfterMinutes).InclusiveBetween(0, QueueLateness.MaxLateAfterMinutes)
            .When(x => x.LateAfterMinutes.HasValue);
    }
}

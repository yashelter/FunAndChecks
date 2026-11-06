using FluentValidation;

namespace FunAndChecks.Application.Queues;

public class CreateQueueEventRequestValidator : AbstractValidator<CreateQueueEventRequest>
{
    public CreateQueueEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SubjectId).GreaterThan(0);
        RuleFor(x => x.EventDateTime).NotEmpty().Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("Дата события должна передаваться в UTC.");
        RuleFor(x => x.LateAfterMinutes).InclusiveBetween(0, QueueLateness.MaxLateAfterMinutes)
            .When(x => x.LateAfterMinutes.HasValue);
    }
}

public class UpdateQueueEventRequestValidator : AbstractValidator<UpdateQueueEventRequest>
{
    public UpdateQueueEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EventDateTime).NotEmpty().Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("Дата события должна передаваться в UTC.");
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

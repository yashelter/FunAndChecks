using FluentValidation;

namespace FunAndChecks.Application.Variants;

public static class VariantLimits
{
    public const int MaxNumber = 9999;
    public const int MaxRange = 500;
}

public class SaveVariantSetRequestValidator : AbstractValidator<SaveVariantSetRequest>
{
    public SaveVariantSetRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Uniqueness).IsInEnum();
        RuleFor(x => x.ClaimDeadline).Must(d => d is null || d.Value.Kind == DateTimeKind.Utc)
            .WithMessage("Срок выбора должен передаваться в UTC.");
        RuleFor(x => x.RangeFrom).InclusiveBetween(1, VariantLimits.MaxNumber).When(x => x.RangeFrom is not null);
        RuleFor(x => x.RangeTo).NotNull().When(x => x.RangeFrom is not null);
        RuleFor(x => x.RangeTo).InclusiveBetween(1, VariantLimits.MaxNumber)
            .GreaterThanOrEqualTo(x => x.RangeFrom ?? 1)
            .LessThan(x => (x.RangeFrom ?? 1) + VariantLimits.MaxRange)
            .When(x => x.RangeTo is not null);
    }
}

public class AddVariantRequestValidator : AbstractValidator<AddVariantRequest>
{
    public AddVariantRequestValidator()
    {
        RuleFor(x => x.Number).InclusiveBetween(1, VariantLimits.MaxNumber);
        RuleFor(x => x.Title).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public class AddVariantRangeRequestValidator : AbstractValidator<AddVariantRangeRequest>
{
    public AddVariantRangeRequestValidator()
    {
        RuleFor(x => x.From).InclusiveBetween(1, VariantLimits.MaxNumber);
        RuleFor(x => x.To).InclusiveBetween(1, VariantLimits.MaxNumber).GreaterThanOrEqualTo(x => x.From)
            .LessThan(x => x.From + VariantLimits.MaxRange);
    }
}

public class UpdateVariantRequestValidator : AbstractValidator<UpdateVariantRequest>
{
    public UpdateVariantRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

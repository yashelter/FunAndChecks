using FluentValidation;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Surveys;

public class SaveSurveyRequestValidator : AbstractValidator<SaveSurveyRequest>
{
    public SaveSurveyRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Description).MaximumLength(4000);
        RuleFor(r => r.SnoozeHours).InclusiveBetween(1, 720);
        RuleFor(r => r.MaxSnoozes).InclusiveBetween(1, 20).When(r => r.MaxSnoozes.HasValue);
        RuleFor(r => r.TriggerType).IsInEnum();
        RuleFor(r => r).Must(r => r.OpensAtUtc is null || r.ClosesAtUtc is null || r.ClosesAtUtc > r.OpensAtUtc)
            .WithErrorCode("survey_window_invalid").WithMessage("The survey must close after it opens.");
        RuleFor(r => r.SubjectId).NotNull().When(r => r.TriggerType != SurveyTriggerType.Immediate)
            .WithMessage("A submission-based trigger needs a subject.");
        RuleFor(r => r.TriggerThreshold).InclusiveBetween(1, 1000).When(r => r.TriggerType != SurveyTriggerType.Immediate);
        RuleFor(r => r.TargetGroupIds).Must(ids => ids is null || (ids.Count <= 100 && ids.Distinct().Count() == ids.Count))
            .WithMessage("Choose different groups.");
        RuleFor(r => r.Questions).Must(q => q is null || q.Count <= SurveyRules.MaxQuestions)
            .WithMessage($"A survey can have at most {SurveyRules.MaxQuestions} questions.");
        RuleForEach(r => r.Questions).SetValidator(new SurveyQuestionInputValidator());
    }
}

public class SurveyQuestionInputValidator : AbstractValidator<SurveyQuestionInput>
{
    public SurveyQuestionInputValidator()
    {
        RuleFor(q => q.Text).NotEmpty().MaximumLength(500);
        RuleFor(q => q.Description).MaximumLength(1000);
        RuleFor(q => q.Type).IsInEnum();
        RuleFor(q => q.Options).Must(o => o is { Count: >= 2 and <= 30 } && o.All(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 300) &&
                o.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == o.Count)
            .When(q => SurveyRules.IsChoice(q.Type))
            .WithErrorCode("survey_options_invalid").WithMessage("A choice question needs 2–30 different options.");
        RuleFor(q => q).Must(q => q.ScaleMin is >= 0 and <= 1 && q.ScaleMax is >= 2 and <= 10)
            .When(q => q.Type == SurveyQuestionType.Scale)
            .WithErrorCode("survey_scale_invalid").WithMessage("A scale starts at 0 or 1 and ends at 2–10.");
        RuleFor(q => q).Must(q => q.ScaleMin is null || q.ScaleMax is null || q.ScaleMin < q.ScaleMax)
            .When(q => q.Type == SurveyQuestionType.Number)
            .WithErrorCode("survey_range_invalid").WithMessage("The minimum must be less than the maximum.");
        RuleFor(q => q.MaxLength).InclusiveBetween(1, 300).When(q => q.Type == SurveyQuestionType.ShortText && q.MaxLength.HasValue);
        RuleFor(q => q.MaxLength).InclusiveBetween(1, 4000).When(q => q.Type == SurveyQuestionType.LongText && q.MaxLength.HasValue);
        RuleFor(q => q.ScaleMinLabel).MaximumLength(100);
        RuleFor(q => q.ScaleMaxLabel).MaximumLength(100);
    }
}

public static class SurveyRules
{
    public const int MaxQuestions = 50;
    public const int ShortTextLimit = 300;
    public const int LongTextLimit = 4000;

    /// <summary>Сколько ответов нужно, чтобы показать детали анонимного опроса.</summary>
    public const int AnonymityThreshold = 3;

    /// <summary>
    /// Срез анонимных ответов (например, одна группа) можно показать, только если и он, и остаток (всё минус срез)
    /// либо пусты, либо не меньше порога: иначе «все» минус «группа A» восстанавливает ответы единственного человека из группы B.
    /// </summary>
    public static bool CanShowAnonymousSlice(int slice, int total)
    {
        var rest = total - slice;
        return slice >= AnonymityThreshold && (rest == 0 || rest >= AnonymityThreshold);
    }

    public static bool IsChoice(SurveyQuestionType type) =>
        type is SurveyQuestionType.SingleChoice or SurveyQuestionType.MultipleChoice or SurveyQuestionType.Dropdown;
}

using FluentValidation;

namespace FunAndChecks.Application.Attendance;

public class CreateAttendanceSessionRequestValidator : AbstractValidator<CreateAttendanceSessionRequest>
{
    public CreateAttendanceSessionRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.StartsAt).NotEmpty().Must(d => d.Kind == DateTimeKind.Utc)
            .WithMessage("Дата занятия должна передаваться в UTC.");
        RuleFor(x => x.GroupIds).NotEmpty().Must(ids => ids is null || ids.Count <= 100);
        RuleForEach(x => x.GroupIds).GreaterThan(0);
    }
}

public class SetAttendanceRequestValidator : AbstractValidator<SetAttendanceRequest>
{
    public SetAttendanceRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Version).NotEmpty();
    }
}

public class RemainingAttendanceRequestValidator : AbstractValidator<RemainingAttendanceRequest>
{
    public RemainingAttendanceRequestValidator()
    {
        RuleFor(x => x.Students).NotEmpty().Must(s => s is null || s.Count <= 5000);
        RuleForEach(x => x.Students).ChildRules(student =>
        {
            student.RuleFor(x => x.StudentId).NotEmpty();
            student.RuleFor(x => x.Version).NotEmpty();
        });
    }
}

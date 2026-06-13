using FluentValidation;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Submissions;

public class CreateSubmissionRequestValidator : AbstractValidator<CreateSubmissionRequest>
{
    public CreateSubmissionRequestValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.TaskId).GreaterThan(0);
        RuleFor(x => x.Status)
            .IsInEnum()
            .NotEqual(SubmissionStatus.NotSubmitted)
            .WithMessage("Submission must be either Accepted or Rejected.");
    }
}

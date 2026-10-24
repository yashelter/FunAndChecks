using FluentValidation;
using FunAndChecks.Application.Attachments;

namespace FunAndChecks.Application.Forum;

public class CreateForumTopicRequestValidator : AbstractValidator<CreateForumTopicRequest>
{
    public CreateForumTopicRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().Must(t => t!.Trim().Length >= 3).WithMessage("The title must have at least 3 characters.")
            .MaximumLength(200);
        RuleFor(r => r.Body).NotEmpty().MaximumLength(ForumValidation.MaxBody);
        RuleFor(r => r.AttachmentIds).Must(ForumValidation.ValidAttachments)
            .WithErrorCode("forum_attachments_invalid").WithMessage($"Attach at most {AttachmentContent.MaxPerPost} different files.");
    }
}

public class UpdateForumTopicRequestValidator : AbstractValidator<UpdateForumTopicRequest>
{
    public UpdateForumTopicRequestValidator()
    {
        RuleFor(r => r.Title).Must(t => t is null || t.Trim().Length >= 3)
            .WithMessage("The title must have at least 3 characters.").MaximumLength(200);
    }
}

public class CreateForumPostRequestValidator : AbstractValidator<CreateForumPostRequest>
{
    public CreateForumPostRequestValidator()
    {
        RuleFor(r => r.Body).MaximumLength(ForumValidation.MaxBody);
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.Body) || r.AttachmentIds is { Count: > 0 })
            .WithErrorCode("forum_post_empty").WithMessage("Write a message or attach a file.");
        RuleFor(r => r.AttachmentIds).Must(ForumValidation.ValidAttachments)
            .WithErrorCode("forum_attachments_invalid").WithMessage($"Attach at most {AttachmentContent.MaxPerPost} different files.");
    }
}

public class UpdateForumPostRequestValidator : AbstractValidator<UpdateForumPostRequest>
{
    public UpdateForumPostRequestValidator()
    {
        RuleFor(r => r.Body).MaximumLength(ForumValidation.MaxBody);
        RuleFor(r => r).Must(r => !string.IsNullOrWhiteSpace(r.Body) || r.AttachmentIds is { Count: > 0 })
            .WithErrorCode("forum_post_empty").WithMessage("Write a message or attach a file.");
        RuleFor(r => r.AttachmentIds).Must(ForumValidation.ValidAttachments)
            .WithErrorCode("forum_attachments_invalid").WithMessage($"Attach at most {AttachmentContent.MaxPerPost} different files.");
    }
}

public class ReportForumPostRequestValidator : AbstractValidator<ReportForumPostRequest>
{
    public ReportForumPostRequestValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
}

public class BanForumUserRequestValidator : AbstractValidator<BanForumUserRequest>
{
    public BanForumUserRequestValidator()
    {
        RuleFor(r => r.UntilUtc).Must(d => d > DateTime.UtcNow).WithMessage("Choose a moment in the future.")
            .Must(d => d < DateTime.UtcNow.AddYears(2)).WithMessage("A ban can last at most two years.");
        RuleFor(r => r.Reason).MaximumLength(500);
    }
}

internal static class ForumValidation
{
    public const int MaxBody = 10000;

    public static bool ValidAttachments(IReadOnlyList<Guid>? ids) =>
        ids is null || (ids.Count <= AttachmentContent.MaxPerPost && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count);
}

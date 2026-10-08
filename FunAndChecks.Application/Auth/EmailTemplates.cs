using System.Globalization;
using System.Resources;

namespace FunAndChecks.Application.Auth;

public enum EmailTemplateKind { Confirmation, PasswordReset, ExistingAccount }

public sealed record EmailTemplate(string Subject, string HtmlBody);

public interface IEmailTemplateRenderer
{
    EmailTemplate Render(EmailTemplateKind kind, string culture, string? code = null);
}

internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly ResourceManager Resources = new(
        "FunAndChecks.Application.Auth.EmailTemplateResources", typeof(EmailTemplateRenderer).Assembly);

    public EmailTemplate Render(EmailTemplateKind kind, string culture, string? code = null)
    {
        var selected = culture == "ru-RU" ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.GetCultureInfo("en-US");
        var prefix = kind switch
        {
            EmailTemplateKind.Confirmation => "Confirmation",
            EmailTemplateKind.PasswordReset => "PasswordReset",
            EmailTemplateKind.ExistingAccount => "ExistingAccount",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var subject = Resources.GetString($"{prefix}Subject", selected)
                      ?? throw new InvalidOperationException($"Missing email resource {prefix}Subject.");
        var body = Resources.GetString($"{prefix}Body", selected)
                   ?? throw new InvalidOperationException($"Missing email resource {prefix}Body.");
        return new EmailTemplate(subject, code is null ? body : string.Format(selected, body, code));
    }
}

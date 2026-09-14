using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using Frontend.Shared.Resources;
using FunAndChecks.Application;
using FunAndChecks.Application.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests;

public partial class LocalizationResourcesTests
{
    [Fact]
    public void FrontendResources_HaveMatchingKeysAndPlaceholders()
    {
        var resources = new ResourceManager("Frontend.Shared.Resources.AppStrings", typeof(AppStrings).Assembly);
        AssertResourceParity(resources);
    }

    [Fact]
    public void EmailResources_HaveMatchingKeysAndPlaceholders()
    {
        var resources = new ResourceManager(
            "FunAndChecks.Application.Auth.EmailTemplateResources", typeof(IEmailTemplateRenderer).Assembly);
        AssertResourceParity(resources);
    }

    [Fact]
    public void EmailRenderer_UsesRequestedCulture_AndPreservesSecurityCode()
    {
        using var services = new ServiceCollection().AddApplication().BuildServiceProvider();
        var renderer = services.GetRequiredService<IEmailTemplateRenderer>();

        var english = renderer.Render(EmailTemplateKind.PasswordReset, "en-US", "123456");
        var russian = renderer.Render(EmailTemplateKind.PasswordReset, "ru-RU", "123456");

        english.Subject.Should().NotBe(russian.Subject);
        english.HtmlBody.Should().Contain("123456");
        russian.HtmlBody.Should().Contain("123456");
    }

    private static void AssertResourceParity(ResourceManager manager)
    {
        var english = Read(manager, CultureInfo.GetCultureInfo("en-US"));
        var russian = Read(manager, CultureInfo.GetCultureInfo("ru-RU"));

        russian.Keys.Should().BeEquivalentTo(english.Keys);
        foreach (var key in english.Keys)
        {
            var englishPlaceholders = PlaceholderRegex().Matches(english[key]).Select(m => m.Value);
            var russianPlaceholders = PlaceholderRegex().Matches(russian[key]).Select(m => m.Value);
            russianPlaceholders.Should().Equal(englishPlaceholders, $"resource '{key}' must accept the same arguments");
        }
    }

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture) =>
        manager.GetResourceSet(culture, createIfNotExists: true, tryParents: true)!
            .Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

    [GeneratedRegex(@"\{\d+(?:[^}]*)\}")]
    private static partial Regex PlaceholderRegex();
}

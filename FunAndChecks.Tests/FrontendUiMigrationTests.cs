using FluentAssertions;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace FunAndChecks.Tests;

public class FrontendUiMigrationTests
{
    [Theory]
    [InlineData("/", "/legacy")]
    [InlineData("/admin/queues/12?subjectId=3#student", "/legacy/admin/queues/12?subjectId=3#student")]
    [InlineData("/legacy", "/")]
    [InlineData("/legacy/student/attendance?subjectId=7", "/student/attendance?subjectId=7")]
    [InlineData("/legacy-old", "/legacy/legacy-old")]
    public void SwitchVersion_PreservesPageAndParameters(string current, string target) =>
        UiRoutes.OtherVersion(current).Should().Be(target);

    [Theory]
    [InlineData("//external.example/admin")]
    [InlineData("https://external.example/admin")]
    [InlineData("/\\external.example/admin")]
    public void SwitchVersion_RejectsExternalDestinations(string current)
    {
        Action action = () => UiRoutes.OtherVersion(current);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Legacy_HasIndependentEquivalentForEveryWorkingPage()
    {
        var workingAssemblies = new[] { typeof(Frontend.App).Assembly, typeof(Frontend.Admin.AdminAssemblyMarker).Assembly,
            typeof(Frontend.Student.StudentAssemblyMarker).Assembly, typeof(Frontend.Shared.Pages.Login).Assembly };
        var legacyTypes = typeof(Frontend.Legacy.LegacyAssemblyMarker).Assembly.GetTypes();
        var legacyRoutes = legacyTypes.SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(a => a.Template).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var type in workingAssemblies.SelectMany(a => a.GetTypes()).Distinct())
        foreach (var route in type.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
        {
            var equivalent = route.Template == "/" ? "/legacy" : "/legacy" + route.Template;
            legacyRoutes.Should().Contain(equivalent, $"{type.FullName} needs a preserved legacy counterpart");
        }
    }

    [Theory]
    [InlineData("#ffffff", "#ffffff")]
    [InlineData("#000000", "#111d2e")]
    [InlineData("#ffff00", "#ffffff")]
    [InlineData("#13896e", "#f4f7fb")]
    [InlineData("#7f7f7f", "#f4f7fb")]
    [InlineData("#ff5599", "#111d2e")]
    public void CustomAccent_IsReadableInBothThemes(string color, string background)
    {
        var adjusted = DashboardAppearanceService.ReadableAccent(color, background);
        DashboardAppearanceService.Contrast(adjusted, background).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [InlineData("#12abcd", "#12abcd")]
    [InlineData(" B5A0FF ", "#b5a0ff")]
    [InlineData("var(--accent)", null)]
    [InlineData("#fff", null)]
    [InlineData("#00ff00;display:none", null)]
    public void Accent_AcceptsOnlyFullHexColors(string input, string? expected) =>
        DashboardAppearanceService.NormalizeAccent(input).Should().Be(expected);

    [Fact]
    public void Accent_PreservesSemanticStatusColors()
    {
        var mint = DashboardAppearanceService.CreateTheme("#66e4c1");
        var purple = DashboardAppearanceService.CreateTheme("#b5a0ff");
        purple.PaletteDark.Success.Should().Be(mint.PaletteDark.Success);
        purple.PaletteDark.Warning.Should().Be(mint.PaletteDark.Warning);
        purple.PaletteDark.Error.Should().Be(mint.PaletteDark.Error);
        purple.PaletteLight.Success.Should().Be(mint.PaletteLight.Success);
        purple.PaletteDark.Primary.Should().NotBe(mint.PaletteDark.Primary);
    }
}

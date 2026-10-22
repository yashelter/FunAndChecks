using FluentAssertions;
using Frontend.Shared.Services;
using Xunit;

namespace FunAndChecks.Tests;

public class FrontendAppearanceTests
{
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

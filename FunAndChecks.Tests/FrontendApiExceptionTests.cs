using System.Net;
using System.Text;
using FluentAssertions;
using Frontend.Shared.Api;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests;

public class FrontendApiExceptionTests
{
    [Fact]
    public async Task EnsureSuccessAsync_LegacyProblemDetails_FallsBackToDetailAndErrors()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"detail":"Legacy message","errors":{"Name":["Legacy field error"]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();

        var act = () => response.EnsureSuccessAsync(localizer);

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.Message.Should().Be("Legacy message");
        exception.Which.ValidationErrors["Name"].Should().Equal("Legacy field error");
    }

    [Fact]
    public async Task EnsureSuccessAsync_CatalogCode_ExposesCodeAndLocalizesMessage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"detail":"Email is not confirmed.","code":"auth.email_not_confirmed"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        localizer["Error_EmailNotConfirmed"].Returns(new LocalizedString("Error_EmailNotConfirmed", "Подтвердите email"));

        var act = () => response.EnsureSuccessAsync(localizer);

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.Code.Should().Be("auth.email_not_confirmed");
        exception.Which.Message.Should().Be("Подтвердите email");
    }

    [Fact]
    public async Task ReadErrorInfoAsync_UnknownCode_ReturnsMessageWithCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"detail":"Points must be between 0 and 100.","code":"grades.out_of_range"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();

        var (message, code) = await response.ReadErrorInfoAsync(localizer);

        message.Should().Be("Points must be between 0 and 100.");
        code.Should().Be("grades.out_of_range");
    }

    [Fact]
    public async Task CatalogCode_WithArrayArg_JoinsValuesWithComma()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"detail":"Groups are not linked.","code":"queue.autofill_group_not_enrolled","args":{"subjectId":7,"groupIds":["A-101","B-202"]}}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        var localizer = Substitute.For<IStringLocalizer<AppStrings>>();
        localizer["Error_AutofillGroupNotEnrolled"].Returns(
            new LocalizedString("Error_AutofillGroupNotEnrolled", "Группы {0} не привязаны к этому предмету."));

        var (message, _) = await response.ReadErrorInfoAsync(localizer);

        message.Should().Be("Группы A-101, B-202 не привязаны к этому предмету.");
    }
}

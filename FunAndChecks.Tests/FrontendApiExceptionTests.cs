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
}

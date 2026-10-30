using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Students;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

/// <summary>
/// Снимок цветов для живой подписки таблицы: доступен там же, где сама таблица (публичная страница результатов),
/// и отдаёт тех же студентов, не перекачивая всю таблицу.
/// </summary>
[Collection("Integration")]
public class ResultsColorsFlowTests(TestWebAppFactory factory)
{
    [Fact]
    public async Task Colors_AreServedWhereverTheTableIs_ForTheSameStudents()
    {
        int subjectId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var group = db.Group("M8O-COLORS");
            var subject = db.Subject("Цвета");
            await db.SaveChangesAsync();
            db.LinkGroupSubject(group, subject);
            db.Student(group, "Red").Color = "#ff0000";
            db.Student(group, "Plain");
            await db.SaveChangesAsync();
            subjectId = subject.Id;
        }

        using var client = factory.CreateClient();
        var table = await client.GetFromJsonAsync<SubjectResultsDto>($"/api/results/subjects/{subjectId}");
        var colors = await client.GetFromJsonAsync<List<StudentColorUpdateDto>>($"/api/results/subjects/{subjectId}/colors");

        colors.Should().BeEquivalentTo(table!.UserResults.Select(r => new StudentColorUpdateDto(r.StudentId, r.StudentColor)));
        colors.Should().Contain(c => c.Color == "#ff0000");
        (await client.GetAsync("/api/results/subjects/987654/colors")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

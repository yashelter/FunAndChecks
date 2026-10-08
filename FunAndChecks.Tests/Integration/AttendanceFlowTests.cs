using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using FunAndChecks.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class AttendanceFlowTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task AdminCanEnableCreateMarkExportAndDisable_StudentCanOnlyReadOwnHistory()
    {
        var data = await SeedAsync();
        using var admin = Client(data.AdminToken);
        using var student = Client(data.StudentToken);
        var subjectUrl = $"/api/attendance/subjects/{data.SubjectId}";
        (await admin.PutAsJsonAsync($"{subjectUrl}/settings", new AttendanceSettingsDto(true))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var create = await admin.PostAsJsonAsync($"{subjectUrl}/sessions",
            new CreateAttendanceSessionRequest("Практика", new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc), [data.GroupId]));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await create.Content.ReadFromJsonAsync<AttendanceSessionDto>())!;
        var sessionUrl = $"/api/attendance/sessions/{session.Id}";
        var details = (await admin.GetFromJsonAsync<AttendanceSessionDetailsDto>(sessionUrl))!;
        var markUrl = $"{sessionUrl}/students/{data.StudentId}";
        var originalVersion = details.Participants.Single(p => p.StudentId == data.StudentId).Version;
        (await admin.PutAsJsonAsync(markUrl, new SetAttendanceRequest(AttendanceStatus.Present, originalVersion))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync(markUrl, new SetAttendanceRequest(AttendanceStatus.Absent, originalVersion))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await student.PutAsJsonAsync(markUrl, new SetAttendanceRequest(AttendanceStatus.Absent, originalVersion))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.GetAsync($"{subjectUrl}/journal")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.GetAsync($"{subjectUrl}/export")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var own = (await student.GetFromJsonAsync<List<StudentAttendanceDto>>($"/api/me/attendance/subjects/{data.SubjectId}"))!;
        own.Should().ContainSingle().Which.Status.Should().Be(AttendanceStatus.Present);

        using var export = await admin.GetAsync($"{subjectUrl}/export?groupId={data.GroupId}&from=2026-10-05&to=2026-10-05");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        export.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var stream = new MemoryStream(await export.Content.ReadAsByteArrayAsync());
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Посещаемость");
        sheet.Cell(4, 3).GetString().Should().Contain("05.10.2026 10:00").And.Contain("Практика");
        sheet.Cell(5, 3).GetString().Should().Be("✓");
        sheet.Cell(5, 4).GetValue<int>().Should().Be(1);
        sheet.Cell(5, 5).GetValue<int>().Should().Be(0);
        sheet.Cell(6, 3).GetString().Should().Be("—");
        sheet.Cell(6, 5).GetValue<int>().Should().Be(0);
        sheet.Cell(6, 6).GetValue<int>().Should().Be(1);

        (await admin.PutAsJsonAsync($"{subjectUrl}/settings", new AttendanceSettingsDto(false))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"{subjectUrl}/journal")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"{subjectUrl}/export")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.GetFromJsonAsync<List<StudentAttendanceDto>>($"/api/me/attendance/subjects/{data.SubjectId}"))!.Should().ContainSingle();
        (await admin.PostAsJsonAsync($"{subjectUrl}/sessions", new CreateAttendanceSessionRequest(null, DateTime.UtcNow, [data.GroupId])))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AnonymousRequestsAndSubjectRestrictedAdmin_CannotReadJournalOrExport()
    {
        var data = await SeedAsync();
        using var anonymous = _factory.CreateClient();
        var url = $"/api/attendance/subjects/{data.SubjectId}";
        (await anonymous.GetAsync($"{url}/journal")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{url}/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var scope = _factory.Services.CreateScope())
        {
            var access = scope.ServiceProvider.GetRequiredService<FunAndChecks.Application.Admins.IAdminAccessService>();
            await access.SetSubjectRestrictedAsync(data.AdminId, data.SubjectId, true);
        }
        using var admin = Client(data.AdminToken);
        (await admin.GetAsync($"{url}/journal")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync($"{url}/export")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid AdminId, int SubjectId, int GroupId, Guid StudentId, string AdminToken, string StudentToken)> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin();
        var subject = db.Subject("Программирование");
        var group = db.Group();
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group, "Alpha");
        db.Student(group, "Beta");
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student)).Succeeded.Should().BeTrue();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return (admin.Id, subject.Id, group.Id, student.Id,
            await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(student.Id));
    }

    public void Dispose() => _factory.Dispose();
}

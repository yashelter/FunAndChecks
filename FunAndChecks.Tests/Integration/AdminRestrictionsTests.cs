using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

// Запуск хостов последовательно: Program использует общий Serilog.Log.Logger.
[Collection("Integration")]
public class AdminRestrictionsTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task SubjectRestriction_HidesQueuesAndBlocksOperations_UntilRemoved()
    {
        var data = await SeedAsync();
        using var admin = CreateClient(data.AdminToken);
        using var superAdmin = CreateClient(data.SuperAdminToken);
        var restrictionUrl = $"/api/admins/{data.AdminId}/subjects/{data.SubjectId}/restriction";

        (await superAdmin.PutAsJsonAsync(restrictionUrl, new { restricted = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var active = await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues");
        active!.Select(q => q.Id).Should().Equal(data.OtherEventId);
        var all = await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues/all");
        all!.Select(q => q.Id).Should().Equal(data.OtherEventId);

        (await admin.GetAsync($"/api/queues/{data.EventId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/queues",
            new CreateQueueEventRequest("Forbidden", DateTime.UtcNow.AddDays(1), data.SubjectId)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/queues/{data.EventId}",
            new UpdateQueueEventRequest("Forbidden", DateTime.UtcNow.AddDays(2))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/queues/{data.EventId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/queues/{data.EventId}/students/{data.StudentId}/status",
            new UpdateQueueStatusRequest(QueueEntryStatus.Checking)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Ограничение персональное: другой админ и студент по-прежнему видят очередь.
        (await superAdmin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues/all"))!
            .Should().HaveCount(3);
        using var student = CreateClient(data.StudentToken);
        (await student.GetAsync($"/api/queues/{data.EventId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.QueueEvents.CountAsync()).Should().Be(3);
            (await db.QueueEvents.FindAsync(data.EventId))!.Name.Should().Be("Current");
        }

        // Снятие запрета начинает действовать с тем же JWT, без повторного входа.
        (await superAdmin.PutAsJsonAsync(restrictionUrl, new { restricted = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues"))!
            .Select(q => q.Id).Should().BeEquivalentTo([data.EventId, data.OtherEventId]);
        (await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues/all"))!
            .Should().HaveCount(3);
        (await admin.GetAsync($"/api/queues/{data.EventId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/queues",
            new CreateQueueEventRequest("Allowed", DateTime.UtcNow.AddDays(1), data.SubjectId)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PutAsJsonAsync($"/api/queues/{data.EventId}",
            new UpdateQueueEventRequest("Allowed", DateTime.UtcNow.AddDays(2))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"/api/queues/{data.EventId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupRestriction_KeepsParticipantsVisible_ButBlocksReceivingAndGrading(bool existingGrade)
    {
        var data = await SeedAsync();
        using var admin = CreateClient(data.AdminToken);
        using var superAdmin = CreateClient(data.SuperAdminToken);
        var gradeUrl = $"/api/grade-components/{data.ComponentId}/students/{data.StudentId}";
        if (existingGrade)
            (await admin.PutAsJsonAsync(gradeUrl, new SetGradeRequest(25, "Original")))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var restrictionUrl = $"/api/admins/{data.AdminId}/groups/{data.GroupId}/restriction";
        (await superAdmin.PutAsJsonAsync(restrictionUrl, new { restricted = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues"))!
            .Should().Contain(q => q.Id == data.EventId);
        var details = await admin.GetFromJsonAsync<QueueDetailsDto>($"/api/queues/{data.EventId}");
        details!.Participants.Should().HaveCount(2);
        details.Participants.Single(p => p.StudentId == data.StudentId).CanManage.Should().BeFalse();
        details.Participants.Single(p => p.StudentId == data.AllowedStudentId).CanManage.Should().BeTrue();

        foreach (var status in Enum.GetValues<QueueEntryStatus>())
            (await admin.PutAsJsonAsync($"/api/queues/{data.EventId}/students/{data.StudentId}/status",
                new UpdateQueueStatusRequest(status)))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var status in new[] { SubmissionStatus.Accepted, SubmissionStatus.Rejected })
            (await admin.PostAsJsonAsync("/api/submissions",
                new CreateSubmissionRequest(data.StudentId, data.TaskId, status, "Result")))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync(gradeUrl, new SetGradeRequest(50, "Forbidden")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        if (existingGrade)
            (await admin.DeleteAsync(gradeUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entry = await db.QueueEntries.SingleAsync(e => e.StudentId == data.StudentId);
            entry.Status.Should().Be(QueueEntryStatus.Waiting);
            entry.CurrentAdminId.Should().BeNull();
            (await db.Submissions.CountAsync()).Should().Be(0);
            var grade = await db.StudentGrades.SingleOrDefaultAsync(g => g.StudentId == data.StudentId);
            if (existingGrade)
                grade!.Points.Should().Be(25);
            else
                grade.Should().BeNull();
        }

        // Разрешённую группу можно принимать и оценивать в той же очереди.
        (await admin.PutAsJsonAsync($"/api/queues/{data.EventId}/students/{data.AllowedStudentId}/status",
            new UpdateQueueStatusRequest(QueueEntryStatus.Checking)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync("/api/submissions",
            new CreateSubmissionRequest(data.AllowedStudentId, data.TaskId, SubmissionStatus.Accepted, null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync($"/api/grade-components/{data.ComponentId}/students/{data.AllowedStudentId}",
            new SetGradeRequest(50, null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await superAdmin.PutAsJsonAsync(restrictionUrl, new { restricted = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        details = await admin.GetFromJsonAsync<QueueDetailsDto>($"/api/queues/{data.EventId}");
        details!.Participants.Single(p => p.StudentId == data.StudentId).CanManage.Should().BeTrue();
        (await admin.PutAsJsonAsync($"/api/queues/{data.EventId}/students/{data.StudentId}/status",
            new UpdateQueueStatusRequest(QueueEntryStatus.Checking)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync("/api/submissions",
            new CreateSubmissionRequest(data.StudentId, data.TaskId, SubmissionStatus.Accepted, null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PutAsJsonAsync(gradeUrl, new SetGradeRequest(50, null)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync(gradeUrl)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<TestData> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin();
        var superAdmin = db.Admin("S");
        var subject = db.Subject();
        var otherSubject = db.Subject("Physics");
        var group = db.Group();
        var otherGroup = db.Group("Other");
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        db.LinkGroupSubject(otherGroup, subject);
        var student = db.Student(group);
        var allowedStudent = db.Student(otherGroup);
        var task = db.Task(subject);
        var component = db.Component(subject);
        var current = new QueueEvent { Name = "Current", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow.AddDays(1) };
        var past = new QueueEvent { Name = "Past", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow.AddDays(-5) };
        var other = new QueueEvent { Name = "Other", SubjectId = otherSubject.Id, EventDateTime = DateTime.UtcNow.AddDays(1) };
        db.QueueEvents.AddRange(current, past, other);
        await db.SaveChangesAsync();
        foreach (var participant in new[] { student, allowedStudent })
            db.QueueEntries.Add(new QueueEntry
            {
                QueueEventId = current.Id,
                StudentId = participant.Id,
                JoinedAt = DateTime.UtcNow,
                Status = QueueEntryStatus.Waiting,
            });
        await db.SaveChangesAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin))
            .Succeeded.Should().BeTrue();
        (await users.AddToRolesAsync((await users.FindByIdAsync(superAdmin.Id.ToString()))!, [Roles.Admin, Roles.SuperAdmin]))
            .Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student))
            .Succeeded.Should().BeTrue();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new TestData(admin.Id, subject.Id, group.Id, student.Id, allowedStudent.Id, task.Id,
            component.Id, current.Id, other.Id, await tokens.CreateTokenAsync(admin.Id),
            await tokens.CreateTokenAsync(superAdmin.Id), await tokens.CreateTokenAsync(student.Id));
    }

    private HttpClient CreateClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public void Dispose() => _factory.Dispose();

    private record TestData(Guid AdminId, int SubjectId, int GroupId, Guid StudentId, Guid AllowedStudentId,
        int TaskId, int ComponentId, int EventId, int OtherEventId, string AdminToken, string SuperAdminToken,
        string StudentToken);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Queues;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

/// <summary>Ручной архив очереди через API: права, выборки current/archive, детали по прямой ссылке, запрет записи.</summary>
[Collection("Integration")]
public class QueueArchiveFlowTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task ArchivedQueue_MovesFromCurrentToArchive_ForBothRoles_AndStaysReadable()
    {
        var data = await SeedAsync();
        using var admin = CreateClient(data.AdminToken);
        using var stranger = CreateClient(data.StrangerToken);
        using var student = CreateClient(data.StudentToken);

        // Преподаватель без доступа к предмету архивировать не может.
        (await stranger.PostAsync($"/api/queues/{data.QueueId}/archive", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var response = await admin.PostAsync($"/api/queues/{data.QueueId}/archive", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<QueueEventDto>())!.ArchivedAt.Should().NotBeNull();

        (await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues"))!.Should().NotContain(e => e.Id == data.QueueId);
        (await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues?scope=archive"))!
            .Should().ContainSingle(e => e.Id == data.QueueId && e.ArchivedAt != null);
        // Клиенты «всех» очередей (Управление) видят и архив.
        (await admin.GetFromJsonAsync<List<QueueEventDto>>("/api/queues/all"))!.Should().Contain(e => e.Id == data.QueueId);

        (await student.GetFromJsonAsync<List<QueueEventDto>>("/api/me/queue-events"))!.Should().BeEmpty();
        (await student.GetFromJsonAsync<List<QueueEventDto>>("/api/me/available-queue-events"))!.Should().BeEmpty();
        (await student.GetFromJsonAsync<List<QueueEventDto>>("/api/me/queue-events?scope=archive"))!
            .Should().ContainSingle(e => e.Id == data.QueueId);
        // Старый includePast=true по-прежнему означает «все».
        (await student.GetFromJsonAsync<List<QueueEventDto>>("/api/me/queue-events?includePast=true"))!
            .Should().ContainSingle(e => e.Id == data.QueueId);

        // По прямой ссылке архив открывается, но запись закрыта.
        (await student.GetFromJsonAsync<QueueDetailsDto>($"/api/queues/{data.QueueId}"))!.ArchivedAt.Should().NotBeNull();
        var join = await student.PostAsync($"/api/queues/{data.QueueId}/join", null);
        join.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await join.Content.ReadAsStringAsync()).Should().Contain("queue.archived");
    }

    private sealed record TestData(int QueueId, string AdminToken, string StrangerToken, string StudentToken);

    private async Task<TestData> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin();
        var stranger = db.Admin("B");
        var subject = db.Subject();
        db.GrantSubject(admin, subject);
        var group = db.Group();
        await db.SaveChangesAsync();
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group);
        var queue = new QueueEvent { Name = "Labs", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow.AddHours(2) };
        db.QueueEvents.Add(queue);
        await db.SaveChangesAsync();
        db.QueueEntries.Add(new QueueEntry
        {
            QueueEventId = queue.Id, StudentId = student.Id, JoinedAt = DateTime.UtcNow, Status = QueueEntryStatus.Finished,
        });
        await db.SaveChangesAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (id, role) in new[] { (admin.Id, Roles.Admin), (stranger.Id, Roles.Admin), (student.Id, Roles.Student) })
            (await users.AddToRoleAsync((await users.FindByIdAsync(id.ToString()))!, role)).Succeeded.Should().BeTrue();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new TestData(queue.Id, await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(stranger.Id),
            await tokens.CreateTokenAsync(student.Id));
    }

    private HttpClient CreateClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public void Dispose() => _factory.Dispose();
}

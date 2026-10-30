using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

/// <summary>Зарегистрировавшийся после создания авто-очереди студент попадает в неё при подтверждении почты.</summary>
[Collection("Integration")]
public class QueueAutoFillFlowTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task StudentRegisteredAfterAutoQueue_JoinsItOnEmailConfirmation()
    {
        int groupId, queueId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var group = db.Group("M8O-301");
            var subject = db.Subject("Сети");
            await db.SaveChangesAsync();
            db.LinkGroupSubject(group, subject);
            var queue = new QueueEvent
            {
                Name = "Защита", EventDateTime = DateTime.UtcNow.AddDays(1), SubjectId = subject.Id,
                AllowSelfJoin = false, AutoFillGroups = [group],
            };
            db.QueueEvents.Add(queue);
            await db.SaveChangesAsync();
            (groupId, queueId) = (group.Id, queue.Id);
        }

        using var client = _factory.CreateClient();
        const string email = "late-student@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Late", lastName = "Student", email, password = "secret1", groupId,
            acceptedPrivacyPolicy = true, acceptedTerms = true,
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/auth/confirm-email", new { email, code = _factory.Email.LastCodeFor(email) }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verify = _factory.Services.CreateScope();
        var entries = await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().QueueEntries
            .Where(e => e.QueueEventId == queueId).ToListAsync();
        entries.Should().ContainSingle().Which.Status.Should().Be(QueueEntryStatus.Waiting);
    }

    public void Dispose() => _factory.Dispose();
}

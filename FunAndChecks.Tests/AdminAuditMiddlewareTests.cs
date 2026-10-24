using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Middleware;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using NSubstitute;
using Xunit;

namespace FunAndChecks.Tests;

public sealed class AdminAuditMiddlewareTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CaptureWriter _writer = new();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private static DefaultHttpContext Request(Guid actorId, string method = "PUT", string controller = "Subjects", string action = "Update")
    {
        var http = new DefaultHttpContext { TraceIdentifier = "test-request" };
        http.Request.Method = method;
        http.Request.RouteValues["subjectId"] = 123;
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString()), new Claim(ClaimTypes.Role, Roles.Admin)
        ], "test"));
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = controller, ActionName = action }), "test"));
        return http;
    }

    [Fact]
    public async Task ForbiddenAttempt_UsesCurrentRoleAndNameAndRethrowsOriginalError()
    {
        var audit = new AdminAuditContext(_writer);
        await using var db = _db.NewContext(audit);
        var admin = db.Admin();
        admin.FirstName = "Анна";
        admin.LastName = "Иванова";
        await db.SaveChangesAsync();
        _identity.GetCurrentRolesAsync(admin.Id, Arg.Any<CancellationToken>()).Returns([Roles.Admin]);
        var http = Request(admin.Id);
        http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{password:'must-not-log'}"));
        var error = new ForbiddenException("restricted");
        var middleware = new AdminAuditMiddleware(_ => throw error);
        var operation = () => middleware.InvokeAsync(http, audit, _identity, db);

        (await operation.Should().ThrowAsync<ForbiddenException>()).Which.Should().BeSameAs(error);
        var entry = _writer.Events.Should().ContainSingle().Which;
        entry.ActorName.Should().Be("Иванова Анна");
        entry.ActorRole.Should().Be(Roles.Admin);
        entry.Action.Should().Be("subjects.update");
        entry.Outcome.Should().Be("denied");
        entry.After!["statusCode"].Should().Be(403);
        JsonSerializer.Serialize(entry).Should().NotContain("must-not-log");
    }

    [Fact]
    public async Task HandledValidationResponse_IsRecordedOnce()
    {
        var audit = new AdminAuditContext(_writer);
        await using var db = _db.NewContext(audit);
        var admin = db.Admin();
        await db.SaveChangesAsync();
        _identity.GetCurrentRolesAsync(admin.Id, Arg.Any<CancellationToken>()).Returns([Roles.SuperAdmin, Roles.Admin]);
        var http = Request(admin.Id);
        var middleware = new AdminAuditMiddleware(context => { context.Response.StatusCode = 400; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, audit, _identity, db);
        var entry = _writer.Events.Should().ContainSingle().Which;
        entry.ActorRole.Should().Be(Roles.SuperAdmin);
        entry.Outcome.Should().Be("failed");
        entry.After!["statusCode"].Should().Be(400);
    }

    [Fact]
    public async Task FailureAfterAnEarlierCommit_PreservesChangeAndReportsPartialFailure()
    {
        var audit = new AdminAuditContext(_writer);
        await using var db = _db.NewContext(audit);
        var admin = db.Admin();
        await db.SaveChangesAsync();
        _identity.GetCurrentRolesAsync(admin.Id, Arg.Any<CancellationToken>()).Returns([Roles.Admin]);
        var middleware = new AdminAuditMiddleware(async _ =>
        {
            db.Subject("Actually committed");
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Follow-up failed");
        });
        var operation = () => middleware.InvokeAsync(Request(admin.Id, "POST", action: "Create"), audit, _identity, db);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        _writer.Events.Should().HaveCount(2);
        _writer.Events[0].Outcome.Should().Be("success");
        _writer.Events[0].After!["name"].Should().Be("Actually committed");
        _writer.Events[1].Outcome.Should().Be("partial_failed");
        _writer.Events[1].After!["committedChanges"].Should().Be(1);
    }

    [Theory]
    [InlineData("GET", "Subjects", "GetAll")]
    [InlineData("POST", "Auth", "Refresh")]
    [InlineData("POST", "Auth", "Logout")]
    [InlineData("PUT", "Me", "SetPreferredCulture")]
    public async Task ReadsAndRoutineAuthentication_HaveNoAudit(string method, string controller, string action)
    {
        var audit = new AdminAuditContext(_writer);
        await using var db = _db.NewContext(audit);
        var http = Request(Guid.NewGuid(), method, controller, action);
        var calls = 0;
        await new AdminAuditMiddleware(_ => { calls++; return Task.CompletedTask; }).InvokeAsync(http, audit, _identity, db);
        calls.Should().Be(1);
        audit.Enabled.Should().BeFalse();
        _writer.Events.Should().BeEmpty();
        await _identity.DidNotReceive().GetCurrentRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulMutation_HasNoDuplicateHttpEnvelope()
    {
        var audit = new AdminAuditContext(_writer);
        await using var db = _db.NewContext(audit);
        var admin = db.Admin();
        await db.SaveChangesAsync();
        _identity.GetCurrentRolesAsync(admin.Id, Arg.Any<CancellationToken>()).Returns([Roles.Admin]);
        var middleware = new AdminAuditMiddleware(async _ =>
        {
            db.Subject("One committed event");
            await db.SaveChangesAsync();
        });
        await middleware.InvokeAsync(Request(admin.Id), audit, _identity, db);
        _writer.Events.Should().ContainSingle().Which.ObjectType.Should().Be("subject");
    }

    public void Dispose() => _db.Dispose();
    private sealed class CaptureWriter : IAdminAuditWriter
    {
        public List<AdminAuditEvent> Events { get; } = [];
        public bool TryWrite(AdminAuditEvent auditEvent) { Events.Add(auditEvent); return true; }
    }
}

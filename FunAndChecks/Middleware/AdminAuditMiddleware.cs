using System.Security.Claims;
using FluentValidation;
using FunAndChecks.Application.Auditing;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Middleware;

/// <summary>Sets the actor before authorization and records failures without reading request bodies.</summary>
public sealed class AdminAuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, AdminAuditContext audit, IIdentityService identity, IApplicationDbContext db)
    {
        var descriptor = http.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (!IsAuditedMutation(http, descriptor) || http.User.Identity?.IsAuthenticated != true ||
            (!http.User.IsInRole(Roles.Admin) && !http.User.IsInRole(Roles.SuperAdmin)) ||
            !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub"), out var actorId))
        {
            await next(http);
            return;
        }

        var actorName = actorId.ToString();
        var actorRole = http.User.IsInRole(Roles.SuperAdmin) ? Roles.SuperAdmin : Roles.Admin;
        try
        {
            var roles = await identity.GetCurrentRolesAsync(actorId, http.RequestAborted);
            actorRole = roles.Contains(Roles.SuperAdmin) ? Roles.SuperAdmin : roles.Contains(Roles.Admin) ? Roles.Admin : "User";
            actorName = await db.Admins.AsNoTracking().Where(a => a.Id == actorId)
                .Select(a => a.LastName + " " + a.FirstName).FirstOrDefaultAsync(http.RequestAborted) ?? actorName;
        }
        catch
        {
            // Preserve the signed-in actor ID when diagnostic enrichment is unavailable.
            // Authorization itself still checks roles in its existing policy.
            AdminAuditDiagnostics.RecordCaptureFailure();
        }
        var action = $"{descriptor!.ControllerName.ToLowerInvariant()}.{ToSnakeCase(descriptor.ActionName)}";
        audit.Begin(actorId, actorName, actorRole, action, http.TraceIdentifier);

        try
        {
            await next(http);
            if (http.Response.StatusCode >= 400) RecordFailure(audit, descriptor, http, http.Response.StatusCode);
        }
        catch (Exception exception)
        {
            var status = exception switch
            {
                ForbiddenException => StatusCodes.Status403Forbidden,
                NotFoundException => StatusCodes.Status404NotFound,
                ConflictException => StatusCodes.Status409Conflict,
                ValidationException => StatusCodes.Status400BadRequest,
                RateLimitException => StatusCodes.Status429TooManyRequests,
                OperationCanceledException when http.RequestAborted.IsCancellationRequested => 499,
                _ => StatusCodes.Status500InternalServerError,
            };
            RecordFailure(audit, descriptor, http, status);
            throw; // Existing exception middleware remains responsible for the response.
        }
    }

    internal static bool IsAuditedMutation(HttpContext http, ControllerActionDescriptor? action)
    {
        if (action is null || !HttpMethods.IsPost(http.Request.Method) && !HttpMethods.IsPut(http.Request.Method) &&
            !HttpMethods.IsPatch(http.Request.Method) && !HttpMethods.IsDelete(http.Request.Method)) return false;
        return action.ControllerName switch
        {
            "Subjects" or "Tasks" or "TaskGroups" or "Groups" or "Grades" or "Submissions" or "Students" or "Admins" or
            "Queues" or "Attendance" or "Variants" or "Achievements" or "Backup" or "Profiles" => true,
            "Me" => action.ActionName is "SetSubjectHidden" or "SetGroupHidden",
            _ => false,
        };
    }

    private static void RecordFailure(AdminAuditContext audit, ControllerActionDescriptor action, HttpContext http, int status)
    {
        var outcome = audit.HasCommittedWrites ? "partial_failed" : status is 401 or 403 ? "denied" : "failed";
        var objectId = string.Join("/", http.Request.RouteValues
            .Where(pair => pair.Key is not "action" and not "controller")
            .Select(pair => Convert.ToString(pair.Value)));
        audit.Publish(audit.Create(action.ControllerName.ToLowerInvariant(), objectId, outcome,
            $"HTTP {status}: {audit.Action}", after: new Dictionary<string, object?>
            {
                ["statusCode"] = status,
                ["committedChanges"] = audit.CommittedMutationCount,
                ["hasCommittedWrites"] = audit.HasCommittedWrites,
            }));
    }

    private static string ToSnakeCase(string name) => string.Concat(name.Select((character, index) =>
        char.IsUpper(character) && index > 0 ? "_" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
}

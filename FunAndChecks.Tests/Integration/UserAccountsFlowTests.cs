using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Students;
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

[Collection("Integration")]
public class UserAccountsFlowTests : IDisposable
{
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task AccountsEndpoints_RequireSuperAdmin_AndApplicationServiceAlsoChecksRole()
    {
        var data = await SeedAsync();
        using var anonymous = _factory.CreateClient();
        using var admin = Client(data.AdminToken);
        using var student = Client(data.StudentToken);
        (await anonymous.GetAsync("/api/students/accounts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/students/accounts/{data.StudentId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var client in new[] { admin, student })
        {
            (await client.GetAsync("/api/students/accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.DeleteAsync($"/api/students/accounts/{data.StudentId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
        await FluentActions.Invoking(() => accounts.GetAsync(data.AdminId, null, 1, 25)).Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Invoking(() => accounts.DeleteAsync(data.AdminId, data.StudentId)).Should().ThrowAsync<ForbiddenException>();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(u => u.Id == data.StudentId)).Should().BeTrue();
    }

    [Fact]
    public async Task Listing_SearchesNamesAndEmail_Paginates_AndIncludesUnconfirmedWithoutSubject()
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var group = await db.Groups.SingleAsync(g => g.Id == data.GroupId);
            for (var i = 0; i < 26; i++)
            {
                var student = db.Student(group, $"Account-{i:D2}");
                student.FirstName = "Searchable";
            }
            await db.SaveChangesAsync();
            var pending = db.Students.Single(s => s.LastName == "Account-00");
            pending.IsActive = false;
            (await db.Users.SingleAsync(u => u.Id == pending.Id)).EmailConfirmed = false;
            await db.SaveChangesAsync();
        }
        using var super = Client(data.SuperToken);
        var first = (await super.GetFromJsonAsync<UserAccountPageDto>("/api/students/accounts?query=SEARCHABLE&pageSize=25"))!;
        var second = (await super.GetFromJsonAsync<UserAccountPageDto>("/api/students/accounts?query=searchable&page=2&pageSize=25"))!;
        first.TotalCount.Should().Be(26);
        first.Items.Should().HaveCount(25);
        second.Items.Should().ContainSingle().Which.LastName.Should().Be("Account-25");
        first.Items.Should().ContainSingle(a => a.LastName == "Account-00" && !a.EmailConfirmed && !a.IsActive);
        var email = first.Items[3].Email!;
        (await super.GetFromJsonAsync<UserAccountPageDto>($"/api/students/accounts?query={Uri.EscapeDataString(email)}"))!
            .Items.Should().ContainSingle().Which.Email.Should().Be(email);
        (await super.GetAsync("/api/students/accounts?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await super.GetAsync("/api/students/accounts?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await super.GetAsync("/api/students/accounts?page=2147483647")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Listing_SearchesCyrillicNamesInEitherCase_AndBothFullNameOrders()
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var student = await db.Students.SingleAsync(s => s.Id == data.StudentId);
            student.FirstName = "Екатерина";
            student.LastName = "Орлова";
            await db.SaveChangesAsync();
        }
        using var super = Client(data.SuperToken);
        foreach (var query in new[] { "Орлова", "орлова", "ОРЛОВА", "орлова екатерина", "ЕКАТЕРИНА ОРЛОВА", "КатЕРи" })
        {
            var result = (await super.GetFromJsonAsync<UserAccountPageDto>(
                $"/api/students/accounts?query={Uri.EscapeDataString(query)}"))!;
            result.TotalCount.Should().Be(1, $"query '{query}' must match the Cyrillic name without case sensitivity");
            result.Items.Should().ContainSingle().Which.Id.Should().Be(data.StudentId);
        }
    }

    [Fact]
    public async Task AdminRolesAndProfiles_AreExcludedAndCannotBeDeleted_EvenWithoutMatchingProfileOrRole()
    {
        var data = await SeedAsync();
        var protectedIds = new List<Guid> { data.SuperId, data.AdminId };
        Guid orphanId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
            foreach (var role in new[] { Roles.Admin, Roles.SuperAdmin })
            {
                var id = Guid.NewGuid();
                await identity.CreateAccountAsync(id, $"role-only-{id:N}@example.com", "Password123!", [role], true);
                protectedIds.Add(id);
            }
            var profileOnly = db.Admin("P");
            await db.SaveChangesAsync();
            protectedIds.Add(profileOnly.Id);
            orphanId = Guid.NewGuid();
            await identity.CreateAccountAsync(orphanId, $"no-profile-{orphanId:N}@example.com", "Password123!", [], false);
        }
        using var super = Client(data.SuperToken);
        var list = (await super.GetFromJsonAsync<UserAccountPageDto>("/api/students/accounts?pageSize=100"))!;
        list.Items.Select(a => a.Id).Should().NotIntersectWith(protectedIds);
        list.Items.Should().ContainSingle(a => a.Id == orphanId && !a.HasStudentProfile && !a.EmailConfirmed);
        foreach (var id in protectedIds)
            (await super.DeleteAsync($"/api/students/accounts/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await super.DeleteAsync($"/api/students/accounts/{orphanId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await super.DeleteAsync($"/api/students/accounts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteStudent_CascadesAllRelatedData_InvalidatesResults_AndRevokesOldTokens()
    {
        var data = await SeedAsync();
        string refresh;
        int subjectId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var group = await db.Groups.SingleAsync(g => g.Id == data.GroupId);
            var subject = db.Subject();
            await db.SaveChangesAsync();
            subjectId = subject.Id;
            db.LinkGroupSubject(group, subject);
            var task = db.Task(subject);
            var component = db.Component(subject);
            var queue = new QueueEvent { Name = "Review", SubjectId = subject.Id, EventDateTime = DateTime.UtcNow };
            db.QueueEvents.Add(queue);
            var session = new AttendanceSession { Name = "Class", SubjectId = subject.Id, StartsAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            db.AttendanceSessions.Add(session);
            await db.SaveChangesAsync();
            db.Submissions.Add(new Submission { StudentId = data.StudentId, AdminId = data.AdminId, TaskId = task.Id, SubmittedAt = DateTime.UtcNow, Status = SubmissionStatus.Accepted });
            db.StudentGrades.Add(new StudentGrade { StudentId = data.StudentId, AdminId = data.AdminId, GradeComponentId = component.Id, Points = 20, UpdatedAt = DateTime.UtcNow });
            db.QueueEntries.Add(new QueueEntry { StudentId = data.StudentId, QueueEventId = queue.Id, Status = QueueEntryStatus.Waiting, JoinedAt = DateTime.UtcNow });
            db.AttendanceRecords.Add(new AttendanceRecord { StudentId = data.StudentId, SessionId = session.Id, GroupId = group.Id, GroupName = group.Name, Status = AttendanceStatus.Present });
            await db.SaveChangesAsync();
            refresh = await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>().IssueAsync(data.StudentId);
        }
        using var super = Client(data.SuperToken);
        using var student = Client(data.StudentToken);
        var resultsUrl = $"/api/results/subjects/{subjectId}";
        (await super.GetFromJsonAsync<SubjectResultsDto>(resultsUrl))!.UserResults.Should().Contain(r => r.StudentId == data.StudentId);
        (await student.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await super.DeleteAsync($"/api/students/accounts/{data.StudentId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await super.GetFromJsonAsync<SubjectResultsDto>(resultsUrl))!.UserResults.Should().NotContain(r => r.StudentId == data.StudentId);
        (await student.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await student.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var verify = _factory.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Users.AnyAsync(u => u.Id == data.StudentId)).Should().BeFalse();
        (await context.Students.AnyAsync(s => s.Id == data.StudentId)).Should().BeFalse();
        (await context.Submissions.AnyAsync(s => s.StudentId == data.StudentId)).Should().BeFalse();
        (await context.StudentGrades.AnyAsync(g => g.StudentId == data.StudentId)).Should().BeFalse();
        (await context.QueueEntries.AnyAsync(q => q.StudentId == data.StudentId)).Should().BeFalse();
        (await context.AttendanceRecords.AnyAsync(a => a.StudentId == data.StudentId)).Should().BeFalse();
        (await context.RefreshTokens.AnyAsync(t => t.UserId == data.StudentId)).Should().BeFalse();
        (await context.Admins.AnyAsync(a => a.Id == data.AdminId)).Should().BeTrue();
        (await context.Groups.AnyAsync(g => g.Id == data.GroupId)).Should().BeTrue();
        (await context.Subjects.AnyAsync(s => s.Id == subjectId)).Should().BeTrue();
        (await context.QueueEvents.CountAsync()).Should().Be(1);
        (await context.AttendanceSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteFailure_RollsBackIdentityAndProfileAndRefreshTokenTogether()
    {
        var data = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>().IssueAsync(data.StudentId);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_student_delete BEFORE DELETE ON Students BEGIN SELECT RAISE(ABORT, 'test deletion failure'); END;");
        }
        using var super = Client(data.SuperToken);
        (await super.DeleteAsync($"/api/students/accounts/{data.StudentId}")).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var verify = _factory.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Users.AnyAsync(u => u.Id == data.StudentId)).Should().BeTrue();
        (await context.Students.AnyAsync(s => s.Id == data.StudentId)).Should().BeTrue();
        (await context.RefreshTokens.AnyAsync(t => t.UserId == data.StudentId)).Should().BeTrue();
        using var student = Client(data.StudentToken);
        (await student.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<AccountsSeed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var super = db.Admin("S");
        var admin = db.Admin();
        var group = db.Group();
        await db.SaveChangesAsync();
        var student = db.Student(group);
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.AddToRolesAsync((await users.FindByIdAsync(super.Id.ToString()))!, [Roles.Admin, Roles.SuperAdmin])).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student)).Succeeded.Should().BeTrue();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new AccountsSeed(super.Id, admin.Id, student.Id, group.Id,
            await tokens.CreateTokenAsync(super.Id), await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(student.Id));
    }

    private record AccountsSeed(Guid SuperId, Guid AdminId, Guid StudentId, int GroupId,
        string SuperToken, string AdminToken, string StudentToken);

    public void Dispose() => _factory.Dispose();
}

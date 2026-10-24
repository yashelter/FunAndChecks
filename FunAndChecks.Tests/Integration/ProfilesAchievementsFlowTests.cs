using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class ProfilesAchievementsFlowTests : IDisposable
{
    private readonly ProfileFactory _factory = new();

    [Fact]
    public async Task ProfilesAndMedia_RequireLogin_AndNeverExposeEmail()
    {
        var seed = await SeedAsync();
        using var anonymous = _factory.CreateClient();
        foreach (var url in new[] { "/api/me/profile", $"/api/profiles/{seed.StudentId}", "/api/achievements", $"/api/media/{Guid.NewGuid()}" })
            (await anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var student = Client(seed.StudentToken);
        var response = await student.GetAsync($"/api/profiles/{seed.AdminId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = (await response.Content.ReadFromJsonAsync<UserProfileDto>())!;
        profile.IsAdmin.Should().BeTrue();
        profile.UserId.Should().Be(seed.AdminId);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("email");
        (await student.GetAsync($"/api/profiles/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await student.GetAsync($"/api/profiles/{seed.PendingId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OwnProfile_ValidatesLinksAndPins_WithoutChangingAcademicIdentity()
    {
        var seed = await SeedAsync();
        using var student = Client(seed.StudentToken);
        var request = new UpdateProfileRequest(" <b>Люблю C#</b> ", "https://github.com/student", "https://portfolio.example/work", []);
        var updated = await student.PutAsJsonAsync("/api/me/profile", request);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = (await updated.Content.ReadFromJsonAsync<UserProfileDto>())!;
        profile.AboutMe.Should().Be("<b>Люблю C#</b>");
        profile.FullName.Should().Be("Орлова Екатерина");
        profile.GroupName.Should().Be("Hidden group");
        (await student.PutAsJsonAsync("/api/me/profile", request with { PortfolioUrl = "javascript:alert(1)" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await student.PutAsJsonAsync("/api/me/profile", request with { GitHubUrl = "https://github.com.evil.example/student" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await student.PutAsJsonAsync("/api/me/profile", request with { PinnedAwardIds = [1, 2, 3, 4] })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await student.PutAsJsonAsync("/api/me/profile", request with { PinnedAwardIds = [999] })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = _factory.Services.CreateScope();
        var original = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Students.SingleAsync(s => s.Id == seed.StudentId);
        original.Color.Should().Be("#aabbcc");
        original.FirstName.Should().Be("Екатерина");
        original.LastName.Should().Be("Орлова");
    }

    [Fact]
    public async Task DefinitionManagement_RequiresCurrentSuperRole_EvenWithOldToken()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        var request = new UpsertAchievementRequest("First achievement", "Description");
        (await student.PostAsJsonAsync("/api/admin/achievements", request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync("/api/admin/achievements", request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var definition = await CreateAsync(super, "First achievement");
        (await admin.PutAsJsonAsync($"/api/admin/achievements/{definition.Id}", request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.RemoveFromRoleAsync((await users.FindByIdAsync(seed.SuperId.ToString()))!, Roles.SuperAdmin);
        }
        (await super.PostAsJsonAsync("/api/admin/achievements", request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await super.PutAsJsonAsync($"/api/admin/achievements/{definition.Id}/archive", new ArchiveAchievementRequest(true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminCanGrantAcrossRestrictionsAndToSelf_DirectoryDeduplicatesAndPaginates()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        var definition = await CreateAsync(super);
        (await student.GetAsync("/api/admin/achievement-recipients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var query in new[] { "орлова", "ОРЛОВА", "ЕКАТЕРИНА ОРЛОВА", "Орлова Екатерина" })
        {
            var page = (await admin.GetFromJsonAsync<AchievementRecipientAccountPageDto>(
                $"/api/admin/achievement-recipients?query={Uri.EscapeDataString(query)}"))!;
            page.TotalCount.Should().Be(1);
            page.Items.Should().ContainSingle().Which.UserId.Should().Be(seed.StudentId);
        }
        var all = (await admin.GetFromJsonAsync<AchievementRecipientAccountPageDto>("/api/admin/achievement-recipients?pageSize=100"))!;
        all.Items.Should().HaveCount(4);
        all.Items.Should().NotContain(p => p.UserId == seed.PendingId);
        all.Items.Should().ContainSingle(p => p.UserId == seed.AdminId && p.IsAdmin);
        var first = (await admin.GetFromJsonAsync<AchievementRecipientAccountPageDto>("/api/admin/achievement-recipients?pageSize=1"))!;
        first.TotalCount.Should().Be(4);
        first.Items.Should().ContainSingle();
        (await admin.GetAsync("/api/admin/achievement-recipients?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var award = await GrantAsync(admin, definition.Id, seed.StudentId);
        award.IssuedByUserId.Should().Be(seed.AdminId);
        await GrantAsync(admin, definition.Id, seed.AdminId);
        var recipients = (await student.GetFromJsonAsync<AchievementRecipientPageDto>($"/api/achievements/{definition.Id}/recipients"))!;
        recipients.TotalCount.Should().Be(2);
        recipients.Items.Select(p => p.UserId).Should().BeEquivalentTo([seed.StudentId, seed.AdminId]);
    }

    [Fact]
    public async Task RevokeUnpins_OnlyIssuerOrSuperAllowed_RegrantPreservesHistory()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        using var other = Client(seed.OtherAdminToken);
        using var student = Client(seed.StudentToken);
        var definition = await CreateAsync(super);
        var award = await GrantAsync(admin, definition.Id, seed.StudentId);
        (await admin.PostAsJsonAsync($"/api/admin/achievements/{definition.Id}/awards", new GrantAchievementRequest(seed.StudentId)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await student.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequest("", null, null, [award.Id])))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!.Awards.Should().ContainSingle().Which.PinPosition.Should().Be(1);
        (await other.PostAsync($"/api/admin/achievement-awards/{award.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/admin/achievement-awards/{award.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!.Awards.Should().BeEmpty();
        var replacement = await GrantAsync(other, definition.Id, seed.StudentId);
        replacement.Id.Should().NotBe(award.Id);
        (await super.PostAsync($"/api/admin/achievement-awards/{replacement.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.AchievementAwards.CountAsync(a => a.UserId == seed.StudentId)).Should().Be(2);
        (await db.PinnedAchievements.CountAsync()).Should().Be(0);
        (await db.AchievementAwards.AllAsync(a => a.RevokedAtUtc != null)).Should().BeTrue();
    }

    [Fact]
    public async Task ArchivedDefinitionsRemainOnProfileAndDetail_AndEditsUpdateExistingAwards()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        var definition = await CreateAsync(super, "Old name");
        await GrantAsync(admin, definition.Id, seed.StudentId);
        (await super.PutAsJsonAsync($"/api/admin/achievements/{definition.Id}", new UpsertAchievementRequest("New name", "Changed")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await super.PutAsJsonAsync($"/api/admin/achievements/{definition.Id}/archive", new ArchiveAchievementRequest(true)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync($"/api/admin/achievements/{definition.Id}/awards", new GrantAchievementRequest(seed.AdminId)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await student.GetFromJsonAsync<AchievementPageDto>("/api/achievements"))!.Items.Should().BeEmpty();
        (await student.GetFromJsonAsync<AchievementPageDto>("/api/achievements?includeArchived=true"))!.Items.Should().ContainSingle().Which.IsArchived.Should().BeTrue();
        (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!.Awards.Should().ContainSingle().Which.Name.Should().Be("New name");
        (await student.GetFromJsonAsync<AchievementDto>($"/api/achievements/{definition.Id}"))!.Description.Should().Be("Changed");
        (await student.GetAsync("/api/achievements?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PinReorderingIsAtomic_AndCannotPinSomeoneElsesAward()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        var ids = new List<int>();
        for (var i = 0; i < 3; i++) ids.Add((await GrantAsync(admin, (await CreateAsync(super, $"Achievement {i}")).Id, seed.StudentId)).Id);
        var foreignAward = await GrantAsync(admin, (await CreateAsync(super, "Admin only")).Id, seed.AdminId);
        foreach (var order in new[] { ids.ToArray(), ids.AsEnumerable().Reverse().ToArray() })
        {
            (await student.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequest("", null, null, order))).StatusCode.Should().Be(HttpStatusCode.OK);
            var profile = (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!;
            profile.Awards.Where(a => a.PinPosition.HasValue).OrderBy(a => a.PinPosition).Select(a => a.Id).Should().Equal(order);
        }
        (await student.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequest("Changed", null, null, [foreignAward.Id])))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!.AboutMe.Should().BeEmpty();
    }

    [Fact]
    public async Task AvatarReplacementAndDatabaseFailure_KeepRetryableSessionAndPreviousMedia()
    {
        var seed = await SeedAsync();
        using var student = Client(seed.StudentToken);
        using var admin = Client(seed.AdminToken);
        var session = _factory.Media.AddSession(seed.StudentId);
        (await admin.PostAsJsonAsync("/api/me/avatar", new UpdateAvatarRequest(session, Crop)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var response = await student.PostAsJsonAsync("/api/me/avatar", new UpdateAvatarRequest(session, Crop));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var avatar = (await response.Content.ReadFromJsonAsync<MediaAssetDto>())!;
        avatar.Width.Should().Be(256);
        _factory.Media.Committed.Should().Contain(session);
        var mediaResponse = await student.GetAsync($"/api/media/{avatar.Id}");
        mediaResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await mediaResponse.Content.ReadAsByteArrayAsync()).Should().Equal([1, 2, 3]);
        mediaResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        var failedSession = _factory.Media.AddSession(seed.StudentId);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_media_insert BEFORE INSERT ON ProfileMediaAssets BEGIN SELECT RAISE(ABORT, 'test media failure'); END;");
        (await student.PostAsJsonAsync("/api/me/avatar", new UpdateAvatarRequest(failedSession, Crop)))
            .StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _factory.Media.Committed.Should().NotContain(failedSession);
        (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!.Avatar!.Id.Should().Be(avatar.Id);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER fail_media_insert;");
        var replacement = (await (await student.PostAsJsonAsync("/api/me/avatar", new UpdateAvatarRequest(failedSession, Crop)))
            .Content.ReadFromJsonAsync<MediaAssetDto>())!;
        replacement.Id.Should().NotBe(avatar.Id);
        (await student.GetAsync($"/api/media/{avatar.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await student.DeleteAsync("/api/me/avatar")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await student.GetAsync($"/api/media/{replacement.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AccountDeletionCascadesNewData_AndRemovingIssuerKeepsRecipientsAndSnapshots()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        var definitionSession = _factory.Media.AddSession(seed.SuperId);
        var definition = (await (await super.PostAsJsonAsync("/api/admin/achievements",
            new UpsertAchievementRequest("Retained", "", definitionSession, Crop))).Content.ReadFromJsonAsync<AchievementDto>())!;
        definition.Image!.Width.Should().Be(512);
        var award = await GrantAsync(admin, definition.Id, seed.StudentId);
        await student.PutAsJsonAsync("/api/me/profile", new UpdateProfileRequest("Bio", null, null, [award.Id]));
        var avatarSession = _factory.Media.AddSession(seed.StudentId);
        var avatar = (await (await student.PostAsJsonAsync("/api/me/avatar", new UpdateAvatarRequest(avatarSession, Crop)))
            .Content.ReadFromJsonAsync<MediaAssetDto>())!;
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(u => u.Id == seed.AdminId).ExecuteDeleteAsync();
        var profile = (await student.GetFromJsonAsync<UserProfileDto>("/api/me/profile"))!;
        profile.Awards.Should().ContainSingle().Which.IssuedByUserId.Should().BeNull();
        profile.Awards[0].IssuedByName.Should().Be("A Admin");
        (await super.DeleteAsync($"/api/students/accounts/{seed.StudentId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.UserProfiles.AnyAsync(p => p.UserId == seed.StudentId)).Should().BeFalse();
        (await db.ProfileMediaAssets.AnyAsync(a => a.Id == avatar.Id)).Should().BeFalse();
        (await db.AchievementAwards.AnyAsync(a => a.UserId == seed.StudentId)).Should().BeFalse();
        (await db.PinnedAchievements.AnyAsync()).Should().BeFalse();
        (await db.ProfileMediaAssets.AnyAsync(a => a.Id == definition.Image.Id)).Should().BeTrue();
        (await db.AchievementDefinitions.AnyAsync(a => a.Id == definition.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task DatabaseConstraintPreventsTwoActiveAwards_AndRejectsUnownedMedia()
    {
        var seed = await SeedAsync();
        using var super = Client(seed.SuperToken);
        using var admin = Client(seed.AdminToken);
        var definition = await CreateAsync(super);
        await GrantAsync(admin, definition.Id, seed.StudentId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AchievementAwards.Add(new AchievementAward
                { AchievementId = definition.Id, UserId = seed.StudentId, IssuedByUserId = seed.AdminId, IssuedByName = "Duplicate", GrantedAtUtc = DateTime.UtcNow });
            await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProfileMediaAssets.Add(new ProfileMediaAsset
                { Data = [1], ContentType = "image/webp", PosterData = [1], PosterContentType = "image/webp", Width = 256, Height = 256 });
            await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
    }

    private static readonly MediaRenderRequest Crop = new(0, 0, 1, 1, 0, 0, 0, "Still");

    private async Task<AchievementDto> CreateAsync(HttpClient client, string name = "Achievement")
    {
        var response = await client.PostAsJsonAsync("/api/admin/achievements", new UpsertAchievementRequest(name, "Description"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AchievementDto>())!;
    }

    private static async Task<AchievementAwardDto> GrantAsync(HttpClient client, int id, Guid userId)
    {
        var response = await client.PostAsJsonAsync($"/api/admin/achievements/{id}/awards", new GrantAchievementRequest(userId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AchievementAwardDto>())!;
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<ProfileSeed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var super = db.Admin("S");
        var admin = db.Admin("A");
        var other = db.Admin("B");
        var group = db.Group("Hidden group");
        await db.SaveChangesAsync();
        var student = db.Student(group, "Орлова");
        student.FirstName = "Екатерина";
        student.Color = "#aabbcc";
        var pending = db.Student(group, "Pending");
        pending.IsActive = false;
        db.Users.Local.Single(u => u.Id == pending.Id).EmailConfirmed = false;
        // A dual academic row must appear only once, with administrator identity taking precedence.
        db.AdminGroupAccesses.Add(new AdminGroupAccess { AdminId = admin.Id, GroupId = group.Id, IsRestricted = true, IsHidden = true });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO Students (Id, FirstName, LastName, IsActive, GroupId, CreatedAt, Color) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
            admin.Id, "Duplicate", "Duplicate", true, group.Id, DateTime.UtcNow, "#123456");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.AddToRolesAsync((await users.FindByIdAsync(super.Id.ToString()))!, [Roles.Admin, Roles.SuperAdmin]);
        await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin);
        await users.AddToRoleAsync((await users.FindByIdAsync(other.Id.ToString()))!, Roles.Admin);
        await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student);
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new(super.Id, admin.Id, other.Id, student.Id, pending.Id,
            await tokens.CreateTokenAsync(super.Id), await tokens.CreateTokenAsync(admin.Id),
            await tokens.CreateTokenAsync(other.Id), await tokens.CreateTokenAsync(student.Id));
    }

    public void Dispose() => _factory.Dispose();

    private record ProfileSeed(Guid SuperId, Guid AdminId, Guid OtherAdminId, Guid StudentId, Guid PendingId,
        string SuperToken, string AdminToken, string OtherAdminToken, string StudentToken);

    private sealed class ProfileFactory : TestWebAppFactory
    {
        public FakeMediaProcessor Media { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProfileMediaProcessor>();
                services.AddSingleton<IProfileMediaProcessor>(Media);
            });
        }
    }

    private sealed class FakeMediaProcessor : IProfileMediaProcessor
    {
        private readonly Dictionary<Guid, Guid> _sessions = [];
        public List<Guid> Committed { get; } = [];
        public Guid AddSession(Guid userId)
        {
            var id = Guid.NewGuid();
            _sessions[id] = userId;
            return id;
        }
        public Task<IProfileMediaRenderLease> RenderAsync(Guid userId, Guid sessionId, MediaRenderRequest request, int outputSize, CancellationToken cancellationToken = default)
        {
            if (!_sessions.TryGetValue(sessionId, out var owner)) throw new NotFoundException("Session not found.");
            if (owner != userId) throw new ForbiddenException("The session belongs to someone else.");
            return Task.FromResult<IProfileMediaRenderLease>(new Lease(this, sessionId,
                new ProcessedProfileMedia([1, 2, 3], "image/webp", [4, 5], "image/webp", outputSize, outputSize, 0, false)));
        }
        public Task<MediaEditorSessionDto> PrepareAsync(Guid userId, Stream source, string fileName, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaStreamResult> GetPreviewAsync(Guid userId, Guid sessionId, bool poster = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaStreamResult> GetFrameAsync(Guid userId, Guid sessionId, double timeSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private sealed class Lease(FakeMediaProcessor owner, Guid sessionId, ProcessedProfileMedia media) : IProfileMediaRenderLease
        {
            public ProcessedProfileMedia Media => media;
            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Committed.Add(sessionId);
                owner._sessions.Remove(sessionId);
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Infrastructure.Backup;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class BackupFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"funandchecks-backup-api-tests-{Guid.NewGuid():N}");
    private readonly BackupWebAppFactory _factory;

    public BackupFlowTests() => _factory = new BackupWebAppFactory(_directory);

    [Fact]
    public async Task BackupEndpoints_RequireSuperAdmin()
    {
        var tokens = await SeedTokensAsync();
        using var anonymous = _factory.CreateClient();
        using var admin = Client(tokens.Admin);
        using var student = Client(tokens.Student);
        foreach (var (client, status) in new[]
        {
            (anonymous, HttpStatusCode.Unauthorized),
            (admin, HttpStatusCode.Forbidden),
            (student, HttpStatusCode.Forbidden),
        })
        {
            (await client.GetAsync("/api/admin/backup")).StatusCode.Should().Be(status);
            (await client.GetAsync("/api/admin/backup/file.dump")).StatusCode.Should().Be(status);
            (await client.DeleteAsync("/api/admin/backup/file.dump")).StatusCode.Should().Be(status);
            (await client.PostAsync("/api/admin/backup", null)).StatusCode.Should().Be(status);
        }
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task SuperAdmin_CanListDownloadRangeAndDeleteOnlySelectedBackup()
    {
        var tokens = await SeedTokensAsync();
        Directory.CreateDirectory(_directory);
        var bytes = new byte[] { 0, 1, 2, 253, 254, 255 };
        await File.WriteAllBytesAsync(Path.Combine(_directory, "selected.dump"), bytes);
        await File.WriteAllTextAsync(Path.Combine(_directory, "other.dump"), "keep");
        await File.WriteAllTextAsync(Path.Combine(_directory, "unfinished.dump.partial"), "not ready");
        using var super = Client(tokens.Super);

        var list = (await super.GetFromJsonAsync<List<BackupFileDto>>("/api/admin/backup"))!;
        list.Select(file => file.FileName).Should().BeEquivalentTo("selected.dump", "other.dump");
        list.Single(file => file.FileName == "selected.dump").SizeBytes.Should().Be(bytes.Length);
        var json = await super.GetStringAsync("/api/admin/backup");
        json.Should().NotContain(_directory).And.NotContain("partial");

        using var response = await super.GetAsync("/api/admin/backup/selected.dump");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.Should().Be("selected.dump");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/backup/selected.dump");
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await super.SendAsync(rangeRequest);
        rangeResponse.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await rangeResponse.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 253);

        (await super.DeleteAsync("/api/admin/backup/selected.dump")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await super.GetAsync("/api/admin/backup/selected.dump")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await super.DeleteAsync("/api/admin/backup/selected.dump")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await super.GetFromJsonAsync<List<BackupFileDto>>("/api/admin/backup"))!
            .Should().ContainSingle().Which.FileName.Should().Be("other.dump");
        File.ReadAllText(Path.Combine(_directory, "other.dump")).Should().Be("keep");
        File.Exists(Path.Combine(_directory, "unfinished.dump.partial")).Should().BeTrue();
    }

    [Theory]
    [InlineData("not-backup.txt")]
    [InlineData("unfinished.dump.partial")]
    [InlineData("..%5Coutside.dump")]
    [InlineData("C%3Aoutside.dump")]
    public async Task InvalidNames_ReturnBadRequestForReadAndDelete(string fileName)
    {
        var tokens = await SeedTokensAsync();
        using var super = Client(tokens.Super);
        (await super.GetAsync($"/api/admin/backup/{fileName}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await super.DeleteAsync($"/api/admin/backup/{fileName}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task EmptyListAndMissingFiles_HaveExpectedHttpResponses()
    {
        var tokens = await SeedTokensAsync();
        using var super = Client(tokens.Super);
        (await super.GetFromJsonAsync<List<BackupFileDto>>("/api/admin/backup"))!.Should().BeEmpty();
        using var missing = await super.GetAsync("/api/admin/backup/missing.dump");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("backup.not_found").And.NotContain(_directory);
        (await super.DeleteAsync("/api/admin/backup/missing.dump")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        Directory.Exists(_directory).Should().BeFalse();
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Super, string Admin, string Student)> SeedTokensAsync()
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
        return (await tokens.CreateTokenAsync(super.Id), await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(student.Id));
    }

    public void Dispose()
    {
        _factory.Dispose();
        var cleanupPath = Path.GetFullPath(_directory);
        if (!string.Equals(Path.GetDirectoryName(cleanupPath), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(cleanupPath).StartsWith("funandchecks-backup-api-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected backup API test directory.");
        if (Directory.Exists(_directory))
            Directory.Delete(cleanupPath, recursive: true);
    }

    private sealed class BackupWebAppFactory(string directory) : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.Configure<BackupOptions>(options => options.Directory = directory));
        }
    }
}

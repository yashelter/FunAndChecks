using System.IO.Compression;
using FluentAssertions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Backup;
using FunAndChecks.Infrastructure.Files;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Tests.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FunAndChecks.Tests.Files;

public class AttachmentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"funandchecks-attachments-{Guid.NewGuid():N}");

    [Fact]
    public async Task DiskStorage_RoundTrips_AndRejectsKeysOutsideTheRoot()
    {
        var storage = Storage();
        var key = await storage.SaveAsync(new MemoryStream([1, 2, 3]));
        key.Should().MatchRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}$");
        await using (var stream = await storage.OpenReadAsync(key))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(1, 2, 3);
        }
        await storage.DeleteAsync(key);
        await FluentActions.Invoking(() => storage.OpenReadAsync(key)).Should().ThrowAsync<NotFoundException>();
        foreach (var bad in new[] { "../secrets.json", "2026/10/../../x", "C:/Windows/win.ini", "2026/10/ABC" })
            await FluentActions.Invoking(() => storage.OpenReadAsync(bad)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void SignedUrls_ExpireAndCannotBeReusedForAnotherFile()
    {
        var time = new ManualTime(DateTimeOffset.Parse("2026-10-18T10:00:00Z"));
        var signer = new HmacAttachmentUrlSigner(Options.Create(new JwtOptions { Key = new string('k', 64) }), time);
        var id = Guid.NewGuid();
        var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://x/" + signer.CreateUrl(id)).Query);
        var expires = long.Parse(query["exp"]!);
        var signature = query["sig"]!;

        signer.Verify(id, expires, signature).Should().BeTrue();
        signer.Verify(Guid.NewGuid(), expires, signature).Should().BeFalse();
        signer.Verify(id, expires + 3600, signature).Should().BeFalse();
        signer.Verify(id, expires, signature + "x").Should().BeFalse();
        var otherKey = new HmacAttachmentUrlSigner(Options.Create(new JwtOptions { Key = new string('z', 64) }), time);
        otherKey.Verify(id, expires, signature).Should().BeFalse();
        time.Advance(TimeSpan.FromHours(8));
        signer.Verify(id, expires, signature).Should().BeFalse();
    }

    [Fact]
    public async Task Cleanup_RemovesOnlyStaleUnattachedFiles()
    {
        using var database = new TestDatabase();
        var storage = Storage();
        Guid fresh, attached;
        string staleKey, attachedKey;
        await using (var db = database.NewContext())
        {
            var admin = db.Admin();
            var topic = new ForumTopic { Title = "T", AuthorName = "A", CreatedAtUtc = DateTime.UtcNow, LastActivityAtUtc = DateTime.UtcNow };
            var post = new ForumPost { Topic = topic, AuthorName = "A", Body = "b", CreatedAtUtc = DateTime.UtcNow };
            db.ForumPosts.Add(post);
            await db.SaveChangesAsync();
            staleKey = await storage.SaveAsync(new MemoryStream([1]));
            attachedKey = await storage.SaveAsync(new MemoryStream([2]));
            var freshFile = File(await storage.SaveAsync(new MemoryStream([3])), DateTime.UtcNow, null);
            var staleFile = File(staleKey, DateTime.UtcNow.AddDays(-2), null);
            var attachedFile = File(attachedKey, DateTime.UtcNow.AddDays(-2), post.Id);
            db.Attachments.AddRange(freshFile, staleFile, attachedFile);
            await db.SaveChangesAsync();
            (fresh, attached) = (freshFile.Id, attachedFile.Id);
        }

        var services = new ServiceCollection()
            .AddScoped(_ => database.NewContext())
            .AddSingleton<FunAndChecks.Application.Common.Interfaces.IFileStorage>(storage)
            .BuildServiceProvider();
        var cleanup = new AttachmentCleanupService(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AttachmentStorageOptions { RootDirectory = _root }), NullLogger<AttachmentCleanupService>.Instance);
        (await cleanup.CleanupAsync(CancellationToken.None)).Should().Be(1);

        await using var verify = database.NewContext();
        verify.Attachments.Select(a => a.Id).Should().BeEquivalentTo([fresh, attached]);
        await FluentActions.Invoking(() => storage.OpenReadAsync(staleKey)).Should().ThrowAsync<NotFoundException>();
        (await storage.OpenReadAsync(attachedKey)).Dispose();
    }

    [Fact]
    public async Task BackupArchive_PacksFinishedFiles_AndIsListedNextToDumps()
    {
        var storage = Storage();
        var key = await storage.SaveAsync(new MemoryStream([7, 7]));
        await System.IO.File.WriteAllTextAsync(Path.Combine(_root, "2026", "unfinished.part"), "x");
        var backups = Path.Combine(_root, "..", $"funandchecks-backups-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backups);
        try
        {
            var archive = Path.Combine(backups, "funandchecks_db_1.attachments.zip");
            await PgDumpBackupService.CreateAttachmentsArchiveAsync(_root, archive, CancellationToken.None);
            using (var zip = ZipFile.OpenRead(archive))
                zip.Entries.Select(e => e.FullName).Should().Equal(key);
            var service = new PgDumpBackupService(new ConfigurationBuilder().Build(),
                Options.Create(new BackupOptions { Directory = backups }), NullLogger<PgDumpBackupService>.Instance);
            (await service.ListAsync()).Select(f => f.FileName).Should().Equal("funandchecks_db_1.attachments.zip");
            (await service.OpenReadAsync("funandchecks_db_1.attachments.zip")).Dispose();
            await FluentActions.Invoking(() => service.OpenReadAsync("notes.zip")).Should().ThrowAsync<FluentValidation.ValidationException>();
        }
        finally
        {
            Directory.Delete(backups, true);
        }
    }

    private LocalDiskFileStorage Storage() => new(Options.Create(new AttachmentStorageOptions { RootDirectory = _root }));

    private static Attachment File(string key, DateTime created, long? postId) => new()
    {
        FileName = "f.png", ContentType = "image/png", Sha256 = new string('0', 64), StorageKey = key, SizeBytes = 1,
        IsImage = true, CreatedAtUtc = created, ForumPostId = postId,
    };

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

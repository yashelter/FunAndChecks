using FluentAssertions;
using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Infrastructure.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FunAndChecks.Tests.Backups;

public class PgDumpBackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"funandchecks-backup-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task List_FiltersIncompleteAndUnrelatedFiles_ReturnsOnlyNamesAndMetadataNewestFirst()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "old.dump"), [1, 2]);
        await File.WriteAllBytesAsync(Path.Combine(_directory, "new.dump"), [3, 4, 5]);
        await File.WriteAllTextAsync(Path.Combine(_directory, "unfinished.dump.partial"), "not ready");
        await File.WriteAllTextAsync(Path.Combine(_directory, "notes.txt"), "not a backup");
        Directory.CreateDirectory(Path.Combine(_directory, "directory.dump"));
        Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "nested", "nested.dump"), "not directly in backup directory");
        var oldTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newTime = oldTime.AddDays(1);
        File.SetLastWriteTimeUtc(Path.Combine(_directory, "old.dump"), oldTime);
        File.SetLastWriteTimeUtc(Path.Combine(_directory, "new.dump"), newTime);

        var result = await Service().ListAsync();

        result.Select(file => file.FileName).Should().Equal("new.dump", "old.dump");
        result.Select(file => file.SizeBytes).Should().Equal(3L, 2L);
        result.Select(file => file.LastModifiedUtc).Should().Equal(newTime, oldTime);
        result.Should().OnlyContain(file => Path.GetFileName(file.FileName) == file.FileName);
    }

    [Fact]
    public async Task List_WithNoBackupDirectory_IsEmptyAndDoesNotCreateDirectory()
    {
        (await Service().ListAsync()).Should().BeEmpty();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAndDelete_OperateOnTheSelectedDumpAndPreserveOtherFiles()
    {
        Directory.CreateDirectory(_directory);
        var bytes = new byte[] { 0, 1, 2, 254, 255 };
        await File.WriteAllBytesAsync(Path.Combine(_directory, "selected.dump"), bytes);
        await File.WriteAllTextAsync(Path.Combine(_directory, "other.dump"), "keep");

        await using (var stream = await Service().OpenReadAsync("selected.dump"))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
            stream.CanWrite.Should().BeFalse();
        }
        await Service().DeleteAsync("selected.dump");

        File.Exists(Path.Combine(_directory, "selected.dump")).Should().BeFalse();
        File.ReadAllText(Path.Combine(_directory, "other.dump")).Should().Be("keep");
        (await Service().ListAsync()).Should().ContainSingle().Which.FileName.Should().Be("other.dump");
    }

    [Fact]
    public async Task DeleteDuringDownload_RemovesBackupButOpenDownloadCanFinish()
    {
        Directory.CreateDirectory(_directory);
        var bytes = new byte[] { 10, 20, 30, 40 };
        var path = Path.Combine(_directory, "selected.dump");
        await File.WriteAllBytesAsync(path, bytes);
        await using var stream = await Service().OpenReadAsync("selected.dump");

        await Service().DeleteAsync("selected.dump");

        File.Exists(path).Should().BeFalse();
        (await Service().ListAsync()).Should().BeEmpty();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        copy.ToArray().Should().Equal(bytes);
    }

    [Theory]
    [InlineData("../outside.dump")]
    [InlineData("..\\outside.dump")]
    [InlineData("/outside.dump")]
    [InlineData("C:\\outside.dump")]
    [InlineData("folder/file.dump")]
    [InlineData("folder\\file.dump")]
    [InlineData("outside.dump:stream")]
    [InlineData("not-backup.txt")]
    [InlineData("unfinished.dump.partial")]
    [InlineData(".dump")]
    [InlineData("")]
    [InlineData("invalid\u0000.dump")]
    public async Task UnsafeOrNonDumpNames_AreRejectedForDownloadAndDeletion(string fileName)
    {
        await FluentActions.Invoking(() => Service().OpenReadAsync(fileName)).Should().ThrowAsync<ValidationException>();
        await FluentActions.Invoking(() => Service().DeleteAsync(fileName)).Should().ThrowAsync<ValidationException>();
        Directory.Exists(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task MissingFilesAndDumpNamedDirectories_ReturnNotFound()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "directory.dump"));
        foreach (var name in new[] { "missing.dump", "directory.dump" })
        {
            (await FluentActions.Invoking(() => Service().OpenReadAsync(name)).Should().ThrowAsync<NotFoundException>())
                .Which.Code.Should().Be("backup.not_found");
            await FluentActions.Invoking(() => Service().DeleteAsync(name)).Should().ThrowAsync<NotFoundException>();
        }
        Directory.Exists(Path.Combine(_directory, "directory.dump")).Should().BeTrue();
    }

    [UnixFact]
    public async Task SymbolicLinks_AreExcludedAndCannotBeReadOrDeleted()
    {
        Directory.CreateDirectory(_directory);
        var nested = Path.Combine(_directory, "nested");
        Directory.CreateDirectory(nested);
        var target = Path.Combine(nested, "target.dump");
        await File.WriteAllTextAsync(target, "must remain private");
        var link = Path.Combine(_directory, "linked.dump");
        File.CreateSymbolicLink(link, target);

        (await Service().ListAsync()).Should().BeEmpty();
        await FluentActions.Invoking(() => Service().OpenReadAsync("linked.dump")).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Invoking(() => Service().DeleteAsync("linked.dump")).Should().ThrowAsync<NotFoundException>();
        File.ReadAllText(target).Should().Be("must remain private");
        File.Exists(link).Should().BeTrue();
    }

    [Fact]
    public async Task CancelledRequests_DoNotCreateReadOrDeleteBackups()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "keep.dump");
        await File.WriteAllTextAsync(path, "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Invoking(() => Service().CreateBackupAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => Service().ListAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => Service().OpenReadAsync("keep.dump", cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Invoking(() => Service().DeleteAsync("keep.dump", cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        File.Exists(path).Should().BeTrue();
    }

    private PgDumpBackupService Service() => new(new ConfigurationBuilder().Build(),
        Options.Create(new BackupOptions { Directory = _directory }), NullLogger<PgDumpBackupService>.Instance);

    public void Dispose()
    {
        var cleanupPath = Path.GetFullPath(_directory);
        if (!string.Equals(Path.GetDirectoryName(cleanupPath), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(cleanupPath).StartsWith("funandchecks-backup-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected backup test directory.");
        if (Directory.Exists(_directory))
            Directory.Delete(cleanupPath, recursive: true);
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows())
                Skip = "Creating Windows symbolic links requires an OS privilege; Unix symlinks are tested without that privilege.";
        }
    }
}

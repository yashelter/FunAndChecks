using System.IO.Compression;
using FluentAssertions;
using FunAndChecks.Application.Backups;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Infrastructure.Backup;
using FunAndChecks.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FunAndChecks.Tests.Backups;

public class DatabaseRestoreServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"funandchecks-restore-tests-{Guid.NewGuid():N}");
    private readonly IDatabaseRestoreTarget _target = Substitute.For<IDatabaseRestoreTarget>();
    private readonly IDatabaseBackupService _backups = Substitute.For<IDatabaseBackupService>();
    private readonly IResultsCacheService _cache = Substitute.For<IResultsCacheService>();
    private readonly MaintenanceModeState _maintenance = new();

    private string BackupDirectory => Path.Combine(_root, "backups");
    private string AttachmentsDirectory => Path.Combine(_root, "attachments");

    public DatabaseRestoreServiceTests()
    {
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        _target.ReadMigrationIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(["20260101000000_Init"]);
        _target.GetKnownMigrations().Returns(["20260101000000_Init", "20261019205535_AddTaskGroups"]);
        _backups.CreateBackupAsync(Arg.Any<CancellationToken>()).Returns(Path.Combine(BackupDirectory, "safety.dump"));
    }

    [Fact]
    public async Task Restore_ReplacesDatabaseInMaintenance_ThenAttachments_AndReportsSafetyBackup()
    {
        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "old.dump"), "PGDMP");
        CreateArchive("old.attachments.zip", ("2026/01/restored", "restored file"));
        Directory.CreateDirectory(Path.Combine(AttachmentsDirectory, "2026", "10"));
        await File.WriteAllTextAsync(Path.Combine(AttachmentsDirectory, "2026", "10", "newer"), "newer file");
        var maintenanceDuringReplace = false;
        _target.ReplaceDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { maintenanceDuringReplace = _maintenance.IsActive; return Task.CompletedTask; });
        var sut = Service();

        sut.Start("old.dump").State.Should().Be(RestoreState.Running);
        await sut.Completion;

        var status = sut.GetStatus();
        status.State.Should().Be(RestoreState.Succeeded, status.Error);
        status.SafetyBackupFileName.Should().Be("safety.dump");
        status.WarningCode.Should().BeNull();
        maintenanceDuringReplace.Should().BeTrue();
        _maintenance.IsActive.Should().BeFalse();
        Received.InOrder(() =>
        {
            _backups.CreateBackupAsync(Arg.Any<CancellationToken>());
            _target.TerminateOtherSessionsAsync(Arg.Any<CancellationToken>());
            _target.ReplaceDatabaseAsync(Path.Combine(BackupDirectory, "old.dump"), Arg.Any<CancellationToken>());
            _target.MigrateAndSeedAsync(Arg.Any<CancellationToken>());
            _cache.Clear();
        });
        File.ReadAllText(Path.Combine(AttachmentsDirectory, "2026", "01", "restored")).Should().Be("restored file");
        File.Exists(Path.Combine(AttachmentsDirectory, "2026", "10", "newer")).Should().BeFalse();
        Directory.EnumerateDirectories(AttachmentsDirectory, ".restore-*").Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_WithoutAttachmentsArchive_KeepsFilesAndWarns()
    {
        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "old.dump"), "PGDMP");
        await File.WriteAllTextAsync(Path.Combine(AttachmentsDirectory, "keep"), "keep");
        var sut = Service();

        sut.Start("old.dump");
        await sut.Completion;

        sut.GetStatus().State.Should().Be(RestoreState.Succeeded);
        sut.GetStatus().WarningCode.Should().Be("restore.attachments_missing");
        File.Exists(Path.Combine(AttachmentsDirectory, "keep")).Should().BeTrue();
    }

    [Fact]
    public async Task Restore_FromNewerApplicationVersion_FailsBeforeTouchingAnything()
    {
        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "new.dump"), "PGDMP");
        _target.ReadMigrationIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(["20260101000000_Init", "20991231000000_FromTheFuture"]);
        var sut = Service();

        sut.Start("new.dump");
        await sut.Completion;

        sut.GetStatus().State.Should().Be(RestoreState.Failed);
        sut.GetStatus().ErrorCode.Should().Be("restore.newer_schema");
        await _backups.DidNotReceive().CreateBackupAsync(Arg.Any<CancellationToken>());
        await _target.DidNotReceive().ReplaceDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _maintenance.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_WhenSafetyBackupFails_DoesNotReplaceDatabase()
    {
        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "old.dump"), "PGDMP");
        _backups.CreateBackupAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk full"));
        var sut = Service();

        sut.Start("old.dump");
        await sut.Completion;

        sut.GetStatus().ErrorCode.Should().Be("restore.safety_backup_failed");
        await _target.DidNotReceive().ReplaceDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _maintenance.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_WhenDatabaseReplaceFails_ReportsCodeAndLeavesMaintenance()
    {
        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "old.dump"), "PGDMP");
        await File.WriteAllTextAsync(Path.Combine(AttachmentsDirectory, "keep"), "keep");
        CreateArchive("old.attachments.zip", ("other", "other"));
        _target.ReplaceDatabaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RestoreStepException("restore.database_failed", "psql failed"));
        var sut = Service();

        sut.Start("old.dump");
        await sut.Completion;

        var status = sut.GetStatus();
        status.State.Should().Be(RestoreState.Failed);
        status.ErrorCode.Should().Be("restore.database_failed");
        status.SafetyBackupFileName.Should().Be("safety.dump");
        _maintenance.IsActive.Should().BeFalse();
        _cache.DidNotReceive().Clear();
        // Вложения меняются только после успешной замены БД.
        File.Exists(Path.Combine(AttachmentsDirectory, "keep")).Should().BeTrue();
        File.Exists(Path.Combine(AttachmentsDirectory, "other")).Should().BeFalse();
    }

    [Fact]
    public async Task Start_WhileRestoreIsRunning_ThrowsConflict()
    {
        await File.WriteAllTextAsync(Path.Combine(BackupDirectory, "old.dump"), "PGDMP");
        var gate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _target.ReadMigrationIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var sut = Service();

        sut.Start("old.dump");
        var act = () => sut.Start("old.dump");

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("backup.restore_in_progress");
        gate.SetResult(["20260101000000_Init"]);
        await sut.Completion;
        sut.GetStatus().State.Should().Be(RestoreState.Succeeded);
    }

    [Theory]
    [InlineData("old.attachments.zip")]
    [InlineData("..\\outside.dump")]
    [InlineData("missing.dump")]
    public void Start_RejectsNonDumpOrMissingFiles(string fileName)
    {
        File.WriteAllText(Path.Combine(BackupDirectory, "old.attachments.zip"), "zip");
        var act = () => Service().Start(fileName);
        act.Should().Throw<Exception>().Which.Should().Match<Exception>(e =>
            e is FluentValidation.ValidationException || e is NotFoundException);
    }

    [Fact]
    public void RestoreAttachments_RejectsEntriesEscapingTheDirectory()
    {
        var dumpPath = Path.Combine(BackupDirectory, "evil.dump");
        File.WriteAllText(dumpPath, "PGDMP");
        File.WriteAllText(Path.Combine(AttachmentsDirectory, "keep"), "keep");
        CreateArchive("evil.attachments.zip", ("../escaped", "boom"));

        Service().RestoreAttachments("evil.dump", dumpPath).Should().Be("restore.attachments_failed");

        File.Exists(Path.Combine(_root, "escaped")).Should().BeFalse();
        File.Exists(Path.Combine(AttachmentsDirectory, "keep")).Should().BeTrue();
        Directory.EnumerateDirectories(AttachmentsDirectory, ".restore-*").Should().BeEmpty();
    }

    [Fact]
    public void ParseMigrationIds_ReadsRowsOfMigrationsHistoryCopyBlock()
    {
        const string sql = """
            SET statement_timeout = 0;
            COPY public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") FROM stdin;
            20260101000000_Init	10.0.0
            20261019205535_AddTaskGroups	10.0.9
            \.

            """;

        PostgresRestoreTarget.ParseMigrationIds(sql).Should().Equal("20260101000000_Init", "20261019205535_AddTaskGroups");
        PostgresRestoreTarget.ParseMigrationIds("-- empty dump\n").Should().BeEmpty();
    }

    private DatabaseRestoreService Service()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _backups);
        var provider = services.BuildServiceProvider();
        return new DatabaseRestoreService(provider.GetRequiredService<IServiceScopeFactory>(), _target, _maintenance, _cache,
            Options.Create(new BackupOptions { Directory = BackupDirectory }),
            Options.Create(new AttachmentStorageOptions { RootDirectory = AttachmentsDirectory }),
            NullLogger<DatabaseRestoreService>.Instance);
    }

    private void CreateArchive(string name, params (string Entry, string Content)[] entries)
    {
        using var archive = ZipFile.Open(Path.Combine(BackupDirectory, name), ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write(content);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

using Frontend.Shared.Api;
using Frontend.Shared.Resources;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Maintenance
{
    [Inject] private BackupApi Backup { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;

    private bool _backupRunning;
    private string? _lastBackupPath;
    private List<BackupFileDto> _backups = [];
    private bool _loading, _loadFailed, _downloading, _confirmingDelete;
    private string? _busyFile;
    private readonly CancellationTokenSource _lifetime = new();
    private bool Busy => _backupRunning || _loading || _confirmingDelete || _busyFile is not null;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (_loading || _lifetime.IsCancellationRequested) return;
        _loading = true;
        _loadFailed = false;
        try { _backups = await Backup.ListAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex)
        {
            _loadFailed = true;
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _loading = false; }
    }

    private async Task BackupAsync()
    {
        if (Busy) return;
        _backupRunning = true;
        try
        {
            var result = await Backup.CreateAsync(_lifetime.Token);
            _lastBackupPath = result.Path;
            Snackbar.Add(Loc["Maintenance_BackupDone"], Severity.Success);
            await LoadAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _backupRunning = false;
        }
    }

    private async Task DownloadAsync(BackupFileDto file)
    {
        if (Busy) return;
        _busyFile = file.FileName;
        _downloading = true;
        try
        {
            await Backup.DownloadAsync(file.FileName, stream => Downloader.DownloadAsync(file.FileName, stream), _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _busyFile = null; _downloading = false; }
    }

    private async Task DeleteAsync(BackupFileDto file)
    {
        if (Busy) return;
        _confirmingDelete = true;
        bool? confirmed;
        try
        {
            var message = WebUtility.HtmlEncode(Loc["Maintenance_BackupDeleteConfirm", file.FileName].Value);
            confirmed = await Dialogs.ShowMessageBoxAsync(Loc["Maintenance_BackupDeleteTitle"],
                new MarkupString($"<span class=\"fc-backup-delete-copy\">{message}</span>"),
                yesText: Loc["Maintenance_BackupDelete"], cancelText: Loc["Common_Cancel"]);
        }
        finally { _confirmingDelete = false; }
        if (confirmed != true || Busy || _lifetime.IsCancellationRequested) return;
        _busyFile = file.FileName;
        try
        {
            await Backup.DeleteAsync(file.FileName, _lifetime.Token);
            Snackbar.Add(Loc["Maintenance_BackupDeleted"], Severity.Success);
            await LoadAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _busyFile = null; }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value.ToString(unit == 0 ? "0" : "0.##", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}

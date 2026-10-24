using Frontend.Shared.Api;
using Frontend.Shared.Resources;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private JwtAuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private bool _backupRunning;
    private string? _lastBackupPath;
    private List<BackupFileDto> _backups = [];
    private bool _loading, _loadFailed, _downloading, _confirmingDelete;
    private string? _busyFile;
    private readonly CancellationTokenSource _lifetime = new();
    private RestoreStatusDto? _restore;
    private IBrowserFile? _dumpFile, _archiveFile;
    private bool _uploading, _confirmingRestore, _polling;
    // Новое значение пересоздаёт InputFile — так сбрасывается выбранный файл после загрузки.
    private int _inputGeneration;
    private bool RestoreRunning => _restore?.State == RestoreState.Running;
    private bool Busy => _backupRunning || _loading || _confirmingDelete || _busyFile is not null
                         || _uploading || _confirmingRestore || RestoreRunning;

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadAsync(), LoadRestoreStatusAsync());
        // Восстановление могло быть запущено раньше (другая вкладка, перезагрузка страницы).
        if (RestoreRunning) _ = PollRestoreAsync();
    }

    private async Task LoadRestoreStatusAsync()
    {
        try { _restore = await Backup.GetRestoreStatusAsync(_lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException) { /* Статус — дополнительная информация, список бэкапов важнее. */ }
    }

    private void OnDumpSelected(InputFileChangeEventArgs args) => _dumpFile = args.File;

    private void OnArchiveSelected(InputFileChangeEventArgs args) => _archiveFile = args.File;

    private async Task UploadAsync()
    {
        if (Busy || _dumpFile is not { } dumpFile) return;
        if (dumpFile.Size + (_archiveFile?.Size ?? 0) > BackupApi.MaxUploadBytes)
        {
            Snackbar.Add(Loc["Maintenance_RestoreTooLarge", FormatSize(BackupApi.MaxUploadBytes)], Severity.Warning);
            return;
        }

        _uploading = true;
        BackupFileDto? uploaded = null;
        try
        {
            await using var dump = dumpFile.OpenReadStream(BackupApi.MaxUploadBytes, _lifetime.Token);
            await using var archive = _archiveFile?.OpenReadStream(BackupApi.MaxUploadBytes, _lifetime.Token);
            uploaded = await Backup.UploadAsync(dump, dumpFile.Name, archive, _archiveFile?.Name, _lifetime.Token);
            _dumpFile = null;
            _archiveFile = null;
            _inputGeneration++;
            Snackbar.Add(Loc["Maintenance_RestoreUploaded"], Severity.Success);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _uploading = false; }

        if (uploaded is null) return;
        await LoadAsync();
        // Загружают обычно ради восстановления — сразу предлагаем его (с подтверждением).
        await RestoreAsync(uploaded.FileName);
    }

    private async Task RestoreAsync(string fileName)
    {
        if (Busy || _lifetime.IsCancellationRequested) return;
        _confirmingRestore = true;
        bool confirmed;
        try
        {
            var parameters = new DialogParameters<Frontend.Admin.Dialogs.RestoreConfirmDialog> { { x => x.FileName, fileName } };
            var dialog = await Dialogs.ShowAsync<Frontend.Admin.Dialogs.RestoreConfirmDialog>(Loc["Maintenance_RestoreConfirmTitle"], parameters);
            var result = await dialog.Result;
            confirmed = result is { Canceled: false };
        }
        finally { _confirmingRestore = false; }
        if (!confirmed || Busy || _lifetime.IsCancellationRequested) return;

        try
        {
            _restore = await Backup.RestoreAsync(fileName, _lifetime.Token);
            _ = PollRestoreAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    /// <summary>Опрос статуса раз в 2 секунды, пока восстановление идёт.</summary>
    private async Task PollRestoreAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            while (RestoreRunning)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
                try { _restore = await Backup.GetRestoreStatusAsync(_lifetime.Token); }
                catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Сессия не пережила восстановление (пользователя нет в бэкапе) — дальше только вход заново.
                    _restore = _restore! with { State = RestoreState.Succeeded };
                    break;
                }
                catch (ApiException) { /* 503 и сбои сети во время восстановления ожидаемы — пробуем снова. */ }
                await InvokeAsync(StateHasChanged);
            }
            await LoadAsync();
            if (_restore?.State == RestoreState.Succeeded) Snackbar.Add(Loc["Maintenance_RestoreDone"], Severity.Success);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            _polling = false;
            if (!_lifetime.IsCancellationRequested) await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Сессии из старой БД недействительны — выходим и входим заново.</summary>
    private async Task ReloginAsync()
    {
        await Auth.LogoutAsync();
        AuthState.NotifyAuthenticationStateChanged();
        Nav.NavigateTo("/login");
    }

    private string RestoreStepText(string? step) => step switch
    {
        "validating" => Loc["Maintenance_RestoreStepValidating"],
        "safety_backup" => Loc["Maintenance_RestoreStepSafetyBackup"],
        "restoring_database" => Loc["Maintenance_RestoreStepDatabase"],
        "migrating" => Loc["Maintenance_RestoreStepMigrating"],
        "restoring_attachments" => Loc["Maintenance_RestoreStepAttachments"],
        _ => "",
    };

    private string RestoreCodeText(string? code) => code switch
    {
        "restore.invalid_dump" => Loc["Maintenance_RestoreErrorInvalidDump"],
        "restore.newer_schema" => Loc["Maintenance_RestoreErrorNewerSchema"],
        "restore.safety_backup_failed" => Loc["Maintenance_RestoreErrorSafetyBackup"],
        "restore.tools_unavailable" => Loc["Maintenance_RestoreErrorTools"],
        "restore.database_failed" => Loc["Maintenance_RestoreErrorDatabase"],
        "restore.migration_failed" => Loc["Maintenance_RestoreErrorMigration"],
        "restore.attachments_missing" => Loc["Maintenance_RestoreWarningNoAttachments"],
        "restore.attachments_failed" => Loc["Maintenance_RestoreWarningAttachments"],
        _ => Loc["Maintenance_RestoreErrorGeneric"],
    };

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

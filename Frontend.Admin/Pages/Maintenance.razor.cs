using Frontend.Shared.Api;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Maintenance
{
    [Inject] private BackupApi Backup { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private bool _backupRunning;
    private string? _lastBackupPath;

    private async Task BackupAsync()
    {
        _backupRunning = true;
        try
        {
            var result = await Backup.CreateAsync();
            _lastBackupPath = result.Path;
            Snackbar.Add(Loc["Maintenance_BackupDone"], Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _backupRunning = false;
        }
    }
}

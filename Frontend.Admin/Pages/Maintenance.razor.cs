using Frontend.Shared.Api;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Maintenance
{
    [Inject] private BackupApi Backup { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private bool _backupRunning;
    private string? _lastBackupPath;

    private async Task BackupAsync()
    {
        _backupRunning = true;
        try
        {
            var result = await Backup.CreateAsync();
            _lastBackupPath = result.Path;
            Snackbar.Add("Резервная копия создана.", Severity.Success);
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

using Frontend.Shared.Api;
using Frontend.Admin.Dialogs;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class AdminManagement
{
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private List<AdminDto> _admins = [];
    private bool _loading = true;

    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _color;
    private string? _letter;
    private bool _isSuperAdmin;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await ReloadAdminsAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAdminsAsync() => _admins = await Admins.GetAllAsync();

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_password))
        {
            Snackbar.Add(Loc["AdminMgmt_EmailPasswordRequired"], Severity.Warning);
            return;
        }

        try
        {
            await Admins.CreateAsync(new CreateAdminRequest(
                _firstName.Trim(), _lastName.Trim(), _email.Trim(), _password,
                string.IsNullOrWhiteSpace(_color) ? null : _color.Trim(),
                string.IsNullOrWhiteSpace(_letter) ? null : _letter.Trim(),
                _isSuperAdmin));

            Snackbar.Add(Loc["AdminMgmt_Created"], Severity.Success);
            _firstName = _lastName = _email = _password = string.Empty;
            _color = _letter = null;
            _isSuperAdmin = false;
            await ReloadAdminsAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task DeleteAsync(AdminDto admin)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(
            Loc["AdminMgmt_DeleteTitle"],
            string.Format(Loc["AdminMgmt_DeleteConfirm"], admin.LastName, admin.FirstName),
            yesText: Loc["Common_Delete"], cancelText: Loc["Common_Cancel"]);

        if (confirmed != true)
            return;

        try
        {
            await Admins.DeleteAsync(admin.Id);
            Snackbar.Add(Loc["AdminMgmt_Deleted"], Severity.Success);
            await ReloadAdminsAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OpenAccessAsync(AdminDto admin)
    {
        var parameters = new DialogParameters<AdminRestrictionsDialog> { { x => x.Admin, admin } };
        await DialogService.ShowAsync<AdminRestrictionsDialog>(
            Loc["AdminMgmt_RestrictionsTitle", admin.LastName, admin.FirstName], parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = false, BackdropClick = false });
    }
}

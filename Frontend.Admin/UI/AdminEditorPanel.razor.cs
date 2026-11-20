using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.UI;

/// <summary>
/// Панель выбранного администратора: профиль, вход и роль сохраняются одной кнопкой;
/// доступ — через существующий диалог, удаление — отдельной секцией.
/// </summary>
public partial class AdminEditorPanel : IDisposable
{
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;

    [Parameter, EditorRequired] public AdminDetailsDto Admin { get; set; } = null!;
    [Parameter] public bool ActorIsSuperAdmin { get; set; }
    [Parameter] public Guid? CurrentUserId { get; set; }
    /// <summary>После сохранения — родитель перезагружает список.</summary>
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback<Guid> OnDeleted { get; set; }

    private AdminDetailsDto? _source;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string? _letter;
    private string? _color;
    private string _email = string.Empty;
    private string _newPassword = string.Empty;
    private bool _isSuperAdmin;
    private bool _showPassword;
    private bool _saving;

    private UnsavedChangesTracker.Registration? _edits;
    private bool CanEdit => !Admin.IsSuperAdmin || ActorIsSuperAdmin;

    private bool IsSelf => CurrentUserId == Admin.Id;
    private bool CannotDemoteSelf => IsSelf && Admin.IsSuperAdmin;

    private string? FirstNameError => string.IsNullOrWhiteSpace(_firstName) ? Loc["AdminMgmt_Required"].Value : null;
    private string? LastNameError => string.IsNullOrWhiteSpace(_lastName) ? Loc["AdminMgmt_Required"].Value : null;
    private string? EmailError => AdminFormRules.EmailError(_email, Loc);
    private string? PasswordError => _newPassword.Length is > 0 and < AdminFormRules.MinPasswordLength
        ? Loc["AdminMgmt_PasswordTooShort", AdminFormRules.MinPasswordLength].Value : null;
    private bool IsValid => FirstNameError is null && LastNameError is null && EmailError is null && PasswordError is null;

    private bool EmailChanged => EmailDiffers(Admin);

    /// <summary>Есть несохранённые правки — родитель спрашивает перед сменой админа.</summary>
    public bool HasChanges => DiffersFrom(Admin);

    private bool EmailDiffers(AdminDetailsDto baseline) =>
        !string.Equals(_email.Trim(), baseline.Email ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private bool DiffersFrom(AdminDetailsDto baseline) =>
        _firstName != baseline.FirstName || _lastName != baseline.LastName ||
        !string.Equals(Normalize(_letter), Normalize(baseline.Letter), StringComparison.Ordinal) ||
        !string.Equals(Normalize(_color), Normalize(baseline.Color), StringComparison.OrdinalIgnoreCase) ||
        EmailDiffers(baseline) || _newPassword.Length > 0 || _isSuperAdmin != baseline.IsSuperAdmin;

    protected override void OnInitialized() => _edits = Dirty.Register();

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Admin, _source))
            return;
        var otherAdmin = _source?.Id != Admin.Id;
        // Список перезагрузили (например, создан другой админ) — не теряем начатые правки.
        if (!otherAdmin && _source is not null && DiffersFrom(_source))
        {
            _source = Admin;
            return;
        }
        LoadFrom(Admin);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (HasChanges) _edits?.MarkDirty(); else _edits?.MarkClean();
    }

    private void LoadFrom(AdminDetailsDto admin)
    {
        _source = admin;
        _firstName = admin.FirstName;
        _lastName = admin.LastName;
        _letter = admin.Letter;
        _color = admin.Color;
        _email = admin.Email ?? string.Empty;
        _newPassword = string.Empty;
        _showPassword = false;
        _isSuperAdmin = admin.IsSuperAdmin;
    }

    /// <summary>Отменить правки (кнопка или подтверждённый переход к другому админу).</summary>
    public void Discard()
    {
        LoadFrom(Admin);
        _edits?.MarkClean();
        StateHasChanged();
    }

    private async Task SaveAsync()
    {
        if (_saving || !CanEdit || !DiffersFrom(Admin) || !IsValid) return;
        _saving = true;
        try
        {
            var request = new UpdateAdminRequest(
                _firstName.Trim(), _lastName.Trim(), Normalize(_color), Normalize(_letter),
                Email: EmailChanged ? _email.Trim() : null,
                NewPassword: _newPassword.Length > 0 ? _newPassword : null,
                IsSuperAdmin: _isSuperAdmin != Admin.IsSuperAdmin ? _isSuperAdmin : null);
            await Admins.UpdateAsync(Admin.Id, request);
            _newPassword = string.Empty;
            _edits?.MarkClean();
            Snackbar.Add(Loc["Common_Saved"], Severity.Success);
            await OnSaved.InvokeAsync(Admin.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task OpenAccessAsync()
    {
        if (!CanEdit || _saving)
        {
            return;
        }

        DialogParameters<AdminRestrictionsDialog> parameters = new() { { x => x.Admin, ToAdminDto(Admin) } };
        await DialogService.ShowAsync<AdminRestrictionsDialog>(
            Loc["AdminMgmt_RestrictionsTitle", Admin.LastName, Admin.FirstName], parameters,
            new DialogOptions { MaxWidth = MaxWidth.Large, FullWidth = true, CloseOnEscapeKey = false, BackdropClick = false });
    }

    private async Task DeleteAsync()
    {
        if (!CanEdit || !await Confirm.ConfirmDeleteAsync(Admin.FullName))
            return;
        try
        {
            await Admins.DeleteAsync(Admin.Id);
            _edits?.MarkClean();
            Snackbar.Add(Loc["AdminMgmt_Deleted"], Severity.Success);
            await OnDeleted.InvokeAsync(Admin.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private static AdminDto ToAdminDto(AdminDetailsDto admin) =>
        new(admin.Id, admin.FirstName, admin.LastName, admin.Color, admin.Letter);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _edits?.Dispose();
}

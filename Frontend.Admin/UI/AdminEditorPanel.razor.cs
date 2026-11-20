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
    [Inject] private AdminPermissionReader PermissionReader { get; set; } = null!;
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
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

    private AccessSummary? _access;
    private AdminAccessDto? _adminAccess;
    private HashSet<string> _permissionSelection = new(StringComparer.Ordinal);
    private List<string> _expectedPermissions = [];
    private bool _permissionsStale;
    private bool _permissionsSaving;
    private bool _actorDemoted;

    private bool CanEdit => !_actorDemoted && (!Admin.IsSuperAdmin || ActorIsSuperAdmin);
    private bool PermissionsChanged => !_permissionSelection.SetEquals(_expectedPermissions);
    private bool _accessFailed;
    private int _accessVersion;
    private UnsavedChangesTracker.Registration? _edits;

    private sealed record AccessSummary(int AllowedSubjects, int TotalSubjects, int RestrictedGroups);

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
    public bool HasChanges => DiffersFrom(Admin) || PermissionsChanged;

    private bool EmailDiffers(AdminDetailsDto baseline) =>
        !string.Equals(_email.Trim(), baseline.Email ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private bool DiffersFrom(AdminDetailsDto baseline) =>
        _firstName != baseline.FirstName || _lastName != baseline.LastName ||
        !string.Equals(Normalize(_letter), Normalize(baseline.Letter), StringComparison.Ordinal) ||
        !string.Equals(Normalize(_color), Normalize(baseline.Color), StringComparison.OrdinalIgnoreCase) ||
        EmailDiffers(baseline) || _newPassword.Length > 0 || _isSuperAdmin != baseline.IsSuperAdmin;

    protected override void OnInitialized() => _edits = Dirty.Register();

    protected override async Task OnParametersSetAsync()
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
        if (otherAdmin || _access is null)
            await LoadAccessAsync();
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
        _permissionSelection = _expectedPermissions.ToHashSet(StringComparer.Ordinal);
        _edits?.MarkClean();
        StateHasChanged();
    }

    private async Task LoadAccessAsync(bool keepSelection = false, CancellationToken cancellationToken = default)
    {
        var version = ++_accessVersion;
        _access = null;
        _accessFailed = false;
        _adminAccess = null;
        try
        {
            var subjectsTask = Subjects.GetAllAsync();
            Task<AdminAccessDto> accessTask = Admins.GetAccessAsync(Admin.Id, cancellationToken);
            await Task.WhenAll(subjectsTask, accessTask);
            if (version != _accessVersion) return;
            var subjectIds = (await subjectsTask).Select(s => s.Id).ToHashSet();
            AdminAccessDto access = await accessTask;
            _adminAccess = access;
            _expectedPermissions = access.ExplicitPermissions ?? [];
            if (!keepSelection)
            {
                _permissionSelection = _expectedPermissions.ToHashSet(StringComparer.Ordinal);
            }
            _permissionsStale = false;
            _access = new AccessSummary(
                (access.AllowedSubjectIds ?? []).Count(subjectIds.Contains), subjectIds.Count, access.RestrictedGroupIds.Count);
        }
        catch (ApiException)
        {
            if (version == _accessVersion) _accessFailed = true;
        }
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
        if (!CanEdit) { return; }
        var parameters = new DialogParameters<AdminRestrictionsDialog> { { x => x.Admin, ToAdminDto(Admin) } };
        var dialog = await DialogService.ShowAsync<AdminRestrictionsDialog>(
            Loc["AdminMgmt_RestrictionsTitle", Admin.LastName, Admin.FirstName], parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = false, BackdropClick = false });
        var result = await dialog.Result;
        if (result is { Canceled: false })
            await LoadAccessAsync();
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

    private void TogglePermission((string Key, bool Selected) change)
    {
        if (change.Selected)
        {
            _permissionSelection.Add(change.Key);
        }
        else
        {
            _permissionSelection.Remove(change.Key);
        }
    }

    private void SelectAllPermissions()
    {
        _permissionSelection = (_adminAccess?.PermissionCatalog ?? []).ToHashSet(StringComparer.Ordinal);
    }

    private Task ReloadPermissionBaselineAsync() => LoadAccessAsync(keepSelection: true);

    private async Task SavePermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanEdit || !PermissionsChanged || _permissionsStale || _permissionsSaving || _adminAccess is null)
        {
            return;
        }

        _permissionsSaving = true;
        try
        {
            List<string> keys = _permissionSelection.Order(StringComparer.Ordinal).ToList();
            await Admins.ReplaceSpecialPermissionsAsync(Admin.Id, new(keys, _expectedPermissions), cancellationToken);
            await LoadAccessAsync(cancellationToken: cancellationToken);
            Snackbar.Add(Loc["Common_Saved"], Severity.Success);
        }
        catch (ApiException ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _permissionsStale = true;
                Snackbar.Add(Loc["AdminPermissions_Changed"], Severity.Warning);
            }
            else
            {
                Snackbar.Add(ex.Message, Severity.Error);
                if (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    AdminAccessDto? mine = await PermissionReader.ReadAsync(cancellationToken);
                    _actorDemoted = mine?.Has(FunAndChecks.Domain.Constants.AdminPermissions.ADMINS_MANAGE) != true ||
                        (Admin.IsSuperAdmin && mine.IsSuperAdmin != true);
                    await LoadAccessAsync(keepSelection: true, cancellationToken);
                }
            }
        }
        finally
        {
            _permissionsSaving = false;
        }
    }

    private static AdminDto ToAdminDto(AdminDetailsDto admin) =>
        new(admin.Id, admin.FirstName, admin.LastName, admin.Color, admin.Letter);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void Dispose() => _edits?.Dispose();
}

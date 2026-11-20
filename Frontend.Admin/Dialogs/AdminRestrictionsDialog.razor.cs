using System.Net;
using Frontend.Admin.UI;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public sealed partial class AdminRestrictionsDialog : IDisposable
{
    private const int PERMISSIONS_TAB = 0;

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;
    [Parameter, EditorRequired] public AdminDto Admin { get; set; } = null!;

    private readonly CancellationTokenSource _lifetime = new();
    private UnsavedChangesTracker.Registration? _edits;
    private AdminAccessDto? _access;
    private HashSet<string> _selectedPermissions = new(StringComparer.Ordinal);
    private List<string> _originalPermissions = [];
    private List<RestrictionList.Entry> _subjects = [];
    private List<RestrictionList.Entry> _groups = [];
    private HashSet<int> _restrictedSubjects = [];
    private HashSet<int> _restrictedGroups = [];
    private HashSet<int> _originalSubjects = [];
    private HashSet<int> _originalGroups = [];
    private int _activeTab;
    private bool _permissionsLoading;
    private bool _permissionsStale;
    private bool _scopesLoading;
    private bool _scopesLoaded;
    private bool _saving;

    private bool IsPermissionsTab => _activeTab == PERMISSIONS_TAB;
    private bool PermissionsChanged => !_selectedPermissions.SetEquals(_originalPermissions);
    private bool ScopesChanged => !_originalSubjects.SetEquals(_restrictedSubjects) || !_originalGroups.SetEquals(_restrictedGroups);

    protected override async Task OnInitializedAsync()
    {
        _edits = Dirty.Register();
        await LoadPermissionsAsync(false, _lifetime.Token);
    }

    private async Task ChangeTabAsync(int tab, CancellationToken cancellationToken)
    {
        _activeTab = tab;
        if (!IsPermissionsTab && !_scopesLoaded && !_scopesLoading)
        {
            await LoadScopesAsync(cancellationToken);
        }
    }

    private async Task LoadPermissionsAsync(bool keepSelection, CancellationToken cancellationToken)
    {
        _permissionsLoading = true;
        try
        {
            _access = await Admins.GetAccessAsync(Admin.Id, cancellationToken);
            _originalPermissions = _access.ExplicitPermissions ?? [];
            if (!keepSelection)
            {
                _selectedPermissions = _originalPermissions.ToHashSet(StringComparer.Ordinal);
            }
            _permissionsStale = false;
            TrackEdits();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _permissionsLoading = false;
        }
    }

    private async Task LoadScopesAsync(CancellationToken cancellationToken)
    {
        _scopesLoading = true;
        try
        {
            Task<List<SubjectDto>> subjectTask = Subjects.GetAllAsync(cancellationToken);
            Task<List<GroupDto>> groupTask = Groups.GetAllAsync(cancellationToken);
            await Task.WhenAll(subjectTask, groupTask);
            _subjects = (await subjectTask).Select(subject => new RestrictionList.Entry(subject.Id, subject.Name)).ToList();
            _groups = (await groupTask).Select(group => new RestrictionList.Entry(group.Id, group.Name)).ToList();

            // Fetch the scope after the catalog so newly created subjects count as ungranted.
            AdminAccessDto scope = await Admins.GetAccessAsync(Admin.Id, cancellationToken);
            HashSet<int> allowedSubjects = (scope.ExplicitSubjectIds ?? scope.AllowedSubjectIds ?? []).ToHashSet();
            _restrictedSubjects = _subjects.Where(subject => !allowedSubjects.Contains(subject.Id)).Select(subject => subject.Id).ToHashSet();
            _restrictedGroups = [.. scope.RestrictedGroupIds];
            _originalSubjects = [.. _restrictedSubjects];
            _originalGroups = [.. _restrictedGroups];
            _scopesLoaded = true;
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _scopesLoading = false;
        }
    }

    private void TogglePermission((string Key, bool Selected) change)
    {
        SetMembership(_selectedPermissions, change.Key, change.Selected);
        TrackEdits();
    }

    private void SelectAllPermissions()
    {
        _selectedPermissions = (_access?.PermissionCatalog ?? []).ToHashSet(StringComparer.Ordinal);
        TrackEdits();
    }

    private void OnSubjectChanged(RestrictionList.RestrictionChange change)
    {
        SetMembership(_restrictedSubjects, change.Id, change.Restricted);
        TrackEdits();
    }

    private void OnGroupChanged(RestrictionList.RestrictionChange change)
    {
        SetMembership(_restrictedGroups, change.Id, change.Restricted);
        TrackEdits();
    }

    private void SetAllScopes(bool restricted)
    {
        _restrictedSubjects = restricted ? _subjects.Select(subject => subject.Id).ToHashSet() : [];
        _restrictedGroups = restricted ? _groups.Select(group => group.Id).ToHashSet() : [];
        TrackEdits();
    }

    private static void SetMembership<T>(HashSet<T> items, T value, bool included)
    {
        if (included)
        {
            items.Add(value);
        }
        else
        {
            items.Remove(value);
        }
    }

    private void TrackEdits()
    {
        if (PermissionsChanged || ScopesChanged)
        {
            _edits?.MarkDirty();
        }
        else
        {
            _edits?.MarkClean();
        }
    }

    private async Task SavePermissionsAsync(CancellationToken cancellationToken)
    {
        if (_saving || _permissionsLoading || _permissionsStale || !PermissionsChanged ||
            _access?.PermissionCatalog is not { Count: > 0 })
        {
            return;
        }

        _saving = true;
        try
        {
            List<string> keys = _selectedPermissions.Order(StringComparer.Ordinal).ToList();
            await Admins.ReplaceSpecialPermissionsAsync(Admin.Id, new(keys, _originalPermissions), cancellationToken);
            _originalPermissions = keys;
            _access = _access with
            {
                ExplicitPermissions = keys,
                EffectivePermissions = _access.IsSuperAdmin ? _access.PermissionCatalog : keys,
            };
            TrackEdits();
            Snackbar.Add(Loc["Common_Saved"], Severity.Success);
        }
        catch (ApiException ex)
        {
            _permissionsStale = ex.StatusCode == HttpStatusCode.Conflict;
            Snackbar.Add(ex.Message, _permissionsStale ? Severity.Warning : Severity.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task SaveScopesAsync(CancellationToken cancellationToken)
    {
        if (_saving || !_scopesLoaded || !ScopesChanged)
        {
            return;
        }

        _saving = true;
        try
        {
            await Admins.ReplacePermissionsAsync(Admin.Id, new ReplaceAdminPermissionsRequest(
                _subjects.Where(subject => !_restrictedSubjects.Contains(subject.Id)).Select(subject => subject.Id).ToList(),
                [.. _restrictedGroups]), cancellationToken);
            _originalSubjects = [.. _restrictedSubjects];
            _originalGroups = [.. _restrictedGroups];
            TrackEdits();
            Snackbar.Add(Loc["Common_Saved"], Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task CloseAsync()
    {
        if ((PermissionsChanged || ScopesChanged) &&
            !await Confirm.ConfirmAsync(Loc["AdminMgmt_UnsavedTitle"],
                Loc["AdminMgmt_UnsavedText", Admin.FullName], Loc["AdminMgmt_Revert"]))
        {
            return;
        }

        _edits?.MarkClean();
        MudDialog.Close();
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _edits?.Dispose();
    }
}

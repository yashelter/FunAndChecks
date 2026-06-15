using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class AdminManagement
{
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<AdminDto> _admins = [];
    private List<SubjectDto> _subjects = [];
    private List<GroupDto> _groups = [];
    private bool _loading = true;

    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _color;
    private string? _letter;
    private bool _isSuperAdmin;

    private AdminDto? _accessAdmin;
    private HashSet<int> _restrictedSubjects = [];
    private HashSet<int> _restrictedGroups = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Subjects.GetAllAsync();
            _groups = await Groups.GetAllAsync();
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
            Snackbar.Add("Укажите email и пароль.", Severity.Warning);
            return;
        }

        try
        {
            await Admins.CreateAsync(new CreateAdminRequest(
                _firstName.Trim(), _lastName.Trim(), _email.Trim(), _password,
                string.IsNullOrWhiteSpace(_color) ? null : _color.Trim(),
                string.IsNullOrWhiteSpace(_letter) ? null : _letter.Trim(),
                _isSuperAdmin));

            Snackbar.Add("Администратор создан.", Severity.Success);
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
            "Удаление администратора",
            $"Удалить администратора {admin.LastName} {admin.FirstName}?",
            yesText: "Удалить", cancelText: "Отмена");

        if (confirmed != true)
            return;

        try
        {
            await Admins.DeleteAsync(admin.Id);
            Snackbar.Add("Администратор удалён.", Severity.Success);
            if (_accessAdmin?.Id == admin.Id)
                _accessAdmin = null;
            await ReloadAdminsAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OpenAccessAsync(AdminDto admin)
    {
        _accessAdmin = admin;
        try
        {
            var access = await Admins.GetAccessAsync(admin.Id);
            _restrictedSubjects = [.. access.RestrictedSubjectIds];
            _restrictedGroups = [.. access.RestrictedGroupIds];
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ToggleSubjectAsync(int subjectId, bool restricted)
    {
        if (_accessAdmin is null)
            return;

        try
        {
            await Admins.SetSubjectRestrictionAsync(_accessAdmin.Id, subjectId, restricted);
            if (restricted) _restrictedSubjects.Add(subjectId); else _restrictedSubjects.Remove(subjectId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ToggleGroupAsync(int groupId, bool restricted)
    {
        if (_accessAdmin is null)
            return;

        try
        {
            await Admins.SetGroupRestrictionAsync(_accessAdmin.Id, groupId, restricted);
            if (restricted) _restrictedGroups.Add(groupId); else _restrictedGroups.Remove(groupId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }
}

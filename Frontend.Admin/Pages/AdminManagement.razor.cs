using System.Security.Claims;
using Frontend.Admin.Dialogs;
using Frontend.Admin.UI;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

/// <summary>
/// Администраторы: список с поиском и панель выбранного (выбор хранится в ?id=).
/// Создание — редкое действие, оно в диалоге по кнопке в заголовке.
/// </summary>
public partial class AdminManagement
{
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "id")] public Guid? SelectedIdQuery { get; set; }

    private List<AdminDetailsDto> _admins = [];
    private bool _loading = true;
    private bool _loadFailed;
    private string? _search;
    private Guid? _selectedId;
    private Guid? _currentUserId;
    private AdminEditorPanel? _editor;

    private AdminDetailsDto? SelectedAdmin => _admins.FirstOrDefault(a => a.Id == _selectedId);
    private bool HasUnsavedChanges => _editor?.HasChanges == true;

    private IEnumerable<AdminDetailsDto> FilteredAdmins
    {
        get
        {
            var query = _search?.Trim();
            return string.IsNullOrEmpty(query)
                ? _admins
                : _admins.Where(a =>
                    $"{a.LastName} {a.FirstName}".Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    $"{a.FirstName} {a.LastName}".Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    (a.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthState.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            _currentUserId = userId;
        await LoadAsync();
    }

    protected override void OnParametersSet() => _selectedId = SelectedIdQuery;

    private async Task LoadAsync()
    {
        _loading = true;
        _loadFailed = false;
        try
        {
            _admins = await Admins.GetDetailsAsync();
        }
        catch (ApiException)
        {
            _loadFailed = true;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SelectAsync(Guid id)
    {
        if (id == _selectedId)
            return;
        if (!await ConfirmDiscardAsync())
            return;
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("id", id), replace: true);
    }

    private async Task OnItemKeyDownAsync(KeyboardEventArgs e, Guid id)
    {
        if (e.Key is "Enter" or " ")
            await SelectAsync(id);
    }

    /// <summary>Правки текущего админа теряются при переходе — спрашиваем и сбрасываем.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!HasUnsavedChanges)
            return true;
        var name = SelectedAdmin?.FullName ?? string.Empty;
        if (!await Confirm.ConfirmAsync(Loc["AdminMgmt_UnsavedTitle"], Loc["AdminMgmt_UnsavedText", name], Loc["AdminMgmt_Revert"]))
            return false;
        _editor?.Discard();
        return true;
    }

    private async Task OnBeforeInternalNavigationAsync(LocationChangingContext context)
    {
        // Смена ?id= обрабатывается в SelectAsync; здесь только уход со страницы.
        var target = Navigation.ToBaseRelativePath(context.TargetLocation).Split('?', '#')[0];
        var current = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0];
        if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
            return;
        if (!await ConfirmDiscardAsync())
            context.PreventNavigation();
    }

    private async Task OnAdminSavedAsync(Guid id) => await LoadAsync();

    private async Task OnAdminDeletedAsync(Guid id)
    {
        await LoadAsync();
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("id", (Guid?)null), replace: true);
    }

    private async Task OpenCreateAsync()
    {
        var dialog = await DialogService.ShowAsync<AdminCreateDialog>(string.Empty,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = false, BackdropClick = false });
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: Guid id })
            await OnAdminCreatedAsync(id);
    }

    private async Task OnAdminCreatedAsync(Guid id)
    {
        await LoadAsync();
        _search = null;
        StateHasChanged();
        if (await ConfirmDiscardAsync())
            Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("id", id), replace: true);
    }
}

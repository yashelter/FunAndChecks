using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Queues
{
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private List<QueueEventDto> _queues = [];
    private bool _loading = true;
    private bool _showPast;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task OnShowPastChangedAsync(bool value)
    {
        _showPast = value;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _queues = _showPast ? await QueuesApi.GetAllAsync() : await QueuesApi.GetActiveAsync();
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

    private void Open(int eventId) => Nav.NavigateTo($"/admin/queues/{eventId}");

    /// <summary>Новая очередь прямо со страницы очередей — та же форма, что в «Управлении».</summary>
    private async Task CreateAsync()
    {
        var dialog = await Dialogs.ShowAsync<Frontend.Admin.Dialogs.CreateQueueDialog>(Loc["Management_CreateQueue"],
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true });
        // Уведомление «Очередь создана» показывает сама форма; новая очередь появляется в списке.
        if (await dialog.Result is { Canceled: false }) await LoadAsync();
    }
}

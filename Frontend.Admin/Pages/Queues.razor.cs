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

    /// <summary>Вкладка: archive — «Архив» (в рамках доступных предметов), иначе «Текущие».</summary>
    [SupplyParameterFromQuery(Name = "view")] public string? View { get; set; }
    private bool Archive => string.Equals(View, "archive", StringComparison.OrdinalIgnoreCase);

    private List<QueueEventDto> _queues = [];
    private bool _loading = true;
    private bool? _shownArchive;

    protected override async Task OnParametersSetAsync()
    {
        if (_shownArchive == Archive) return;
        _shownArchive = Archive;
        await LoadAsync();
    }

    private void OnTabChanged(int index) => Nav.NavigateTo(index == 1 ? "/admin/queues?view=archive" : "/admin/queues");

    private async Task LoadAsync(bool quiet = false)
    {
        if (!quiet) _loading = true;
        var archive = Archive;
        try
        {
            var queues = archive ? await QueuesApi.GetArchiveAsync() : await QueuesApi.GetActiveAsync();
            // Пока грузилось, могли переключить вкладку: чужой ответ не показываем.
            if (archive == Archive) _queues = queues;
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

    private Task ReloadQuietAsync() => LoadAsync(quiet: true);

    /// <summary>Новая очередь прямо со страницы очередей — та же форма, что в «Управлении».</summary>
    private async Task CreateAsync()
    {
        var dialog = await Dialogs.ShowAsync<Frontend.Admin.Dialogs.CreateQueueDialog>(Loc["Management_CreateQueue"],
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true });
        // Уведомление «Очередь создана» показывает сама форма; новая очередь появляется в списке.
        if (await dialog.Result is { Canceled: false }) await LoadAsync();
    }
}

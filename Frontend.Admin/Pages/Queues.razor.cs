using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Queues
{
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<QueueEventDto> _queues = [];
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _queues = await QueuesApi.GetActiveAsync();
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
}

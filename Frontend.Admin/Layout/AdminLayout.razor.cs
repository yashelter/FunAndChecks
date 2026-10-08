using Frontend.Shared.Api;
using Frontend.Shared.Layout;
using Microsoft.AspNetCore.Components;

namespace Frontend.Admin.Layout;

public partial class AdminLayout : AppLayoutBase
{
    [Inject] private MeApi Me { get; set; } = null!;

    private bool _drawerOpen = true;
    private string? _userName;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var me = await Me.GetMeAsync();
            _userName = me.FullName;
            await Culture.SynchronizeAccountCultureAsync(me.PreferredCulture);
        }
        catch
        {
            // имя в шапке некритично
        }
    }

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
}

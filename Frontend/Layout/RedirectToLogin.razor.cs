using Microsoft.AspNetCore.Components;

namespace Frontend.Layout;

/// <summary>Перенаправляет неавторизованного пользователя на страницу входа.</summary>
public partial class RedirectToLogin : ComponentBase
{
    [Inject] private NavigationManager Nav { get; set; } = null!;

    protected override void OnInitialized() => Nav.NavigateTo("/login");
}

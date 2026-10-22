using Microsoft.AspNetCore.Components;

namespace Frontend.Layout;

/// <summary>
/// Перенаправляет неавторизованного пользователя на страницу входа. Текущий адрес уходит в returnUrl,
/// чтобы после входа открылась та же страница (ссылка на сообщение, опрос и т.п.).
/// </summary>
public partial class RedirectToLogin : ComponentBase
{
    [Inject] private NavigationManager Nav { get; set; } = null!;

    protected override void OnInitialized()
    {
        var current = "/" + Nav.ToBaseRelativePath(Nav.Uri);
        Nav.NavigateTo(current is "/" or "/login" ? "/login" : $"/login?returnUrl={Uri.EscapeDataString(current)}");
    }
}

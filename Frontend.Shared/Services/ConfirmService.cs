using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Shared.Services;

/// <summary>
/// Единое подтверждение необратимых действий вместо window.confirm: тот же вид, тема и язык,
/// что и у остального интерфейса. Текст выводится как обычная строка, не как разметка.
/// </summary>
public class ConfirmService(IDialogService dialogs, IStringLocalizer<AppStrings> loc)
{
    public async Task<bool> ConfirmAsync(string title, string message, string? confirmText = null) =>
        await dialogs.ShowMessageBoxAsync(title, message, yesText: confirmText ?? loc["Common_Delete"], cancelText: loc["Common_Cancel"]) == true;

    /// <summary>«Удалить «X»? Это нельзя отменить.»</summary>
    public Task<bool> ConfirmDeleteAsync(string name) =>
        ConfirmAsync(loc["Common_ConfirmDeleteTitle"], loc["Common_ConfirmDeleteText", name]);
}

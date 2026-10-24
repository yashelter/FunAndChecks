using System.Text.Json;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class CommentDialog : IDisposable
{
    private const string RecentKey = "fc-recent-rework-comments";
    private const int RecentLimit = 6;

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private string _comment = string.Empty;
    private List<string> _recent = [];
    private UnsavedChangesTracker.Registration _edits = null!;

    protected override async Task OnInitializedAsync()
    {
        _edits = Dirty.Register();
        try
        {
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", RecentKey);
            if (!string.IsNullOrEmpty(json)) _recent = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException) { _recent = []; }
    }

    private async Task KeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter" && (e.CtrlKey || e.MetaKey))
            await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        await RememberAsync(_comment);
        _edits.MarkClean();
        MudDialog.Close(DialogResult.Ok(_comment));
    }

    /// <summary>Последние причины возврата хранятся в браузере преподавателя.</summary>
    private async Task RememberAsync(string comment)
    {
        var text = comment.Trim();
        if (text.Length == 0) return;
        var recent = _recent.Where(r => !string.Equals(r, text, StringComparison.CurrentCultureIgnoreCase)).Prepend(text).Take(RecentLimit).ToList();
        try { await JS.InvokeVoidAsync("localStorage.setItem", RecentKey, JsonSerializer.Serialize(recent)); }
        catch (JSException) { /* Подсказки — удобство, не данные. */ }
    }

    private static string Short(string text) => text.Length <= 48 ? text : text[..47] + "…";

    private void Cancel()
    {
        _edits.MarkClean();
        MudDialog.Cancel();
    }

    public void Dispose() => _edits?.Dispose();
}

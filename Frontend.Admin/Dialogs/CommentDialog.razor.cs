using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class CommentDialog : IDisposable
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;

    private string _comment = string.Empty;
    private UnsavedChangesTracker.Registration _edits = null!;

    protected override void OnInitialized() => _edits = Dirty.Register();

    private void Submit()
    {
        _edits.MarkClean();
        MudDialog.Close(DialogResult.Ok(_comment));
    }

    private void Cancel()
    {
        _edits.MarkClean();
        MudDialog.Cancel();
    }

    public void Dispose() => _edits?.Dispose();
}

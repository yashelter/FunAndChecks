using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class CommentDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    private string _comment = string.Empty;

    private void Submit() => MudDialog.Close(DialogResult.Ok(_comment));

    private void Cancel() => MudDialog.Cancel();
}

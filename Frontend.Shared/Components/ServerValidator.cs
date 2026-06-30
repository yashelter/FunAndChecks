using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Frontend.Shared.Components;

public class ServerValidator : ComponentBase, IDisposable
{
    private ValidationMessageStore? _messageStore;

    [CascadingParameter]
    public EditContext? CurrentEditContext { get; set; }

    protected override void OnInitialized()
    {
        if (CurrentEditContext == null)
        {
            throw new InvalidOperationException($"{nameof(ServerValidator)} requires a cascading parameter of type {nameof(EditContext)}.");
        }

        _messageStore = new ValidationMessageStore(CurrentEditContext);

        CurrentEditContext.OnValidationRequested += ClearErrors;
        CurrentEditContext.OnFieldChanged += ClearError;
    }

    public void DisplayErrors(Dictionary<string, string[]> errors)
    {
        if (CurrentEditContext == null || _messageStore == null)
            return;

        _messageStore.Clear();

        foreach (var (field, messages) in errors)
        {
            var fieldIdentifier = new FieldIdentifier(CurrentEditContext.Model, field);
            _messageStore.Add(fieldIdentifier, messages);
        }

        CurrentEditContext.NotifyValidationStateChanged();
    }

    private void ClearErrors(object? sender, ValidationRequestedEventArgs args)
    {
        _messageStore?.Clear();
        CurrentEditContext?.NotifyValidationStateChanged();
    }

    private void ClearError(object? sender, FieldChangedEventArgs args)
    {
        _messageStore?.Clear(args.FieldIdentifier);
        CurrentEditContext?.NotifyValidationStateChanged();
    }

    public void Dispose()
    {
        if (CurrentEditContext != null)
        {
            CurrentEditContext.OnValidationRequested -= ClearErrors;
            CurrentEditContext.OnFieldChanged -= ClearError;
        }
    }
}

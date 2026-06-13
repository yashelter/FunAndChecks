using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Pages;

/// <summary>Публичная таблица результатов — доступна без входа.</summary>
public partial class PublicResults
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectResultsDto? _results;
    private bool _loading;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Subjects.GetAllAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OnSubjectSelectedAsync(SubjectDto? subject)
    {
        if (subject is null)
            return;

        _loading = true;
        _results = null;

        try
        {
            _results = await Results.GetSubjectResultsAsync(subject.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось загрузить результаты: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }
}

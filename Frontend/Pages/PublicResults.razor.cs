using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Pages;

/// <summary>Публичная таблица результатов — доступна без входа.</summary>
public partial class PublicResults
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private ResultsNavigationState ProfileReturn { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private SubjectResultsDto? _results;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await ProfileReturn.InitializeAsync();
            _subjects = await Subjects.GetAllAsync();
            var selected = ProfileReturn.Subject("/results");
            if ((_subjects.FirstOrDefault(s => s.Id == selected) ?? _subjects.FirstOrDefault()) is { } subject)
                await OnSubjectSelectedAsync(subject);
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

    private int _loadVersion;

    private async Task OnSubjectSelectedAsync(SubjectDto? subject)
    {
        if (subject is null)
            return;

        _selectedSubject = subject;
        var version = ++_loadVersion;
        ProfileReturn.SelectSubject("/results", subject.Id);
        _loading = true;
        _results = null;

        try
        {
            var results = await Results.GetSubjectResultsAsync(subject.Id);
            if (version == _loadVersion) _results = results;
        }
        catch (ApiException ex) when (version == _loadVersion)
        {
            Snackbar.Add(string.Format(Loc["Common_LoadResultsError"], ex.Message), Severity.Error);
        }
        catch (ApiException) { /* Ответ по уже не выбранному предмету. */ }
        finally
        {
            if (version == _loadVersion) _loading = false;
        }
    }
}

using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.UI;

public partial class ReviewerStatsBoard
{
    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<ReviewerStatsDto> _rows = [];
    private int? _subjectId;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = (await Subjects.GetAllAsync()).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        await LoadAsync();
    }

    private async Task ChangeSubjectAsync(int? subjectId)
    {
        _subjectId = subjectId;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var subjectId = _subjectId;
        try
        {
            var rows = await Admins.GetReviewerStatsAsync(subjectId);
            // Пока грузили, могли выбрать другой предмет — его загрузка и покажет свою таблицу.
            if (_subjectId == subjectId) _rows = rows;
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally
        {
            if (_subjectId == subjectId) _loading = false;
        }
    }

    private string SubjectName(int? id) =>
        id is null ? Loc["Stats_AllSubjects"] : _subjects.FirstOrDefault(s => s.Id == id)?.Name ?? "";

    private static string Share(ReviewerStatsDto row) =>
        row.AcceptedShare is double share ? share.ToString("0", System.Globalization.CultureInfo.CurrentCulture) : "—";

    private static string? LetterStyle(string? color) =>
        string.IsNullOrWhiteSpace(color) ? null : $"background-color:{color};color:{ColorUtils.ContrastText(color)};border-color:{color};";
}

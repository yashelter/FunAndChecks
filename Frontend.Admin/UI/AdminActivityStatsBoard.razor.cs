using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.UI;

public partial class AdminActivityStatsBoard
{
    private enum ActivityPeriod { All, Today, Last7Days, Last30Days, Custom }

    [Inject] private AdminsApi Admins { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<AdminActivityStatsDto> _rows = [];
    private int? _subjectId;
    private ActivityPeriod _period = ActivityPeriod.All;
    private DateTime? _customFrom;
    private DateTime? _customTo;
    private bool _loading = true;
    private int _requestVersion;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = (await Subjects.GetAllAsync()).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        await LoadAsync();
    }

    private Task ChangeSubjectAsync(int? subjectId)
    {
        _subjectId = subjectId;
        return LoadAsync();
    }

    private Task ChangePeriodAsync(ActivityPeriod period)
    {
        _period = period;
        return LoadAsync();
    }

    private Task ChangeFromDateAsync(DateTime? date)
    {
        _customFrom = date;
        return LoadAsync();
    }

    private Task ChangeToDateAsync(DateTime? date)
    {
        _customTo = date;
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var version = ++_requestVersion;
        var (fromUtc, toUtc) = ResolveRange();
        try
        {
            var rows = await Admins.GetAdminActivityStatsAsync(_subjectId, fromUtc, toUtc);
            if (_requestVersion == version) _rows = rows;
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally
        {
            if (_requestVersion == version) _loading = false;
        }
    }

    private (DateTime? FromUtc, DateTime? ToUtc) ResolveRange()
    {
        var nowUtc = DateTime.UtcNow;
        return _period switch
        {
            ActivityPeriod.Today => (DateTime.Today.ToUniversalTime(), null),
            ActivityPeriod.Last7Days => (nowUtc.AddDays(-7), null),
            ActivityPeriod.Last30Days => (nowUtc.AddDays(-30), null),
            ActivityPeriod.Custom => (
                _customFrom?.Date.ToUniversalTime(),
                _customTo?.Date.AddDays(1).AddTicks(-1).ToUniversalTime()),
            _ => (null, null)
        };
    }

    private string SubjectName(int? id) =>
        id is null ? Loc["Stats_AllSubjects"] : _subjects.FirstOrDefault(s => s.Id == id)?.Name ?? "";

    private static string FormatLastActivity(DateTime? utc) =>
        utc is DateTime dt ? dt.ToLocalTime().ToString("g") : "—";

    private static string? LetterStyle(string? color) =>
        string.IsNullOrWhiteSpace(color) ? null : $"background-color:{color};color:{ColorUtils.ContrastText(color)};border-color:{color};";
}

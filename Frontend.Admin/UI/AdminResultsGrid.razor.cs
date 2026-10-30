using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Frontend.Admin.UI;

/// <summary>Клик по ячейке задачи: студент и задача, которые открыть.</summary>
public readonly record struct ResultCellClick(StudentResultRowDto Row, TaskHeaderDto Task);

public partial class AdminResultsGrid : IAsyncDisposable
{
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private ResultsNavigationState ProfileReturn { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired] public SubjectResultsDto Results { get; set; } = null!;
    [Parameter] public VariantBoardDto? Variants { get; set; }
    [Parameter] public Dictionary<Guid, (int Present, int Absent, int Unmarked)>? Attendance { get; set; }
    [Parameter] public EventCallback<ResultCellClick> OnCellClick { get; set; }

    /// <summary>Ключ состояния возврата из профиля (страница таблицы).</summary>
    private const string StateKey = "/admin";

    private MudDataGrid<StudentResultRowDto>? _grid;
    private ElementReference _host;
    private readonly Guid _profileViewInstance = Guid.NewGuid();
    private Dictionary<Guid, Dictionary<int, AssignedVariantDto>> _variantsByStudent = [];
    private string? _scrollAttachedKey;

    // Что было нарисовано: рендер нужен, только если что-то из этого сменилось.
    private SubjectResultsDto? _shownResults;
    private VariantBoardDto? _shownVariants;
    private object? _shownAttendance;
    private string? _shownCulture;
    private bool _dirty = true;

    // Тексты, общие для всех ячеек: считаются один раз, а не в каждой из тысяч ячеек.
    private string _cellAction = "", _accepted = "", _rejected = "", _notSubmitted = "", _selfClaimed = "", _byTeacher = "";

    /// <summary>
    /// MudDataGrid регистрирует колонки в порядке создания: колонка, появившаяся при живом обновлении
    /// (новая задача, оценка или набор вариантов), встала бы в конец, после посещаемости. Другой набор колонок — новая таблица.
    /// </summary>
    private string ColumnsKey => ResultsColumns.Key(Results) + "|" + string.Join(',', Variants?.Sets.Select(s => s.Id) ?? []);

    protected override void OnParametersSet()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (ReferenceEquals(_shownResults, Results) && ReferenceEquals(_shownVariants, Variants) && ReferenceEquals(_shownAttendance, Attendance)
            && _shownCulture == culture)
            return;

        if (!ReferenceEquals(_shownVariants, Variants))
            _variantsByStudent = Variants?.Students.ToDictionary(r => r.StudentId, r => r.Variants) ?? [];
        if (_shownCulture != culture)
        {
            _cellAction = Loc["Dashboard_CellAction"];
            _accepted = Loc["Task_StatusAccepted"];
            _rejected = Loc["Task_StatusRejected"];
            _notSubmitted = Loc["Task_StatusNotSubmitted"];
            _selfClaimed = Loc["Variants_SelfClaimed"];
            _byTeacher = Loc["Variants_ByTeacher"];
        }
        (_shownResults, _shownVariants, _shownAttendance, _shownCulture) = (Results, Variants, Attendance, culture);
        _dirty = true;
    }

    /// <summary>
    /// Страница перерисовывается часто (статистика, уведомления, события SignalR) — таблица только при смене данных.
    /// Клик по ячейке тоже не перерисовывает её: диалог открывает страница. Переключатель цветов преподавателей
    /// меняет лишь класс обёртки на странице (<c>fc-results-show-teacher</c>), а цвет уже лежит в ячейке.
    /// </summary>
    protected override bool ShouldRender() => _dirty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _dirty = false;
        // Восстановленные фильтры и сортировка меняют порядок строк — показать их может только новый рендер таблицы.
        if (await ProfileReturn.RestoreAsync(StateKey, _grid, _profileViewInstance))
        {
            _dirty = true;
            StateHasChanged();
        }
        // Полосу сверху привязываем к таблице заново, когда @key пересоздал её с новым набором колонок.
        if (_scrollAttachedKey != ColumnsKey)
        {
            _scrollAttachedKey = ColumnsKey;
            try { await JS.InvokeVoidAsync("resultsScroll.attach", _host); } catch (JSException) { }
        }
    }

    private Task RememberResultsAsync() => ProfileReturn.CaptureAsync(StateKey, _grid, _profileViewInstance);

    private int? VariantNumber(StudentResultRowDto row, int setId) =>
        _variantsByStudent.GetValueOrDefault(row.StudentId)?.GetValueOrDefault(setId)?.Number;

    private string CellLabel(StudentResultRowDto row, TaskHeaderDto task, ResultCellDto? cell)
    {
        var status = cell?.Status switch
        {
            SubmissionStatus.Accepted => _accepted,
            SubmissionStatus.Rejected => _rejected,
            _ => _notSubmitted,
        };
        return $"{row.FullName}, {task.TaskName}: {status}. {_cellAction}";
    }

    private static string FioStyle(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return string.Empty;
        return $"background-color:{color};color:{ColorUtils.ContrastText(color)};padding:2px 8px;border-radius:6px;";
    }

    private static string CellText(ResultCellDto? cell) =>
        cell?.Status == SubmissionStatus.Accepted ? "+" : cell?.DisplayValue ?? string.Empty;

    private string? CellStyle(ResultCellDto? cell)
    {
        if (cell is null || string.IsNullOrWhiteSpace(cell.AdminColor))
            return null;

        // Цвет преподавателя у зачтённой — всегда в переменной; показывает его класс fc-results-show-teacher на странице.
        if (cell.Status == SubmissionStatus.Accepted)
            return $"--tc:{cell.AdminColor};";

        // The letter and its teacher's color carry information independently from the theme accent.
        return $"background-color:{cell.AdminColor};color:{ColorUtils.ContrastText(cell.AdminColor)};";
    }

    private static string CellClass(ResultCellDto? cell) => cell?.Status switch
    {
        SubmissionStatus.Accepted => "fc-result-accepted",
        SubmissionStatus.Rejected => "fc-result-rework",
        _ => "fc-result-unsubmitted",
    };

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("resultsScroll.detach", _host); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }
}

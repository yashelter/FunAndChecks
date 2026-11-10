using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Frontend.Admin.UI;

public partial class AdminResultsGrid
{
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    [Parameter, EditorRequired] public SubjectResultsDto Results { get; set; } = null!;
    [Parameter] public VariantBoardDto? Variants { get; set; }
    [Parameter] public Dictionary<Guid, (int Present, int Absent, int Unmarked)>? Attendance { get; set; }
    [Parameter] public EventCallback<ResultCellClick> OnCellClick { get; set; }

    // Списки колонок пересобираются только при смене своих данных: по ссылке на них таблица решает, перерисовываться ли.
    private IReadOnlyList<ResultsExtraColumn>? _variantColumns, _attendanceColumns;
    private VariantBoardDto? _shownVariants;
    private object? _shownAttendance;
    private int _shownSubject;
    private string? _shownCulture;

    protected override void OnParametersSet()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var cultureChanged = _shownCulture != culture;
        _shownCulture = culture;

        if (cultureChanged || !ReferenceEquals(_shownVariants, Variants))
        {
            _shownVariants = Variants;
            _variantColumns = Variants is { Sets.Count: > 0 } board ? VariantColumns(board) : null;
        }
        if (cultureChanged || !ReferenceEquals(_shownAttendance, Attendance) || _shownSubject != Results.SubjectId)
        {
            _shownAttendance = Attendance;
            _shownSubject = Results.SubjectId;
            _attendanceColumns = Attendance is { } attendance ? [AttendanceColumn(attendance, Results.SubjectId)] : null;
        }
    }

    private List<ResultsExtraColumn> VariantColumns(VariantBoardDto board)
    {
        var byStudent = board.Students.ToDictionary(r => r.StudentId, r => r.Variants);
        string selfClaimed = Loc["Variants_SelfClaimed"], byTeacher = Loc["Variants_ByTeacher"];
        AssignedVariantDto? Find(StudentResultRowDto row, int setId) => byStudent.GetValueOrDefault(row.StudentId)?.GetValueOrDefault(setId);

        return board.Sets.Select(set => new ResultsExtraColumn(
            Key: $"variant:{set.Id}",
            Title: Loc["Variants_ColumnHeader", set.Name],
            Cell: row => Find(row, set.Id) is { } variant
                ? new ResultsExtraCell(variant.Number.ToString(), variant.Title ?? (variant.SelfClaimed ? selfClaimed : byTeacher))
                : default,
            // Без варианта — в конец при сортировке по возрастанию.
            SortKey: row => Find(row, set.Id)?.Number ?? int.MaxValue,
            CellClass: "fc-results-variant")).ToList();
    }

    private ResultsExtraColumn AttendanceColumn(Dictionary<Guid, (int Present, int Absent, int Unmarked)> attendance, int subjectId) => new(
        Key: "attendance",
        Title: Loc["Nav_Attendance"],
        // Одна строка: высота строк одинакова, иначе виртуализация «прыгает».
        Cell: row =>
        {
            var counts = attendance.GetValueOrDefault(row.StudentId);
            return new ResultsExtraCell(
                Loc["Dashboard_AttendanceCounts", counts.Present, counts.Absent],
                Href: $"/admin/attendance?subjectId={subjectId}&studentId={row.StudentId}",
                Note: counts.Unmarked > 0 ? Loc["Dashboard_AttendanceUnmarked", counts.Unmarked] : null);
        },
        CellClass: "fc-results-attendance");
}

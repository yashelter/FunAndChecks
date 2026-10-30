using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Variants
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private VariantsApi Api { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private int? _subjectId;
    private VariantBoardDto? _board;
    private bool _loading, _busy;
    private int? _groupFilter;
    private string? _search;
    private bool _onlyWithout;
    private readonly Dictionary<int, int?> _rangeFrom = [], _rangeTo = [];

    private IEnumerable<(int Id, string Name)> Groups => (_board?.Students ?? [])
        .Select(r => (r.GroupId, r.GroupName)).Distinct().OrderBy(g => g.GroupName, StringComparer.CurrentCultureIgnoreCase);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetVisibleSubjectsAsync();
            _subjectId = await CurrentSubject.PickAsync(_subjects.Select(s => s.Id)) ?? _subjects.FirstOrDefault()?.Id;
            await LoadAsync();
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task ChangeSubjectAsync(int? subjectId)
    {
        _subjectId = subjectId;
        _groupFilter = null;
        if (subjectId is { } id) await CurrentSubject.SetAsync(id);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_subjectId is not { } subjectId) { _board = null; return; }
        _loading = _board?.SubjectId != subjectId;
        try
        {
            var board = await Api.GetBoardAsync(subjectId);
            if (_subjectId == subjectId) _board = board;
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _loading = false; }
    }

    /// <summary>Действие с перечитыванием доски: счётчики выдач и пометки «занят» зависят от всех студентов.</summary>
    private async Task RunAsync(Func<Task> action, string? done = null)
    {
        _busy = true;
        try
        {
            await action();
            if (done is not null) Snackbar.Add(done, Severity.Success);
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally
        {
            await LoadAsync();
            _busy = false;
        }
    }

    private async Task CreateSetAsync()
    {
        if (_subjectId is not { } subjectId) return;
        var dialog = await Dialogs.ShowAsync<VariantSetDialog>(Loc["Variants_NewSet"], new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        if (await dialog.Result is { Canceled: false, Data: SaveVariantSetRequest request })
            await RunAsync(() => Api.CreateSetAsync(subjectId, request), Loc["Common_Saved"]);
    }

    private async Task EditSetAsync(VariantSetDto set)
    {
        var parameters = new DialogParameters<VariantSetDialog> { { x => x.Initial, set } };
        var dialog = await Dialogs.ShowAsync<VariantSetDialog>(Loc["Variants_EditSet"], parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        if (await dialog.Result is { Canceled: false, Data: SaveVariantSetRequest request })
            await RunAsync(() => Api.UpdateSetAsync(set.Id, request), Loc["Common_Saved"]);
    }

    /// <summary>
    /// Открыть или закрыть выбор одной кнопкой. Открытие после истёкшего срока снимает срок —
    /// иначе выбор остался бы закрытым.
    /// </summary>
    private Task ToggleOpenAsync(VariantSetDto set)
    {
        var open = !set.IsOpen;
        var deadline = open && set.ClaimDeadline <= DateTime.UtcNow ? null : set.ClaimDeadline;
        return RunAsync(() => Api.UpdateSetAsync(set.Id, new SaveVariantSetRequest(set.Name, set.Uniqueness, open, deadline)));
    }

    private async Task DeleteSetAsync(VariantSetDto set)
    {
        if (await Confirm.ConfirmAsync(Loc["Common_ConfirmDeleteTitle"], Loc["Variants_DeleteSetConfirm", set.Name], Loc["Common_Delete"]))
            await RunAsync(() => Api.DeleteSetAsync(set.Id));
    }

    private int? RangeFrom(VariantSetDto set) => _rangeFrom.TryGetValue(set.Id, out var from) ? from : NextNumber(set);
    private bool RangeValid(VariantSetDto set) =>
        RangeFrom(set) is { } from && _rangeTo.GetValueOrDefault(set.Id) is { } to && to >= from && to - from < 500;
    private static int NextNumber(VariantSetDto set) => set.Variants.Count == 0 ? 1 : set.Variants.Max(v => v.Number) + 1;

    private async Task AddRangeAsync(VariantSetDto set)
    {
        if (RangeFrom(set) is not { } from || _rangeTo.GetValueOrDefault(set.Id) is not { } to) return;
        var added = 0;
        await RunAsync(async () => added = await Api.AddRangeAsync(set.Id, new AddVariantRangeRequest(from, to)));
        if (added > 0) Snackbar.Add(Loc["Variants_RangeAdded", added], Severity.Success);
        _rangeFrom.Remove(set.Id);
        _rangeTo.Remove(set.Id);
    }

    private async Task AddVariantAsync(VariantSetDto set)
    {
        var parameters = new DialogParameters<VariantEditDialog> { { x => x.SuggestedNumber, NextNumber(set) } };
        var dialog = await Dialogs.ShowAsync<VariantEditDialog>(Loc["Variants_NewVariant"], parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        if (await dialog.Result is { Canceled: false, Data: AddVariantRequest request })
            await RunAsync(() => Api.AddVariantAsync(set.Id, request), Loc["Common_Saved"]);
    }

    private async Task EditVariantAsync(VariantDto variant)
    {
        var parameters = new DialogParameters<VariantEditDialog>
        {
            { x => x.Number, variant.Number }, { x => x.Title, variant.Title }, { x => x.Description, variant.Description },
        };
        var dialog = await Dialogs.ShowAsync<VariantEditDialog>(Loc["Variants_EditVariant", variant.Number], parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        if (await dialog.Result is { Canceled: false, Data: AddVariantRequest request })
            await RunAsync(() => Api.UpdateVariantAsync(variant.Id, new UpdateVariantRequest(request.Title, request.Description)), Loc["Common_Saved"]);
    }

    private async Task DeleteVariantAsync(VariantDto variant)
    {
        if (await Confirm.ConfirmDeleteAsync(Loc["Variants_DeleteVariantConfirm", variant.Number]))
            await RunAsync(() => Api.DeleteVariantAsync(variant.Id));
    }

    private Task AssignAsync(VariantSetDto set, VariantBoardRowDto row, int? variantId)
    {
        if (row.Variants.GetValueOrDefault(set.Id)?.VariantId == variantId) return Task.CompletedTask;
        if (variantId is { } id)
        {
            var number = set.Variants.FirstOrDefault(v => v.Id == id)?.Number;
            return RunAsync(() => Api.AssignAsync(set.Id, row.StudentId, id), Loc["Variants_Assigned", number ?? 0]);
        }
        return RunAsync(() => Api.UnassignAsync(set.Id, row.StudentId), Loc["Variants_Unassigned"]);
    }

    private IEnumerable<VariantBoardRowDto> FilteredRows(VariantSetDto set) => (_board?.Students ?? [])
        .Where(r => _groupFilter is null || r.GroupId == _groupFilter)
        .Where(r => string.IsNullOrWhiteSpace(_search) || r.FullName.Contains(_search.Trim(), StringComparison.CurrentCultureIgnoreCase))
        .Where(r => !_onlyWithout || !r.Variants.ContainsKey(set.Id));

    /// <summary>Кто держит каждый вариант набора: для пометки «занят» без перебора всех студентов на каждый пункт.</summary>
    private ILookup<int, VariantBoardRowDto> Holders(VariantSetDto set) => (_board?.Students ?? [])
        .Where(r => r.Variants.ContainsKey(set.Id)).ToLookup(r => r.Variants[set.Id].VariantId);

    /// <summary>Занят ли вариант для этого студента по правилу набора. Преподаватель всё равно может его выдать.</summary>
    private static bool TakenFor(VariantSetDto set, ILookup<int, VariantBoardRowDto> holders, VariantBoardRowDto row, int variantId) => set.Uniqueness switch
    {
        VariantUniqueness.UniqueInSubject => holders[variantId].Any(r => r.StudentId != row.StudentId),
        VariantUniqueness.UniqueInGroup => holders[variantId].Any(r => r.StudentId != row.StudentId && r.GroupId == row.GroupId),
        _ => false,
    };

    private string VariantLabel(VariantSetDto set, int? variantId) =>
        variantId is { } id && set.Variants.FirstOrDefault(v => v.Id == id) is { } variant
            ? VariantText.Label(variant.Number, variant.Title)
            : Loc["Variants_NotAssigned"];
}

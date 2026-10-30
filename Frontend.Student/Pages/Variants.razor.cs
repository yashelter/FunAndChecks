using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class Variants
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private VariantsApi Api { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<StudentVariantSetDto> _sets = [];
    private int? _subjectId;
    private bool _loading = true, _busy;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetMySubjectsAsync();
            _subjectId = await CurrentSubject.PickAsync(_subjects.Select(s => s.Id)) ?? _subjects.FirstOrDefault()?.Id;
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task ChangeSubjectAsync(int? id)
    {
        _subjectId = id;
        _sets = [];
        if (id is { } subjectId) await CurrentSubject.SetAsync(subjectId);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _error = null;
        if (_subjectId is not { } subjectId) { _sets = []; _loading = false; return; }
        _loading = _sets.Count == 0;
        try { _sets = await Api.GetMyAsync(subjectId); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }

    /// <summary>Выбор окончательный — подтверждаем. Если вариант успели занять, список перечитывается.</summary>
    private async Task ClaimAsync(StudentVariantSetDto set, StudentVariantDto variant)
    {
        if (_busy) return;
        // Кнопки неактивны, пока открыто подтверждение: двойной клик не откроет второй диалог.
        _busy = true;
        try
        {
            if (!await Confirm.ConfirmAsync(Loc["MyVariants_TakeTitle", variant.Number], Loc["MyVariants_TakeText"], Loc["MyVariants_Take"]))
                return;
            var updated = await Api.ClaimAsync(set.Id, variant.Id);
            _sets = _sets.Select(s => s.Id == updated.Id ? updated : s).ToList();
            Snackbar.Add(Loc["MyVariants_Claimed", variant.Number], Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, ex.Code == "variant.taken" ? Severity.Warning : Severity.Error);
            await LoadAsync();
        }
        finally { _busy = false; }
    }
}

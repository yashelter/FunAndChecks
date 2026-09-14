using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты сводных результатов — /api/results.</summary>
public class ResultsApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<SubjectResultsDto> GetSubjectResultsAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<SubjectResultsDto>($"api/results/subjects/{subjectId}", ct);

    /// <summary>Скачивает XLSX-экспорт таблицы результатов (с заливкой цветами).</summary>
    public async Task<byte[]> ExportXlsxAsync(int subjectId, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"api/results/subjects/{subjectId}/export", ct);
        await response.EnsureSuccessAsync(Loc);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}

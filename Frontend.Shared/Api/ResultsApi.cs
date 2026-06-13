using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты сводных результатов — /api/results.</summary>
public class ResultsApi(HttpClient http) : ApiClientBase(http)
{
    public Task<SubjectResultsDto> GetSubjectResultsAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<SubjectResultsDto>($"api/results/subjects/{subjectId}", ct);
}

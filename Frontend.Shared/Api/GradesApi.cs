using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты оценок — /api/grade-components.</summary>
public class GradesApi(HttpClient http) : ApiClientBase(http)
{
    public Task DeleteComponentAsync(int componentId, CancellationToken ct = default) =>
        DeleteAsync($"api/grade-components/{componentId}", ct);

    public Task SetGradeAsync(int componentId, Guid studentId, SetGradeRequest request, CancellationToken ct = default) =>
        PutAsync($"api/grade-components/{componentId}/students/{studentId}", request, ct);

    public Task DeleteGradeAsync(int componentId, Guid studentId, CancellationToken ct = default) =>
        DeleteAsync($"api/grade-components/{componentId}/students/{studentId}", ct);
}

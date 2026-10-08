using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты оценок — /api/grade-components.</summary>
public class GradesApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task DeleteComponentAsync(int componentId, CancellationToken ct = default) =>
        DeleteAsync($"api/grade-components/{componentId}", ct);

    public Task SetGradeAsync(int componentId, Guid studentId, SetGradeRequest request, CancellationToken ct = default) =>
        PutAsync($"api/grade-components/{componentId}/students/{studentId}", request, ct);

    public Task DeleteGradeAsync(int componentId, Guid studentId, CancellationToken ct = default) =>
        DeleteAsync($"api/grade-components/{componentId}/students/{studentId}", ct);
}

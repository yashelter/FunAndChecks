using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты сдач — /api/submissions.</summary>
public class SubmissionsApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task CreateAsync(CreateSubmissionRequest request, CancellationToken ct = default) =>
        PostAsync("api/submissions", request, ct);

    public Task<List<SubmissionLogDto>> GetLogAsync(Guid studentId, int taskId, CancellationToken ct = default) =>
        GetAsync<List<SubmissionLogDto>>($"api/submissions/students/{studentId}/tasks/{taskId}", ct);
}

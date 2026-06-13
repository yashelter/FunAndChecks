using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты сдач — /api/submissions.</summary>
public class SubmissionsApi(HttpClient http) : ApiClientBase(http)
{
    public Task CreateAsync(CreateSubmissionRequest request, CancellationToken ct = default) =>
        PostAsync("api/submissions", request, ct);

    public Task<List<SubmissionLogDto>> GetLogAsync(Guid studentId, int taskId, CancellationToken ct = default) =>
        GetAsync<List<SubmissionLogDto>>($"api/submissions/students/{studentId}/tasks/{taskId}", ct);
}

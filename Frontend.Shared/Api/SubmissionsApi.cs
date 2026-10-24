using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты сдач — /api/submissions.</summary>
public class SubmissionsApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task CreateAsync(CreateSubmissionRequest request, CancellationToken ct = default) =>
        PostAsync("api/submissions", request, ct);

    /// <summary>Отменяет свою последнюю свежую отметку и возвращает статус, ставший текущим.</summary>
    public async Task<SubmissionStatus> UndoLatestAsync(Guid studentId, int taskId, CancellationToken ct = default) =>
        (await GuardAsync(async () =>
        {
            using var response = await Http.DeleteAsync($"api/submissions/students/{studentId}/tasks/{taskId}/latest", ct);
            await response.EnsureSuccessAsync(Loc);
            return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<UndoSubmissionResultDto>(response.Content, ct))!;
        }, Loc, ct)).Status;

    public Task<List<SubmissionLogDto>> GetLogAsync(Guid studentId, int taskId, CancellationToken ct = default) =>
        GetAsync<List<SubmissionLogDto>>($"api/submissions/students/{studentId}/tasks/{taskId}", ct);
}

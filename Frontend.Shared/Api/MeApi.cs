using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты текущего пользователя — /api/me.</summary>
public class MeApi(HttpClient http) : ApiClientBase(http)
{
    public Task<MeDto> GetMeAsync(CancellationToken ct = default) =>
        GetAsync<MeDto>("api/me", ct);

    public Task<List<SubjectDto>> GetMySubjectsAsync(CancellationToken ct = default) =>
        GetAsync<List<SubjectDto>>("api/me/subjects", ct);

    public Task<GroupDto> GetMyGroupAsync(CancellationToken ct = default) =>
        GetAsync<GroupDto>("api/me/group", ct);

    public Task<List<QueueEventDto>> GetMyQueueEventsAsync(bool includePast = false, CancellationToken ct = default) =>
        GetAsync<List<QueueEventDto>>($"api/me/queue-events?includePast={includePast}", ct);

    public Task<List<QueueEventDto>> GetAvailableQueueEventsAsync(bool includePast = false, CancellationToken ct = default) =>
        GetAsync<List<QueueEventDto>>($"api/me/available-queue-events?includePast={includePast}", ct);

    public Task<StudentSubjectResultsDto> GetMyResultsAsync(int subjectId, CancellationToken ct = default) =>
        GetAsync<StudentSubjectResultsDto>($"api/me/results/subjects/{subjectId}", ct);

    public Task<AdminAccessDto> GetMyAccessAsync(CancellationToken ct = default) =>
        GetAsync<AdminAccessDto>("api/me/access", ct);

    /// <summary>Видимые админу предметы (без запрещённых и архивных).</summary>
    public Task<List<SubjectDto>> GetVisibleSubjectsAsync(CancellationToken ct = default) =>
        GetAsync<List<SubjectDto>>("api/me/admin/subjects", ct);

    /// <summary>Видимые админу группы (без запрещённых и архивных).</summary>
    public Task<List<GroupDto>> GetVisibleGroupsAsync(CancellationToken ct = default) =>
        GetAsync<List<GroupDto>>("api/me/admin/groups", ct);

    public Task SetSubjectHiddenAsync(int subjectId, bool hidden, CancellationToken ct = default) =>
        PutAsync($"api/me/subjects/{subjectId}/hidden", new SetHiddenRequest(hidden), ct);

    public Task SetGroupHiddenAsync(int groupId, bool hidden, CancellationToken ct = default) =>
        PutAsync($"api/me/groups/{groupId}/hidden", new SetHiddenRequest(hidden), ct);
}

using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты групп — /api/groups.</summary>
public class GroupsApi(HttpClient http) : ApiClientBase(http)
{
    public Task<List<GroupDto>> GetAllAsync(CancellationToken ct = default) =>
        GetAsync<List<GroupDto>>("api/groups", ct);

    public Task<GroupDto> GetAsync(int groupId, CancellationToken ct = default) =>
        GetAsync<GroupDto>($"api/groups/{groupId}", ct);

    public Task<GroupDto> CreateAsync(CreateGroupRequest request, CancellationToken ct = default) =>
        PostAsync<GroupDto>("api/groups", request, ct);

    public Task<GroupDto> UpdateAsync(int groupId, UpdateGroupRequest request, CancellationToken ct = default) =>
        PutAsync<GroupDto>($"api/groups/{groupId}", request, ct);

    public Task DeleteAsync(int groupId, CancellationToken ct = default) =>
        DeleteAsync($"api/groups/{groupId}", ct);

    public Task LinkSubjectAsync(int groupId, int subjectId, CancellationToken ct = default) =>
        PutAsync($"api/groups/{groupId}/subjects/{subjectId}", body: null, ct);

    public Task UnlinkSubjectAsync(int groupId, int subjectId, CancellationToken ct = default) =>
        DeleteAsync($"api/groups/{groupId}/subjects/{subjectId}", ct);

    public Task<List<StudentDto>> GetStudentsAsync(int groupId, CancellationToken ct = default) =>
        GetAsync<List<StudentDto>>($"api/groups/{groupId}/students", ct);

    public Task<List<StudentDetailsDto>> GetStudentsDetailedAsync(int groupId, CancellationToken ct = default) =>
        GetAsync<List<StudentDetailsDto>>($"api/groups/{groupId}/students/details", ct);
}

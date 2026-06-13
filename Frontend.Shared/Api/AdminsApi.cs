using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты управления админами — /api/admins.</summary>
public class AdminsApi(HttpClient http) : ApiClientBase(http)
{
    private sealed record IdResponse(Guid Id);

    public Task<List<AdminDto>> GetAllAsync(CancellationToken ct = default) =>
        GetAsync<List<AdminDto>>("api/admins", ct);

    public async Task<Guid> CreateAsync(CreateAdminRequest request, CancellationToken ct = default)
    {
        var result = await PostAsync<IdResponse>("api/admins", request, ct);
        return result.Id;
    }

    public Task UpdateAsync(Guid adminId, UpdateAdminRequest request, CancellationToken ct = default) =>
        PutAsync($"api/admins/{adminId}", request, ct);

    public Task DeleteAsync(Guid adminId, CancellationToken ct = default) =>
        DeleteAsync($"api/admins/{adminId}", ct);

    public Task<AdminAccessDto> GetAccessAsync(Guid adminId, CancellationToken ct = default) =>
        GetAsync<AdminAccessDto>($"api/admins/{adminId}/access", ct);

    public Task SetSubjectRestrictionAsync(Guid adminId, int subjectId, bool restricted, CancellationToken ct = default) =>
        PutAsync($"api/admins/{adminId}/subjects/{subjectId}/restriction", new SetRestrictionRequest(restricted), ct);

    public Task SetGroupRestrictionAsync(Guid adminId, int groupId, bool restricted, CancellationToken ct = default) =>
        PutAsync($"api/admins/{adminId}/groups/{groupId}/restriction", new SetRestrictionRequest(restricted), ct);
}

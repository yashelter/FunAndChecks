using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Эндпоинты очередей — /api/queues.</summary>
public class QueuesApi(HttpClient http) : ApiClientBase(http)
{
    public Task<List<QueueEventDto>> GetActiveAsync(CancellationToken ct = default) =>
        GetAsync<List<QueueEventDto>>("api/queues", ct);

    public Task<List<QueueEventDto>> GetAllAsync(CancellationToken ct = default) =>
        GetAsync<List<QueueEventDto>>("api/queues/all", ct);

    public Task<QueueDetailsDto> GetDetailsAsync(int eventId, CancellationToken ct = default) =>
        GetAsync<QueueDetailsDto>($"api/queues/{eventId}", ct);

    public Task<QueueEventDto> CreateAsync(CreateQueueEventRequest request, CancellationToken ct = default) =>
        PostAsync<QueueEventDto>("api/queues", request, ct);

    public Task JoinAsync(int eventId, CancellationToken ct = default) =>
        PostAsync($"api/queues/{eventId}/join", body: null, ct);

    public Task UpdateStatusAsync(int eventId, Guid studentId, UpdateQueueStatusRequest request, CancellationToken ct = default) =>
        PutAsync($"api/queues/{eventId}/students/{studentId}/status", request, ct);
}

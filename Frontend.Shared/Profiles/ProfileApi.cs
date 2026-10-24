using Frontend.Shared.Api;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Frontend.Shared.Profiles;

public class ProfileApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public event Action? ProfileChanged;
    public Task<UserProfileDto> MineAsync(CancellationToken ct = default) => GetAsync<UserProfileDto>("api/me/profile", ct);
    public Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct = default) => GetAsync<UserProfileDto>($"api/profiles/{userId}", ct);
    public Task<ProfileStatsDto> StatsAsync(Guid? userId, CancellationToken ct = default) =>
        GetAsync<ProfileStatsDto>(userId is Guid id ? $"api/profiles/{id}/stats" : "api/me/profile/stats", ct);
    public Task<Dictionary<Guid, MediaDto>> AvatarsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        GetAsync<Dictionary<Guid, MediaDto>>("api/profiles/avatars?" + string.Join("&", ids.Select(id => $"ids={id}")), ct);
    public async Task<UserProfileDto> SaveProfileAsync(UpdateProfileRequest request, CancellationToken ct = default) { var profile = await PutAsync<UserProfileDto>("api/me/profile", request, ct); ProfileChanged?.Invoke(); return profile; }
    public async Task SetAvatarAsync(MediaSelection selection, CancellationToken ct = default) { await PostAsync("api/me/avatar", new { mediaSessionId = selection.SessionId, media = selection.Media }, ct); ProfileChanged?.Invoke(); }
    public async Task RemoveAvatarAsync(CancellationToken ct = default) { await DeleteAsync("api/me/avatar", ct); ProfileChanged?.Invoke(); }
    public Task<ProfilePageDto<AchievementDefinitionDto>> CatalogAsync(string? query = null, int page = 1, bool includeArchived = false, CancellationToken ct = default) =>
        GetAsync<ProfilePageDto<AchievementDefinitionDto>>($"api/achievements?query={Uri.EscapeDataString(query ?? "")}&page={page}&pageSize=24&includeArchived={includeArchived}", ct);
    public Task<AchievementDefinitionDto> AchievementAsync(int id, CancellationToken ct = default) => GetAsync<AchievementDefinitionDto>($"api/achievements/{id}", ct);
    public Task<ProfilePageDto<AchievementRecipientDto>> RecipientsAsync(int id, int page = 1, CancellationToken ct = default) => GetAsync<ProfilePageDto<AchievementRecipientDto>>($"api/achievements/{id}/recipients?page={page}&pageSize=20", ct);
    public Task<ProfilePageDto<AchievementUserDto>> SearchRecipientsAsync(string query, int page = 1, CancellationToken ct = default) => GetAsync<ProfilePageDto<AchievementUserDto>>($"api/admin/achievement-recipients?query={Uri.EscapeDataString(query)}&page={page}&pageSize=20", ct);
    public Task<AchievementDefinitionDto> CreateAchievementAsync(SaveAchievementRequest request, CancellationToken ct = default) => PostAsync<AchievementDefinitionDto>("api/admin/achievements", request, ct);
    public Task<AchievementDefinitionDto> UpdateAchievementAsync(int id, SaveAchievementRequest request, CancellationToken ct = default) => PutAsync<AchievementDefinitionDto>($"api/admin/achievements/{id}", request, ct);
    public Task ArchiveAsync(int id, bool archived, CancellationToken ct = default) => PutAsync($"api/admin/achievements/{id}/archive", new { archived }, ct);
    public Task GrantAsync(int id, Guid userId, CancellationToken ct = default) => PostAsync($"api/admin/achievements/{id}/awards", new { userId }, ct);
    public Task RevokeAsync(int awardId, CancellationToken ct = default) => PostAsync($"api/admin/achievement-awards/{awardId}/revoke", null, ct);
    public Task<MediaSessionDto> UploadAsync(Stream stream, string name, string contentType, CancellationToken ct = default) => GuardAsync(async () =>
    {
        using var form = new MultipartFormDataContent();
        using var source = new StreamContent(stream);
        source.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(source, "file", name);
        using var response = await Http.PostAsync("api/media/editor-sessions", form, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<MediaSessionDto>(ct))!;
    }, Loc, ct);
    public Task CancelSessionAsync(Guid sessionId, CancellationToken ct = default) => DeleteAsync($"api/media/editor-sessions/{sessionId}", ct);
    public Task ConsumeMediaAsync(Guid id, bool poster, Func<Stream, Task> consume, CancellationToken ct = default) => ConsumeAsync($"api/media/{id}?poster={poster}", consume, ct);
    public Task ConsumePreviewAsync(Guid sessionId, Func<Stream, Task> consume, CancellationToken ct = default) => ConsumeAsync($"api/media/editor-sessions/{sessionId}/preview?poster=false", consume, ct);
    public Task ConsumeFrameAsync(Guid sessionId, double seconds, Func<Stream, Task> consume, CancellationToken ct = default) => ConsumeAsync($"api/media/editor-sessions/{sessionId}/frame?timeSeconds={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}", consume, ct);
    private Task ConsumeAsync(string url, Func<Stream, Task> consume, CancellationToken ct) => GuardAsync(async () =>
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        await response.EnsureSuccessAsync(Loc);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await consume(stream);
        return true;
    }, Loc, ct);
}

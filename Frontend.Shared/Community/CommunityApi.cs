using System.Net.Http.Headers;
using System.Net.Http.Json;
using Frontend.Shared.Api;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Community;

public class NotificationApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<NotificationPageDto> ListAsync(int page = 1, CancellationToken ct = default) =>
        GetAsync<NotificationPageDto>($"api/me/notifications?page={page}&pageSize=20", ct);
    public Task MarkReadAsync(long id, CancellationToken ct = default) => PostAsync($"api/me/notifications/{id}/read", null, ct);
    public Task MarkAllReadAsync(CancellationToken ct = default) => PostAsync("api/me/notifications/read-all", null, ct);
}

public class ForumApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    /// <summary>Сообщает меню, что счётчик непрочитанного мог измениться.</summary>
    public event Action? UnreadChanged;

    public Task<List<ForumSectionDto>> SectionsAsync(CancellationToken ct = default) => GetAsync<List<ForumSectionDto>>("api/forum/sections", ct);
    public Task<ForumUnreadDto> UnreadAsync(CancellationToken ct = default) => GetAsync<ForumUnreadDto>("api/forum/unread", ct);
    public Task<ForumTopicPageDto> TopicsAsync(int? subjectId, bool general, string? search, int page, CancellationToken ct = default) =>
        GetAsync<ForumTopicPageDto>($"api/forum/topics?{(subjectId is int s ? $"subjectId={s}&" : "")}general={general}" +
            $"&search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize=25", ct);
    public Task<ForumTopicDto> TopicAsync(int id, CancellationToken ct = default) => GetAsync<ForumTopicDto>($"api/forum/topics/{id}", ct);
    public Task<ForumPostPageDto> PostsAsync(int topicId, int page, long? aroundPostId = null, CancellationToken ct = default) =>
        GetAsync<ForumPostPageDto>($"api/forum/topics/{topicId}/posts?page={page}&pageSize=30{(aroundPostId is long p ? $"&aroundPostId={p}" : "")}", ct);
    public Task<ForumTopicDto> CreateTopicAsync(CreateForumTopicRequest request, CancellationToken ct = default) =>
        PostAsync<ForumTopicDto>("api/forum/topics", request, ct);
    public Task<ForumTopicDto> UpdateTopicAsync(int id, UpdateForumTopicRequest request, CancellationToken ct = default) =>
        ExecutePatchAsync<ForumTopicDto>($"api/forum/topics/{id}", request, ct);
    public Task DeleteTopicAsync(int id, CancellationToken ct = default) => DeleteAsync($"api/forum/topics/{id}", ct);
    public Task<ForumPostDto> CreatePostAsync(int topicId, CreateForumPostRequest request, CancellationToken ct = default) =>
        PostAsync<ForumPostDto>($"api/forum/topics/{topicId}/posts", request, ct);
    public Task<ForumPostDto> UpdatePostAsync(long id, UpdateForumPostRequest request, CancellationToken ct = default) =>
        PutAsync<ForumPostDto>($"api/forum/posts/{id}", request, ct);
    public Task DeletePostAsync(long id, CancellationToken ct = default) => DeleteAsync($"api/forum/posts/{id}", ct);
    public Task PinAsync(long id, bool pinned, CancellationToken ct = default) =>
        pinned ? PostAsync($"api/forum/posts/{id}/pin", null, ct) : DeleteAsync($"api/forum/posts/{id}/pin", ct);
    public Task<List<ForumReactionSummaryDto>> ReactAsync(long id, ForumReactionKind kind, bool active, CancellationToken ct = default) =>
        active ? PutAsync<List<ForumReactionSummaryDto>>($"api/forum/posts/{id}/reactions/{kind}", new { }, ct)
            : ExecuteDeleteAsync<List<ForumReactionSummaryDto>>($"api/forum/posts/{id}/reactions/{kind}", ct);
    public async Task MarkReadAsync(int topicId, long lastPostId, CancellationToken ct = default)
    {
        await PostAsync($"api/forum/topics/{topicId}/read", new { lastPostId }, ct);
        UnreadChanged?.Invoke();
    }
    public Task SubscribeAsync(int topicId, bool subscribed, CancellationToken ct = default) =>
        subscribed ? PutAsync($"api/forum/topics/{topicId}/subscription", null, ct) : DeleteAsync($"api/forum/topics/{topicId}/subscription", ct);
    public Task<List<ForumMentionCandidateDto>> MentionCandidatesAsync(int topicId, string query, CancellationToken ct = default) =>
        GetAsync<List<ForumMentionCandidateDto>>($"api/forum/topics/{topicId}/mention-candidates?query={Uri.EscapeDataString(query)}", ct);
    public Task ReportAsync(long postId, string reason, CancellationToken ct = default) => PostAsync($"api/forum/posts/{postId}/report", new { reason }, ct);
    public Task<ForumReportPageDto> ReportsAsync(int page = 1, CancellationToken ct = default) =>
        GetAsync<ForumReportPageDto>($"api/admin/forum/reports?page={page}&pageSize=25", ct);
    public Task ResolveReportAsync(int id, bool deletePost, CancellationToken ct = default) =>
        PostAsync($"api/admin/forum/reports/{id}/resolve", new { deletePost }, ct);
    public Task<List<ForumBanDto>> BansAsync(CancellationToken ct = default) => GetAsync<List<ForumBanDto>>("api/admin/forum/bans", ct);
    public Task BanAsync(Guid userId, DateTime untilUtc, string? reason, CancellationToken ct = default) =>
        PutAsync($"api/admin/forum/bans/{userId}", new { untilUtc, reason }, ct);
    public Task UnbanAsync(Guid userId, CancellationToken ct = default) => DeleteAsync($"api/admin/forum/bans/{userId}", ct);

    public Task<AttachmentDto> UploadAsync(Stream stream, string name, string contentType, CancellationToken ct = default) => GuardAsync(async () =>
    {
        using var form = new MultipartFormDataContent();
        using var source = new StreamContent(stream);
        source.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(source, "file", name);
        using var response = await Http.PostAsync("api/attachments", form, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<AttachmentDto>(ct))!;
    }, Loc, ct);
    public Task RemoveUploadAsync(Guid id, CancellationToken ct = default) => DeleteAsync($"api/attachments/{id}", ct);

    private Task<T> ExecutePatchAsync<T>(string url, object body, CancellationToken ct) => GuardAsync(async () =>
    {
        using var response = await Http.PatchAsJsonAsync(url, body, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }, Loc, ct);

    private Task<T> ExecuteDeleteAsync<T>(string url, CancellationToken ct) => GuardAsync(async () =>
    {
        using var response = await Http.DeleteAsync(url, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }, Loc, ct);
}

public class SurveyApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<List<PendingSurveyDto>> PendingAsync(CancellationToken ct = default) => GetAsync<List<PendingSurveyDto>>("api/surveys/pending", ct);
    public Task<SurveyFormDto> FormAsync(int id, CancellationToken ct = default) => GetAsync<SurveyFormDto>($"api/surveys/{id}", ct);
    public Task SubmitAsync(int id, SubmitSurveyRequest request, CancellationToken ct = default) => PostAsync($"api/surveys/{id}/responses", request, ct);
    public Task SnoozeAsync(int id, CancellationToken ct = default) => PostAsync($"api/surveys/{id}/snooze", null, ct);
    public Task<List<MySurveyDto>> CompletedAsync(CancellationToken ct = default) => GetAsync<List<MySurveyDto>>("api/me/surveys", ct);

    public Task<SurveyPageDto> ListAsync(SurveyStatus? status, int page, CancellationToken ct = default) =>
        GetAsync<SurveyPageDto>($"api/admin/surveys?{(status is { } s ? $"status={(int)s}&" : "")}page={page}&pageSize=25", ct);
    public Task<SurveyAdminDto> GetAsync(int id, CancellationToken ct = default) => GetAsync<SurveyAdminDto>($"api/admin/surveys/{id}", ct);
    public Task<SurveyAdminDto> CreateAsync(SaveSurveyRequest request, CancellationToken ct = default) =>
        PostAsync<SurveyAdminDto>("api/admin/surveys", request, ct);
    public Task<SurveyAdminDto> SaveAsync(int id, SaveSurveyRequest request, CancellationToken ct = default) =>
        PutAsync<SurveyAdminDto>($"api/admin/surveys/{id}", request, ct);
    public Task<SurveyAdminDto> UpdateDetailsAsync(int id, UpdateSurveyDetailsRequest request, CancellationToken ct = default) => GuardAsync(async () =>
    {
        using var response = await Http.PatchAsJsonAsync($"api/admin/surveys/{id}/details", request, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<SurveyAdminDto>(ct))!;
    }, Loc, ct);
    public Task<SurveyAdminDto> PublishAsync(int id, CancellationToken ct = default) => PostAsync<SurveyAdminDto>($"api/admin/surveys/{id}/publish", new { }, ct);
    public Task<SurveyAdminDto> CloseAsync(int id, CancellationToken ct = default) => PostAsync<SurveyAdminDto>($"api/admin/surveys/{id}/close", new { }, ct);
    public Task<SurveyAdminDto> DuplicateAsync(int id, CancellationToken ct = default) => PostAsync<SurveyAdminDto>($"api/admin/surveys/{id}/duplicate", new { }, ct);
    public Task DeleteSurveyAsync(int id, CancellationToken ct = default) => DeleteAsync($"api/admin/surveys/{id}", ct);
    public Task<SurveyResultsDto> ResultsAsync(int id, int? groupId = null, CancellationToken ct = default) =>
        GetAsync<SurveyResultsDto>($"api/admin/surveys/{id}/results{(groupId is int g ? $"?groupId={g}" : "")}", ct);
    public Task<List<SurveyIndividualResponseDto>> ResponsesAsync(int id, int? groupId = null, CancellationToken ct = default) =>
        GetAsync<List<SurveyIndividualResponseDto>>($"api/admin/surveys/{id}/responses{(groupId is int g ? $"?groupId={g}" : "")}", ct);
    public Task<Stream> ExportAsync(int id, CancellationToken ct = default) => GuardAsync(async () =>
    {
        var response = await Http.GetAsync($"api/admin/surveys/{id}/export.xlsx", ct);
        await response.EnsureSuccessAsync(Loc);
        return await response.Content.ReadAsStreamAsync(ct);
    }, Loc, ct);
}

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
    public Task DeleteAsync(long id, CancellationToken ct = default) => DeleteAsync($"api/me/notifications/{id}", ct);
    public Task DeleteReadAsync(CancellationToken ct = default) => DeleteAsync("api/me/notifications/read", ct);
}

public class ForumApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    /// <summary>Сообщает меню, что счётчик непрочитанного мог измениться.</summary>
    public event Action? UnreadChanged;

    public Task<List<ForumSectionDto>> SectionsAsync(CancellationToken ct = default) => GetAsync<List<ForumSectionDto>>("api/forum/sections", ct);
    public Task<ForumUnreadDto> UnreadAsync(CancellationToken ct = default) => GetAsync<ForumUnreadDto>("api/forum/unread", ct);
    public Task<ForumTopicPageDto> TopicsAsync(int? subjectId, bool general, string? search, int page, CancellationToken ct = default,
        bool personal = false, int pageSize = 25) =>
        GetAsync<ForumTopicPageDto>($"api/forum/topics?{(subjectId is int s ? $"subjectId={s}&" : "")}general={general}&personal={personal}" +
            $"&search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}", ct);
    public Task<ForumFoldersDto> FoldersAsync(CancellationToken ct = default) => GetAsync<ForumFoldersDto>("api/forum/folders", ct);
    public Task<ForumTopicDto> OpenDirectAsync(Guid userId, CancellationToken ct = default) =>
        PostAsync<ForumTopicDto>("api/forum/direct", new { userId }, ct);
    public Task<ForumTopicDto> OpenSavedAsync(CancellationToken ct = default) => PostAsync<ForumTopicDto>("api/forum/saved", new { }, ct);
    public Task<List<ForumMentionCandidateDto>> ContactsAsync(string? query, CancellationToken ct = default) =>
        GetAsync<List<ForumMentionCandidateDto>>($"api/forum/contacts?query={Uri.EscapeDataString(query ?? "")}", ct);
    public Task<ForumPostDto> ForwardAsync(long postId, int? topicId, bool toSaved, CancellationToken ct = default) =>
        PostAsync<ForumPostDto>($"api/forum/posts/{postId}/forward", new { topicId, toSaved }, ct);
    public Task<List<ForumReactionTypeDto>> ReactionTypesAsync(CancellationToken ct = default) =>
        GetAsync<List<ForumReactionTypeDto>>("api/forum/reactions", ct);
    public Task<List<ForumStickerPackDto>> StickerPacksAsync(CancellationToken ct = default) =>
        GetAsync<List<ForumStickerPackDto>>("api/forum/stickers", ct);

    // Набор реакций и стикеров — только супер-админ.
    public Task<ForumReactionTypeDto> CreateReactionAsync(SaveForumReactionRequest request, CancellationToken ct = default) =>
        PostAsync<ForumReactionTypeDto>("api/admin/forum/reactions", request, ct);
    public Task<ForumReactionTypeDto> UpdateReactionAsync(int id, SaveForumReactionRequest request, CancellationToken ct = default) =>
        PutAsync<ForumReactionTypeDto>($"api/admin/forum/reactions/{id}", request, ct);
    public Task<ForumReactionTypeDto> SetReactionImageAsync(int id, Stream stream, string name, string contentType, CancellationToken ct = default) =>
        SendFileAsync<ForumReactionTypeDto>(HttpMethod.Put, $"api/admin/forum/reactions/{id}/image", stream, name, contentType, null, ct);
    public Task<ForumReactionTypeDto> RemoveReactionImageAsync(int id, CancellationToken ct = default) =>
        ExecuteDeleteAsync<ForumReactionTypeDto>($"api/admin/forum/reactions/{id}/image", ct);
    public Task DeleteReactionAsync(int id, CancellationToken ct = default) => DeleteAsync($"api/admin/forum/reactions/{id}", ct);
    public Task ReorderReactionsAsync(IEnumerable<int> ids, CancellationToken ct = default) =>
        PutAsync("api/admin/forum/reactions/order", new { ids = ids.ToList() }, ct);
    public Task<List<ForumStickerPackDto>> AllStickerPacksAsync(CancellationToken ct = default) =>
        GetAsync<List<ForumStickerPackDto>>("api/admin/forum/sticker-packs", ct);
    public Task<ForumStickerPackDto> CreateStickerPackAsync(SaveForumStickerPackRequest request, CancellationToken ct = default) =>
        PostAsync<ForumStickerPackDto>("api/admin/forum/sticker-packs", request, ct);
    public Task<ForumStickerPackDto> UpdateStickerPackAsync(int id, SaveForumStickerPackRequest request, CancellationToken ct = default) =>
        PutAsync<ForumStickerPackDto>($"api/admin/forum/sticker-packs/{id}", request, ct);
    public Task DeleteStickerPackAsync(int id, CancellationToken ct = default) => DeleteAsync($"api/admin/forum/sticker-packs/{id}", ct);
    public Task<ForumStickerDto> AddStickerAsync(int packId, Stream stream, string name, string contentType, CancellationToken ct = default) =>
        SendFileAsync<ForumStickerDto>(HttpMethod.Post, $"api/admin/forum/sticker-packs/{packId}/stickers", stream, name, contentType,
            Path.GetFileNameWithoutExtension(name), ct);
    public Task DeleteStickerAsync(int id, CancellationToken ct = default) => DeleteAsync($"api/admin/forum/stickers/{id}", ct);
    public Task ReorderStickersAsync(int packId, IEnumerable<int> ids, CancellationToken ct = default) =>
        PutAsync($"api/admin/forum/sticker-packs/{packId}/order", new { ids = ids.ToList() }, ct);
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
    public Task<List<ForumReactionSummaryDto>> ReactAsync(long id, int reactionId, bool active, CancellationToken ct = default) =>
        active ? PutAsync<List<ForumReactionSummaryDto>>($"api/forum/posts/{id}/reactions/{reactionId}", new { }, ct)
            : ExecuteDeleteAsync<List<ForumReactionSummaryDto>>($"api/forum/posts/{id}/reactions/{reactionId}", ct);
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

    private Task<T> SendFileAsync<T>(HttpMethod method, string url, Stream stream, string name, string contentType, string? title,
        CancellationToken ct) => GuardAsync(async () =>
    {
        using var form = new MultipartFormDataContent();
        using var source = new StreamContent(stream);
        source.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(source, "file", name);
        if (title is not null) form.Add(new StringContent(title), "name");
        using var request = new HttpRequestMessage(method, url) { Content = form };
        using var response = await Http.SendAsync(request, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }, Loc, ct);

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

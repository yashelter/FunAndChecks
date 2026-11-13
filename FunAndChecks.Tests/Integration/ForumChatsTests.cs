using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Forum;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FunAndChecks.Tests.Integration;

/// <summary>Личные чаты, «Избранное», набор реакций и стикеры.</summary>
[Collection("Integration")]
public class ForumChatsTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private readonly TestWebAppFactory _factory = new();

    [Fact]
    public async Task DirectChats_FollowWhoMayWriteWhom_AndStayPrivate()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        using var mate = Client(seed.MateToken);
        using var outsider = Client(seed.OutsiderToken);

        await ExpectCodeAsync(await student.PostAsJsonAsync("/api/forum/direct", new OpenDirectChatRequest(seed.OutsiderId)),
            HttpStatusCode.Forbidden, "forum.direct_not_allowed");
        (await admin.PostAsJsonAsync("/api/forum/direct", new OpenDirectChatRequest(seed.OutsiderId))).EnsureSuccessStatusCode();
        (await student.PostAsJsonAsync("/api/forum/direct", new OpenDirectChatRequest(seed.MateId))).EnsureSuccessStatusCode();

        var contacts = (await student.GetFromJsonAsync<List<ForumMentionCandidateDto>>("/api/forum/contacts"))!;
        contacts.Select(c => c.UserId).Should().Contain([seed.AdminId, seed.MateId]).And.NotContain(seed.OutsiderId);
        contacts.First().IsAdmin.Should().BeTrue("teachers are listed first");

        var chat = await OpenDirectAsync(student, seed.AdminId);
        (await OpenDirectAsync(admin, seed.StudentId)).Topic.Id.Should().Be(chat.Topic.Id, "one chat per pair");
        chat.Topic.Kind.Should().Be(ForumTopicKind.Direct);
        chat.Topic.Peer!.UserId.Should().Be(seed.AdminId);
        chat.Topic.Title.Should().Be(chat.Topic.Peer.Name);
        chat.Permissions.CanModerate.Should().BeFalse();

        // Пустой чат собеседник ещё не видит; первое сообщение показывает его и присылает уведомление.
        (await PersonalAsync(admin)).Should().NotContain(t => t.Id == chat.Topic.Id);
        var post = await PostAsync(student, chat.Topic.Id, new CreateForumPostRequest("Можно пересдать в четверг?", null, null));
        var listed = (await PersonalAsync(admin)).Single(t => t.Id == chat.Topic.Id);
        listed.UnreadCount.Should().Be(1);
        listed.LastPost!.Text.Should().Be("Можно пересдать в четверг?");
        listed.Peer!.UserId.Should().Be(seed.StudentId);
        var notifications = (await admin.GetFromJsonAsync<NotificationPageDto>("/api/me/notifications"))!;
        notifications.Items.Should().ContainSingle(n => n.Type == NotificationType.DirectMessage && n.Link == $"forum/{chat.Topic.Id}?post={post.Id}");

        var folders = (await admin.GetFromJsonAsync<ForumFoldersDto>("/api/forum/folders"))!;
        folders.PersonalUnreadCount.Should().Be(1);
        folders.Sections.Sum(s => s.TopicCount).Should().Be(0, "private chats are not counted in sections");

        (await outsider.GetAsync($"/api/forum/topics/{chat.Topic.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mate.GetAsync($"/api/forum/topics/{chat.Topic.Id}/posts")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await outsider.GetAsync("/api/forum/topics?search=пересдать")).Content.ReadFromJsonAsync<ForumTopicPageDto>())!
            .Items.Should().BeEmpty();
        // Поиск находит личный чат по имени собеседника.
        (await admin.GetFromJsonAsync<ForumTopicPageDto>("/api/forum/topics?personal=true&search=Student"))!
            .Items.Should().ContainSingle(t => t.Id == chat.Topic.Id);
    }

    [Fact]
    public async Task Saved_IsForTeachersOnly_AndKeepsForwardedMessages()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        using var other = Client(seed.OtherAdminToken);

        await ExpectCodeAsync(await student.PostAsync("/api/forum/saved", null), HttpStatusCode.Forbidden, "forum.saved_admins_only");
        var topic = await CreateTopicAsync(admin, seed.SubjectId);
        var question = await PostAsync(student, topic, new CreateForumPostRequest("Можно защищать в паре?", null, null));

        var forwarded = await (await admin.PostAsJsonAsync($"/api/forum/posts/{question.Id}/forward", new ForwardForumPostRequest(null, true)))
            .Content.ReadFromJsonAsync<ForumPostDto>();
        forwarded!.Body.Should().Be("Можно защищать в паре?");
        forwarded.ForwardedFrom.Should().NotBeNull();
        forwarded.ForwardedFrom!.PostId.Should().Be(question.Id);
        forwarded.ForwardedFrom.TopicTitle.Should().Be("Lab questions");
        forwarded.CanEdit.Should().BeFalse();

        var saved = (await (await admin.PostAsync("/api/forum/saved", null)).Content.ReadFromJsonAsync<ForumTopicDto>())!;
        saved.Topic.Id.Should().Be(forwarded.TopicId);
        saved.Topic.Kind.Should().Be(ForumTopicKind.Saved);
        (await admin.GetFromJsonAsync<ForumFoldersDto>("/api/forum/folders"))!.SavedTopicId.Should().Be(saved.Topic.Id);
        (await admin.GetFromJsonAsync<ForumTopicPageDto>("/api/forum/topics"))!.Items.First().Id.Should().Be(saved.Topic.Id);
        (await other.GetAsync($"/api/forum/topics/{saved.Topic.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await student.GetAsync($"/api/forum/topics/{saved.Topic.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Свои заметки в «Избранном» не дают ни уведомлений, ни непрочитанного.
        await PostAsync(admin, saved.Topic.Id, new CreateForumPostRequest("Ответить на собрании", null, null));
        (await admin.GetFromJsonAsync<ForumFoldersDto>("/api/forum/folders"))!.PersonalUnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Reactions_AreManagedBySuperAdmin()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var super = Client(seed.SuperToken);
        using var student = Client(seed.StudentToken);
        using var anonymous = _factory.CreateClient();
        var topic = await CreateTopicAsync(admin, seed.SubjectId);
        var post = await PostAsync(admin, topic, new CreateForumPostRequest("Дедлайн в пятницу", null, null));

        (await student.GetFromJsonAsync<List<ForumReactionTypeDto>>("/api/forum/reactions"))!.Should().HaveCount(6);
        (await admin.PostAsJsonAsync("/api/admin/forum/reactions", new SaveForumReactionRequest("Огонь", "🔥")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await super.PostAsJsonAsync("/api/admin/forum/reactions", new SaveForumReactionRequest("Огонь", "<b>")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var fire = (await (await super.PostAsJsonAsync("/api/admin/forum/reactions", new SaveForumReactionRequest("Огонь", "🔥")))
            .Content.ReadFromJsonAsync<ForumReactionTypeDto>())!;
        fire.Id.Should().BeGreaterThan(6);

        var image = await UploadAsync(super, $"/api/admin/forum/reactions/{fire.Id}/image", "fire.png", Png);
        image.EnsureSuccessStatusCode();
        var withImage = (await image.Content.ReadFromJsonAsync<ForumReactionTypeDto>())!;
        var file = await anonymous.GetAsync("/" + withImage.ImageUrl);
        file.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        var reactions = (await (await student.PutAsync($"/api/forum/posts/{post.Id}/reactions/{fire.Id}", null))
            .Content.ReadFromJsonAsync<List<ForumReactionSummaryDto>>())!;
        reactions.Should().ContainSingle().Which.Should().Be(new ForumReactionSummaryDto(fire.Id, 1, true));

        (await super.PutAsJsonAsync($"/api/admin/forum/reactions/{fire.Id}", new SaveForumReactionRequest("Огонь", "🔥", false)))
            .EnsureSuccessStatusCode();
        await ExpectCodeAsync(await admin.PutAsync($"/api/forum/posts/{post.Id}/reactions/{fire.Id}", null),
            HttpStatusCode.Conflict, "forum.reaction_disabled");

        var order = (await student.GetFromJsonAsync<List<ForumReactionTypeDto>>("/api/forum/reactions"))!.Select(r => r.Id).Reverse().ToList();
        (await super.PutAsJsonAsync("/api/admin/forum/reactions/order", new ReorderForumItemsRequest(order))).EnsureSuccessStatusCode();
        (await student.GetFromJsonAsync<List<ForumReactionTypeDto>>("/api/forum/reactions"))!.First().Id.Should().Be(fire.Id);

        (await super.DeleteAsync($"/api/admin/forum/reactions/{fire.Id}")).EnsureSuccessStatusCode();
        var posts = (await student.GetFromJsonAsync<ForumPostPageDto>($"/api/forum/topics/{topic}/posts"))!;
        posts.Items.Single(p => p.Id == post.Id).Reactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Stickers_ArePublishedPacks_AndSentAlone()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var super = Client(seed.SuperToken);
        using var student = Client(seed.StudentToken);
        using var anonymous = _factory.CreateClient();
        var topic = await CreateTopicAsync(admin, seed.SubjectId);

        (await admin.PostAsJsonAsync("/api/admin/forum/sticker-packs", new SaveForumStickerPackRequest("Зачётная неделя", false)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var pack = (await (await super.PostAsJsonAsync("/api/admin/forum/sticker-packs", new SaveForumStickerPackRequest("Зачётная неделя", false)))
            .Content.ReadFromJsonAsync<ForumStickerPackDto>())!;
        (await UploadAsync(super, $"/api/admin/forum/sticker-packs/{pack.Id}/stickers", "page.html", "<html></html>"u8.ToArray()))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var sticker = (await (await UploadAsync(super, $"/api/admin/forum/sticker-packs/{pack.Id}/stickers", "zachteno.png", Png))
            .Content.ReadFromJsonAsync<ForumStickerDto>())!;
        sticker.Name.Should().Be("zachteno");

        // Черновик не виден и не отправляется.
        (await student.GetFromJsonAsync<List<ForumStickerPackDto>>("/api/forum/stickers"))!.Should().BeEmpty();
        await ExpectCodeAsync(await student.PostAsJsonAsync($"/api/forum/topics/{topic}/posts", new CreateForumPostRequest(null, null, null, sticker.Id)),
            HttpStatusCode.NotFound, "forum.sticker_not_found");

        (await super.PutAsJsonAsync($"/api/admin/forum/sticker-packs/{pack.Id}", new SaveForumStickerPackRequest("Зачётная неделя", true)))
            .EnsureSuccessStatusCode();
        (await student.GetFromJsonAsync<List<ForumStickerPackDto>>("/api/forum/stickers"))!
            .Should().ContainSingle().Which.Stickers.Should().ContainSingle(s => s.Id == sticker.Id);
        (await student.PostAsJsonAsync($"/api/forum/topics/{topic}/posts", new CreateForumPostRequest("и текст", null, null, sticker.Id)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var sent = await PostAsync(student, topic, new CreateForumPostRequest(null, null, null, sticker.Id));
        sent.Sticker!.Id.Should().Be(sticker.Id);
        sent.CanEdit.Should().BeFalse();
        var image = await anonymous.GetAsync("/" + sent.Sticker.Url);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        image.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        // Удалённый стикер пропадает из панели, но остаётся в отправленном сообщении.
        (await super.DeleteAsync($"/api/admin/forum/stickers/{sticker.Id}")).EnsureSuccessStatusCode();
        (await student.GetFromJsonAsync<List<ForumStickerPackDto>>("/api/forum/stickers"))!.Single().Stickers.Should().BeEmpty();
        var posts = (await student.GetFromJsonAsync<ForumPostPageDto>($"/api/forum/topics/{topic}/posts"))!;
        posts.Items.Single(p => p.Id == sent.Id).Sticker.Should().NotBeNull();
        (await anonymous.GetAsync("/" + sent.Sticker.Url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.GetFromJsonAsync<ForumTopicPageDto>("/api/forum/topics"))!.Items.Single(t => t.Id == topic)
            .LastPost!.IsSticker.Should().BeTrue();
    }

    private static async Task<ForumTopicDto> OpenDirectAsync(HttpClient client, Guid userId)
    {
        var response = await client.PostAsJsonAsync("/api/forum/direct", new OpenDirectChatRequest(userId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ForumTopicDto>())!;
    }

    private static async Task<List<ForumTopicSummaryDto>> PersonalAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ForumTopicPageDto>("/api/forum/topics?personal=true"))!.Items.ToList();

    private static async Task<int> CreateTopicAsync(HttpClient admin, int subjectId)
    {
        var response = await admin.PostAsJsonAsync("/api/forum/topics", new CreateForumTopicRequest(subjectId, "Lab questions", "Ask here", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ForumTopicDto>())!.Topic.Id;
    }

    private static async Task<ForumPostDto> PostAsync(HttpClient client, int topicId, CreateForumPostRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/forum/topics/{topicId}/posts", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ForumPostDto>())!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", name);
        return url.EndsWith("/image") ? await client.PutAsync(url, form) : await client.PostAsync(url, form);
    }

    private static async Task ExpectCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<ChatSeed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin("A");
        var other = db.Admin("B");
        var super = db.Admin("S");
        var group = db.Group("M8O-101");
        var outsiderGroup = db.Group("M8O-202");
        var subject = db.Subject("Operating systems");
        await db.SaveChangesAsync();
        db.GrantSubject(admin, subject);
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group, "Student");
        var mate = db.Student(group, "Mate");
        var outsider = db.Student(outsiderGroup, "Outsider");
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (id, role) in new[] { (admin.Id, Roles.Admin), (other.Id, Roles.Admin), (super.Id, Roles.SuperAdmin),
                     (student.Id, Roles.Student), (mate.Id, Roles.Student), (outsider.Id, Roles.Student) })
            await users.AddToRoleAsync((await users.FindByIdAsync(id.ToString()))!, role);
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new(admin.Id, student.Id, mate.Id, outsider.Id, subject.Id,
            await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(other.Id), await tokens.CreateTokenAsync(super.Id),
            await tokens.CreateTokenAsync(student.Id), await tokens.CreateTokenAsync(mate.Id), await tokens.CreateTokenAsync(outsider.Id));
    }

    public void Dispose() => _factory.Dispose();

    private record ChatSeed(Guid AdminId, Guid StudentId, Guid MateId, Guid OutsiderId, int SubjectId,
        string AdminToken, string OtherAdminToken, string SuperToken, string StudentToken, string MateToken, string OutsiderToken);
}

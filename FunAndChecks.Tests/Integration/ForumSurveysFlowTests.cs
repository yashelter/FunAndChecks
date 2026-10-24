using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FunAndChecks.Application.Attachments;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Forum;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Application.Surveys;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Infrastructure.Identity;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Tests.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FunAndChecks.Tests.Integration;

[Collection("Integration")]
public class ForumSurveysFlowTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private readonly FlowFactory _factory = new();

    [Fact]
    public async Task Forum_IsScopedBySubject_AndOnlyTeachersStartTopics()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        using var outsider = Client(seed.OutsiderToken);
        using var otherAdmin = Client(seed.OtherAdminToken);

        (await student.PostAsJsonAsync("/api/forum/topics", Topic(seed.SubjectId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await otherAdmin.PostAsJsonAsync("/api/forum/topics", Topic(seed.SubjectId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var subjectTopic = await CreateTopicAsync(admin, Topic(seed.SubjectId));
        var generalTopic = await CreateTopicAsync(admin, Topic(null) with { Title = "General news" });

        var sections = (await student.GetFromJsonAsync<List<ForumSectionDto>>("/api/forum/sections"))!;
        sections.Select(s => s.SubjectId).Should().BeEquivalentTo(new int?[] { null, seed.SubjectId });
        sections.Single(s => s.SubjectId == seed.SubjectId).UnreadTopicCount.Should().Be(1);
        var outsiderTopics = (await outsider.GetFromJsonAsync<ForumTopicPageDto>("/api/forum/topics"))!;
        outsiderTopics.Items.Select(t => t.Id).Should().Equal(generalTopic.Topic.Id);
        (await outsider.GetAsync($"/api/forum/topics/{subjectTopic.Topic.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherAdmin.GetAsync($"/api/forum/topics/{subjectTopic.Topic.Id}/posts")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.PostAsJsonAsync($"/api/forum/topics/{subjectTopic.Topic.Id}/posts", Post("hi")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var found = (await student.GetFromJsonAsync<ForumTopicPageDto>("/api/forum/topics?search=LAB"))!;
        found.Items.Should().ContainSingle(t => t.Id == subjectTopic.Topic.Id);
    }

    [Fact]
    public async Task Forum_RepliesWithFiles_MentionsModerationAndBans()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        using var anonymous = _factory.CreateClient();
        var topic = await CreateTopicAsync(admin, Topic(seed.SubjectId));
        var topicId = topic.Topic.Id;

        var html = await UploadAsync(student, "page.html", "<html><script>alert(1)</script></html>"u8.ToArray());
        html.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var image = (await (await UploadAsync(student, "shot.png", Png)).Content.ReadFromJsonAsync<AttachmentDto>())!;
        image.IsImage.Should().BeTrue();
        image.ContentType.Should().Be("image/png");

        var file = await anonymous.GetAsync("/" + image.Url);
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        file.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        file.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await anonymous.GetAsync("/" + image.Url.Replace("sig=", "sig=x"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = $"Посмотрите, @[A Admin](user:{seed.AdminId})";
        var reply = await student.PostAsJsonAsync($"/api/forum/topics/{topicId}/posts",
            new CreateForumPostRequest(body, null, [image.Id]));
        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        var post = (await reply.Content.ReadFromJsonAsync<ForumPostDto>())!;
        post.Attachments.Should().ContainSingle().Which.Id.Should().Be(image.Id);
        // Файл уже привязан — второй раз его не прикрепить.
        (await student.PostAsJsonAsync($"/api/forum/topics/{topicId}/posts", new CreateForumPostRequest("again", null, [image.Id])))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var notifications = (await admin.GetFromJsonAsync<NotificationPageDto>("/api/me/notifications"))!;
        notifications.Items.Should().ContainSingle(n => n.Type == NotificationType.ForumMention && n.Link == $"forum/{topicId}?post={post.Id}");
        notifications.Items.Should().NotContain(n => n.Type == NotificationType.ForumReply);
        (await admin.GetFromJsonAsync<ForumUnreadDto>("/api/forum/unread"))!.UnreadTopicCount.Should().Be(1);
        (await admin.PostAsJsonAsync($"/api/forum/topics/{topicId}/read", new MarkTopicReadRequest(post.Id))).EnsureSuccessStatusCode();
        (await admin.GetFromJsonAsync<ForumUnreadDto>("/api/forum/unread"))!.UnreadTopicCount.Should().Be(0);

        (await student.PostAsync($"/api/forum/posts/{post.Id}/pin", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/forum/posts/{post.Id}/pin", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var pinned = (await student.GetFromJsonAsync<ForumTopicDto>($"/api/forum/topics/{topicId}"))!;
        pinned.PinnedPosts.Should().ContainSingle(p => p.Id == post.Id);
        pinned.Permissions.IsSubscribed.Should().BeTrue();

        (await student.PutAsync($"/api/forum/posts/{post.Id}/reactions/{ForumReactionKind.Party}", null)).EnsureSuccessStatusCode();
        var reactions = (await (await admin.PutAsync($"/api/forum/posts/{post.Id}/reactions/{ForumReactionKind.Party}", null))
            .Content.ReadFromJsonAsync<List<ForumReactionSummaryDto>>())!;
        reactions.Should().ContainSingle().Which.Should().Be(new ForumReactionSummaryDto(ForumReactionKind.Party, 2, true));

        (await student.PostAsJsonAsync($"/api/forum/posts/{post.Id}/report", new ReportForumPostRequest("spam"))).EnsureSuccessStatusCode();
        var reports = (await admin.GetFromJsonAsync<ForumReportPageDto>("/api/admin/forum/reports"))!;
        reports.Items.Should().ContainSingle(r => r.PostId == post.Id && r.Reason == "spam");

        (await admin.PatchAsJsonAsync($"/api/forum/topics/{topicId}", new UpdateForumTopicRequest(IsClosed: true))).EnsureSuccessStatusCode();
        await ExpectCodeAsync(await student.PostAsJsonAsync($"/api/forum/topics/{topicId}/posts", Post("late")),
            HttpStatusCode.Conflict, "forum.topic_closed");
        (await admin.PostAsJsonAsync($"/api/forum/topics/{topicId}/posts", Post("Closing note"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var general = await CreateTopicAsync(admin, Topic(null));
        (await admin.PutAsJsonAsync($"/api/admin/forum/bans/{seed.StudentId}",
            new BanForumUserRequest(DateTime.UtcNow.AddDays(1), "flood"))).EnsureSuccessStatusCode();
        await ExpectCodeAsync(await student.PostAsJsonAsync($"/api/forum/topics/{general.Topic.Id}/posts", Post("hi")),
            HttpStatusCode.Forbidden, "forum.banned");
        (await admin.DeleteAsync($"/api/admin/forum/bans/{seed.StudentId}")).EnsureSuccessStatusCode();

        (await student.DeleteAsync($"/api/forum/posts/{post.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var posts = (await admin.GetFromJsonAsync<ForumPostPageDto>($"/api/forum/topics/{topicId}/posts"))!;
        posts.Items.Single(p => p.Id == post.Id).Should().Match<ForumPostDto>(p => p.IsDeleted && p.Body == "" && p.Attachments.Count == 0);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Attachments.SingleAsync(a => a.Id == image.Id)).ForumPostId.Should().BeNull();
        (await db.ForumTopics.SingleAsync(t => t.Id == topicId)).PostCount.Should().Be(2);
    }

    [Fact]
    public async Task Survey_AppearsAfterAcceptedSubmissions_AndKeepsAnonymity()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        using var outsider = Client(seed.OutsiderToken);
        var survey = await CreateSurveyAsync(admin, Survey(seed.SubjectId, SurveyTriggerType.AcceptedInSubject, 2) with
            { IsAnonymous = true, AllowSnooze = true, MaxSnoozes = 1 });
        (await admin.PostAsync($"/api/admin/surveys/{survey.Id}/publish", null)).EnsureSuccessStatusCode();
        await ExpectCodeAsync(await admin.PutAsJsonAsync($"/api/admin/surveys/{survey.Id}", Survey(seed.SubjectId, SurveyTriggerType.Immediate, 0)),
            HttpStatusCode.Conflict, "survey.locked");

        (await PendingAsync(student)).Should().BeEmpty();
        await SubmitAsync(admin, seed.StudentId, seed.TaskIds[0], SubmissionStatus.Accepted);
        await SubmitAsync(admin, seed.StudentId, seed.TaskIds[1], SubmissionStatus.Rejected);
        (await PendingAsync(student)).Should().BeEmpty();
        await SubmitAsync(admin, seed.StudentId, seed.TaskIds[1], SubmissionStatus.Accepted);
        (await PendingAsync(student)).Should().ContainSingle(p => p.Id == survey.Id && p.IsAnonymous && p.CanSnooze);
        (await PendingAsync(outsider)).Should().BeEmpty();
        (await outsider.GetAsync($"/api/surveys/{survey.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await student.PostAsync($"/api/surveys/{survey.Id}/snooze", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PendingAsync(student)).Should().BeEmpty();
        await ExpectCodeAsync(await student.PostAsync($"/api/surveys/{survey.Id}/snooze", null), HttpStatusCode.Conflict, "survey.snooze_limit");

        var form = (await student.GetFromJsonAsync<SurveyFormDto>($"/api/surveys/{survey.Id}"))!;
        var choice = form.Questions.Single(q => q.Type == SurveyQuestionType.SingleChoice);
        var scale = form.Questions.Single(q => q.Type == SurveyQuestionType.Scale);
        var text = form.Questions.Single(q => q.Type == SurveyQuestionType.LongText);
        (await student.PostAsJsonAsync($"/api/surveys/{survey.Id}/responses", new SubmitSurveyRequest(
            [new(text.Id, null, null, "Only text", null)]))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await student.PostAsJsonAsync($"/api/surveys/{survey.Id}/responses", new SubmitSurveyRequest(
            [new(choice.Id, [choice.Options[0].Id], null, null, null), new(scale.Id, null, null, null, 11)])))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var answers = new SubmitSurveyRequest([
            new(choice.Id, [choice.Options[1].Id], null, null, null), new(scale.Id, null, null, null, 4),
            new(text.Id, null, null, "Больше примеров", null)]);
        (await student.PostAsJsonAsync($"/api/surveys/{survey.Id}/responses", answers)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await ExpectCodeAsync(await student.PostAsJsonAsync($"/api/surveys/{survey.Id}/responses", answers),
            HttpStatusCode.Conflict, "survey.already_completed");
        (await PendingAsync(student)).Should().BeEmpty();
        (await student.GetFromJsonAsync<List<MySurveyDto>>("/api/me/surveys"))!.Should().ContainSingle(s => s.Id == survey.Id);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var response = await db.SurveyResponses.SingleAsync(r => r.SurveyId == survey.Id);
            response.RespondentId.Should().BeNull();
            response.SubmittedAtUtc.TimeOfDay.Should().Be(TimeSpan.Zero);
            (await db.SurveyParticipations.SingleAsync(p => p.SurveyId == survey.Id)).Status.Should().Be(SurveyParticipationStatus.Completed);
        }

        var results = (await admin.GetFromJsonAsync<SurveyResultsDto>($"/api/admin/surveys/{survey.Id}/results"))!;
        results.ResponseCount.Should().Be(1);
        results.AudienceCount.Should().Be(1);
        results.HiddenForAnonymity.Should().BeTrue();
        results.Questions.Should().BeEmpty();
        await ExpectCodeAsync(await admin.GetAsync($"/api/admin/surveys/{survey.Id}/responses"), HttpStatusCode.Conflict,
            "survey.anonymity_threshold");
    }

    [Fact]
    public async Task Survey_AttemptTriggerCountsRejections_AndNamedResultsExport()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        var survey = await CreateSurveyAsync(admin, Survey(seed.SubjectId, SurveyTriggerType.AttemptsInSubject, 2));
        (await admin.PostAsync($"/api/admin/surveys/{survey.Id}/publish", null)).EnsureSuccessStatusCode();
        await SubmitAsync(admin, seed.StudentId, seed.TaskIds[0], SubmissionStatus.Rejected);
        (await PendingAsync(student)).Should().BeEmpty();
        await SubmitAsync(admin, seed.StudentId, seed.TaskIds[0], SubmissionStatus.Rejected);
        (await PendingAsync(student)).Should().ContainSingle(p => p.Id == survey.Id && !p.CanSnooze);
        await ExpectCodeAsync(await student.PostAsync($"/api/surveys/{survey.Id}/snooze", null), HttpStatusCode.Conflict,
            "survey.snooze_not_allowed");

        var form = (await student.GetFromJsonAsync<SurveyFormDto>($"/api/surveys/{survey.Id}"))!;
        var choice = form.Questions.Single(q => q.Type == SurveyQuestionType.SingleChoice);
        var scale = form.Questions.Single(q => q.Type == SurveyQuestionType.Scale);
        (await student.PostAsJsonAsync($"/api/surveys/{survey.Id}/responses", new SubmitSurveyRequest([
            new(choice.Id, null, "Свой вариант", null, null), new(scale.Id, null, null, null, 5)]))).EnsureSuccessStatusCode();

        var results = (await admin.GetFromJsonAsync<SurveyResultsDto>($"/api/admin/surveys/{survey.Id}/results"))!;
        results.HiddenForAnonymity.Should().BeFalse();
        var choiceResult = results.Questions.Single(q => q.QuestionId == choice.Id);
        choiceResult.Options.Single(o => o.OptionId == null).Count.Should().Be(1);
        choiceResult.TextAnswers.Should().Equal("Свой вариант");
        results.Questions.Single(q => q.QuestionId == scale.Id).Average.Should().Be(5);
        var responses = (await admin.GetFromJsonAsync<List<SurveyIndividualResponseDto>>($"/api/admin/surveys/{survey.Id}/responses"))!;
        responses.Should().ContainSingle(r => r.RespondentId == seed.StudentId && r.RespondentName == "Student Test");
        var export = await admin.GetAsync($"/api/admin/surveys/{survey.Id}/export.xlsx");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        (await export.Content.ReadAsByteArrayAsync()).Take(2).Should().Equal((byte)'P', (byte)'K');

        // Разбивка по группам: охват считается по аудитории, фильтр сужает и сводку, и ответы.
        var group = results.Groups.Should().ContainSingle().Which;
        (group.ResponseCount, group.AudienceCount).Should().Be((1, 1));
        var scoped = (await admin.GetFromJsonAsync<SurveyResultsDto>($"/api/admin/surveys/{survey.Id}/results?groupId={group.GroupId}"))!;
        (scoped.GroupId, scoped.ResponseCount, scoped.AudienceCount).Should().Be((group.GroupId, 1, 1));
        (await admin.GetFromJsonAsync<List<SurveyIndividualResponseDto>>($"/api/admin/surveys/{survey.Id}/responses?groupId={group.GroupId}"))!
            .Should().ContainSingle();
        var foreign = (await admin.GetFromJsonAsync<SurveyResultsDto>($"/api/admin/surveys/{survey.Id}/results?groupId={group.GroupId + 1000}"))!;
        (foreign.ResponseCount, foreign.AudienceCount).Should().Be((0, 0));
    }

    [Fact]
    public async Task SurveyDraft_CanBeRewritten_AndIsHiddenFromTeachersWithoutTheSubject()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var otherAdmin = Client(seed.OtherAdminToken);
        using var student = Client(seed.StudentToken);
        var draft = await CreateSurveyAsync(admin, Survey(seed.SubjectId, SurveyTriggerType.Immediate, 0) with { Questions = [] });
        await ExpectCodeAsync(await admin.PostAsync($"/api/admin/surveys/{draft.Id}/publish", null), HttpStatusCode.Conflict, "survey.no_questions");

        var rewritten = Survey(seed.SubjectId, SurveyTriggerType.Immediate, 0) with { Title = "Новое название" };
        var saved = await admin.PutAsJsonAsync($"/api/admin/surveys/{draft.Id}", rewritten);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = (await (await admin.PutAsJsonAsync($"/api/admin/surveys/{draft.Id}",
            rewritten with { Questions = rewritten.Questions!.Take(1).ToList() })).Content.ReadFromJsonAsync<SurveyAdminDto>())!;
        again.Title.Should().Be("Новое название");
        again.Questions.Should().ContainSingle().Which.Options.Select(o => o.Text).Should().Equal("Легко", "Нормально", "Сложно");

        (await otherAdmin.GetAsync($"/api/admin/surveys/{draft.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await otherAdmin.GetFromJsonAsync<SurveyPageDto>("/api/admin/surveys"))!.Items.Should().BeEmpty();
        (await PendingAsync(student)).Should().BeEmpty();
        (await admin.PostAsync($"/api/admin/surveys/{draft.Id}/publish", null)).EnsureSuccessStatusCode();
        (await PendingAsync(student)).Should().ContainSingle(p => p.Id == draft.Id);
        (await student.GetFromJsonAsync<NotificationPageDto>("/api/me/notifications"))!.Items
            .Should().ContainSingle(n => n.Type == NotificationType.SurveyAvailable && n.Link == $"surveys/{draft.Id}");
        (await admin.PostAsync($"/api/admin/surveys/{draft.Id}/close", null)).EnsureSuccessStatusCode();
        (await PendingAsync(student)).Should().BeEmpty();
    }

    [Fact]
    public async Task ProfileStats_ShowProgressOnlyToOwnerAndTeachers()
    {
        var seed = await SeedAsync();
        using var admin = Client(seed.AdminToken);
        using var student = Client(seed.StudentToken);
        using var outsider = Client(seed.OutsiderToken);
        await SubmitAsync(admin, seed.StudentId, seed.TaskIds[0], SubmissionStatus.Accepted);

        var mine = (await student.GetFromJsonAsync<ProfileStatsDto>("/api/me/profile/stats"))!;
        mine.Detailed.Should().BeTrue();
        mine.Subjects.Should().ContainSingle().Which.Should().Match<SubjectProgressDto>(s => s.AcceptedTasks == 1 && s.TotalTasks == 3 && s.Points == 10);
        (await admin.GetFromJsonAsync<ProfileStatsDto>($"/api/profiles/{seed.StudentId}/stats"))!.Detailed.Should().BeTrue();
        var foreign = (await outsider.GetFromJsonAsync<ProfileStatsDto>($"/api/profiles/{seed.StudentId}/stats"))!;
        foreign.Detailed.Should().BeFalse();
        foreign.Subjects.Should().BeEmpty();

        var avatars = await student.GetFromJsonAsync<Dictionary<Guid, MediaAssetDto>>(
            $"/api/profiles/avatars?ids={seed.AdminId}&ids={seed.OutsiderId}");
        avatars.Should().BeEmpty();
    }

    private static CreateForumTopicRequest Topic(int? subjectId) =>
        new(subjectId, "Lab 1 questions", "Ask here", null);

    private static CreateForumPostRequest Post(string body) => new(body, null, null);

    private static SaveSurveyRequest Survey(int subjectId, SurveyTriggerType trigger, int threshold) =>
        new("Как прошли лабы?", "Пара вопросов", subjectId, null, false, false, 24, null, null, null, trigger, threshold,
        [
            new("Сложность", null, SurveyQuestionType.SingleChoice, true, true, null, null, null, null, null, ["Легко", "Нормально", "Сложно"]),
            new("Оценка курса", null, SurveyQuestionType.Scale, true, false, null, 1, 5, "плохо", "отлично", null),
            new("Что улучшить?", null, SurveyQuestionType.LongText, false, false, null, null, null, null, null, null),
        ]);

    private static async Task<ForumTopicDto> CreateTopicAsync(HttpClient client, CreateForumTopicRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/forum/topics", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ForumTopicDto>())!;
    }

    private static async Task<SurveyAdminDto> CreateSurveyAsync(HttpClient client, SaveSurveyRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/admin/surveys", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<SurveyAdminDto>())!;
    }

    private static async Task<List<PendingSurveyDto>> PendingAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PendingSurveyDto>>("/api/surveys/pending"))!;

    private static async Task SubmitAsync(HttpClient admin, Guid studentId, int taskId, SubmissionStatus status) =>
        (await admin.PostAsJsonAsync("/api/submissions", new CreateSubmissionRequest(studentId, taskId, status, null)))
            .EnsureSuccessStatusCode();

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string name, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", name);
        return await client.PostAsync("/api/attachments", form);
    }

    private static async Task ExpectCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<FlowSeed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = db.Admin("A");
        var other = db.Admin("B");
        var group = db.Group("M8O-101");
        var outsiderGroup = db.Group("M8O-202");
        var subject = db.Subject("Operating systems");
        await db.SaveChangesAsync();
        db.GrantSubject(admin, subject);
        db.LinkGroupSubject(group, subject);
        var student = db.Student(group, "Student");
        var outsider = db.Student(outsiderGroup, "Outsider");
        var tasks = new[] { db.Task(subject, 10, "Lab 1"), db.Task(subject, 10, "Lab 2"), db.Task(subject, 10, "Lab 3") };
        await db.SaveChangesAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await users.AddToRoleAsync((await users.FindByIdAsync(admin.Id.ToString()))!, Roles.Admin);
        await users.AddToRoleAsync((await users.FindByIdAsync(other.Id.ToString()))!, Roles.Admin);
        await users.AddToRoleAsync((await users.FindByIdAsync(student.Id.ToString()))!, Roles.Student);
        await users.AddToRoleAsync((await users.FindByIdAsync(outsider.Id.ToString()))!, Roles.Student);
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        return new(admin.Id, student.Id, outsider.Id, subject.Id, tasks.Select(t => t.Id).ToArray(),
            await tokens.CreateTokenAsync(admin.Id), await tokens.CreateTokenAsync(other.Id),
            await tokens.CreateTokenAsync(student.Id), await tokens.CreateTokenAsync(outsider.Id));
    }

    public void Dispose() => _factory.Dispose();

    private record FlowSeed(Guid AdminId, Guid StudentId, Guid OutsiderId, int SubjectId, int[] TaskIds,
        string AdminToken, string OtherAdminToken, string StudentToken, string OutsiderToken);

    private sealed class FlowFactory : TestWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage, MemoryFileStorage>();
            });
        }
    }

    private sealed class MemoryFileStorage : IFileStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new();

        public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = $"2026/10/{Guid.NewGuid():N}";
            _files[key] = buffer.ToArray();
            return key;
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            _files.TryGetValue(key, out var data)
                ? Task.FromResult<Stream>(new MemoryStream(data))
                : throw new NotFoundException("The file is no longer available.", "attachment.not_found");

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _files.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}

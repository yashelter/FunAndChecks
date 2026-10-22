using FunAndChecks.Application.Forum;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using FunAndChecks.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FunAndChecks.Controllers;

[ApiController]
[Authorize]
public class ForumController(IForumService forum, IForumModerationService moderation) : ControllerBase
{
    [HttpGet("api/forum/sections")]
    public async Task<ActionResult<IReadOnlyList<ForumSectionDto>>> Sections(CancellationToken ct) =>
        Ok(await forum.GetSectionsAsync(User.GetUserId(), ct));

    [HttpGet("api/forum/unread")]
    public async Task<ActionResult<ForumUnreadDto>> Unread(CancellationToken ct) =>
        Ok(await forum.GetUnreadAsync(User.GetUserId(), ct));

    /// <summary>Темы: subjectId — раздел предмета, general=true — «Общее», без параметров — все видимые.</summary>
    [HttpGet("api/forum/topics")]
    public async Task<ActionResult<ForumTopicPageDto>> Topics([FromQuery] int? subjectId, [FromQuery] bool general = false,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await forum.GetTopicsAsync(User.GetUserId(), subjectId, general, search, page, pageSize, ct));

    [HttpPost("api/forum/topics")]
    [EnableRateLimiting(RateLimitPolicies.ForumWrite)]
    public async Task<ActionResult<ForumTopicDto>> CreateTopic(CreateForumTopicRequest request, CancellationToken ct)
    {
        var topic = await forum.CreateTopicAsync(User.GetUserId(), request, ct);
        return Created($"/api/forum/topics/{topic.Topic.Id}", topic);
    }

    [HttpGet("api/forum/topics/{topicId:int}")]
    public async Task<ActionResult<ForumTopicDto>> Topic(int topicId, CancellationToken ct) =>
        Ok(await forum.GetTopicAsync(User.GetUserId(), topicId, ct));

    [HttpPatch("api/forum/topics/{topicId:int}")]
    public async Task<ActionResult<ForumTopicDto>> UpdateTopic(int topicId, UpdateForumTopicRequest request, CancellationToken ct) =>
        Ok(await forum.UpdateTopicAsync(User.GetUserId(), topicId, request, ct));

    [HttpDelete("api/forum/topics/{topicId:int}")]
    public async Task<IActionResult> DeleteTopic(int topicId, CancellationToken ct)
    {
        await forum.DeleteTopicAsync(User.GetUserId(), topicId, ct);
        return NoContent();
    }

    /// <summary>aroundPostId открывает страницу с нужным сообщением (ссылки из уведомлений).</summary>
    [HttpGet("api/forum/topics/{topicId:int}/posts")]
    public async Task<ActionResult<ForumPostPageDto>> Posts(int topicId, [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        [FromQuery] long? aroundPostId = null, CancellationToken ct = default) =>
        Ok(await forum.GetPostsAsync(User.GetUserId(), topicId, page, pageSize, aroundPostId, ct));

    [HttpPost("api/forum/topics/{topicId:int}/posts")]
    [EnableRateLimiting(RateLimitPolicies.ForumWrite)]
    public async Task<ActionResult<ForumPostDto>> CreatePost(int topicId, CreateForumPostRequest request, CancellationToken ct) =>
        Ok(await forum.CreatePostAsync(User.GetUserId(), topicId, request, ct));

    [HttpPost("api/forum/topics/{topicId:int}/read")]
    public async Task<IActionResult> MarkRead(int topicId, MarkTopicReadRequest request, CancellationToken ct)
    {
        await forum.MarkReadAsync(User.GetUserId(), topicId, request.LastPostId, ct);
        return NoContent();
    }

    [HttpPut("api/forum/topics/{topicId:int}/subscription")]
    public async Task<IActionResult> Subscribe(int topicId, CancellationToken ct)
    {
        await forum.SetSubscriptionAsync(User.GetUserId(), topicId, true, ct);
        return NoContent();
    }

    [HttpDelete("api/forum/topics/{topicId:int}/subscription")]
    public async Task<IActionResult> Unsubscribe(int topicId, CancellationToken ct)
    {
        await forum.SetSubscriptionAsync(User.GetUserId(), topicId, false, ct);
        return NoContent();
    }

    [HttpGet("api/forum/topics/{topicId:int}/mention-candidates")]
    public async Task<ActionResult<IReadOnlyList<ForumMentionCandidateDto>>> MentionCandidates(int topicId, [FromQuery] string? query,
        CancellationToken ct) =>
        Ok(await forum.GetMentionCandidatesAsync(User.GetUserId(), topicId, query, ct));

    [HttpPut("api/forum/posts/{postId:long}")]
    [EnableRateLimiting(RateLimitPolicies.ForumWrite)]
    public async Task<ActionResult<ForumPostDto>> UpdatePost(long postId, UpdateForumPostRequest request, CancellationToken ct) =>
        Ok(await forum.UpdatePostAsync(User.GetUserId(), postId, request, ct));

    [HttpDelete("api/forum/posts/{postId:long}")]
    public async Task<IActionResult> DeletePost(long postId, CancellationToken ct)
    {
        await forum.DeletePostAsync(User.GetUserId(), postId, ct);
        return NoContent();
    }

    [HttpPost("api/forum/posts/{postId:long}/pin")]
    public async Task<IActionResult> Pin(long postId, CancellationToken ct)
    {
        await forum.SetPostPinnedAsync(User.GetUserId(), postId, true, ct);
        return NoContent();
    }

    [HttpDelete("api/forum/posts/{postId:long}/pin")]
    public async Task<IActionResult> Unpin(long postId, CancellationToken ct)
    {
        await forum.SetPostPinnedAsync(User.GetUserId(), postId, false, ct);
        return NoContent();
    }

    [HttpPut("api/forum/posts/{postId:long}/reactions/{kind}")]
    public async Task<ActionResult<IReadOnlyList<ForumReactionSummaryDto>>> React(long postId, ForumReactionKind kind, CancellationToken ct) =>
        Ok(await forum.SetReactionAsync(User.GetUserId(), postId, kind, true, ct));

    [HttpDelete("api/forum/posts/{postId:long}/reactions/{kind}")]
    public async Task<ActionResult<IReadOnlyList<ForumReactionSummaryDto>>> Unreact(long postId, ForumReactionKind kind, CancellationToken ct) =>
        Ok(await forum.SetReactionAsync(User.GetUserId(), postId, kind, false, ct));

    [HttpPost("api/forum/posts/{postId:long}/report")]
    [EnableRateLimiting(RateLimitPolicies.ForumWrite)]
    public async Task<IActionResult> Report(long postId, ReportForumPostRequest request, CancellationToken ct)
    {
        await moderation.ReportAsync(User.GetUserId(), postId, request, ct);
        return NoContent();
    }

    [HttpGet("api/admin/forum/reports")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<ForumReportPageDto>> Reports([FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Ok(await moderation.GetReportsAsync(User.GetUserId(), page, pageSize, ct));

    [HttpPost("api/admin/forum/reports/{reportId:int}/resolve")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Resolve(int reportId, ResolveForumReportRequest request, CancellationToken ct)
    {
        await moderation.ResolveReportAsync(User.GetUserId(), reportId, request, ct);
        return NoContent();
    }

    [HttpGet("api/admin/forum/bans")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IReadOnlyList<ForumBanDto>>> Bans(CancellationToken ct) =>
        Ok(await moderation.GetBansAsync(User.GetUserId(), ct));

    [HttpPut("api/admin/forum/bans/{userId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Ban(Guid userId, BanForumUserRequest request, CancellationToken ct)
    {
        await moderation.BanAsync(User.GetUserId(), userId, request, ct);
        return NoContent();
    }

    [HttpDelete("api/admin/forum/bans/{userId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Unban(Guid userId, CancellationToken ct)
    {
        await moderation.UnbanAsync(User.GetUserId(), userId, ct);
        return NoContent();
    }
}

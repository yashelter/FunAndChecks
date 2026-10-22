using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Surveys;

public interface ISurveyAdminService
{
    Task<SurveyPageDto> GetAsync(Guid adminId, SurveyStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<SurveyAdminDto> GetByIdAsync(Guid adminId, int id, CancellationToken ct = default);
    Task<SurveyAdminDto> CreateAsync(Guid adminId, SaveSurveyRequest request, CancellationToken ct = default);
    Task<SurveyAdminDto> SaveDraftAsync(Guid adminId, int id, SaveSurveyRequest request, CancellationToken ct = default);
    Task<SurveyAdminDto> UpdateDetailsAsync(Guid adminId, int id, UpdateSurveyDetailsRequest request, CancellationToken ct = default);
    Task<SurveyAdminDto> PublishAsync(Guid adminId, int id, CancellationToken ct = default);
    Task<SurveyAdminDto> CloseAsync(Guid adminId, int id, CancellationToken ct = default);
    Task<SurveyAdminDto> DuplicateAsync(Guid adminId, int id, CancellationToken ct = default);
    Task DeleteAsync(Guid adminId, int id, CancellationToken ct = default);
    /// <summary>Итоги; groupId — только ответы этой группы (порог анонимности считается по ним же).</summary>
    Task<SurveyResultsDto> GetResultsAsync(Guid adminId, int id, int? groupId = null, CancellationToken ct = default);
    Task<IReadOnlyList<SurveyIndividualResponseDto>> GetResponsesAsync(Guid adminId, int id, int? groupId = null, CancellationToken ct = default);
}

public class SurveyAdminService(
    IApplicationDbContext db,
    IAdminAccessService adminAccess,
    IIdentityService identity,
    INotificationService notifications,
    IUserNotifier userNotifier,
    IValidator<SaveSurveyRequest> validator) : ISurveyAdminService
{
    public async Task<SurveyPageDto> GetAsync(Guid adminId, SurveyStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        ProfileValidation.Page(page, pageSize);
        var allowed = (await adminAccess.GetAccessAsync(adminId, ct)).AllowedSubjectIds.ToList();
        var surveys = db.Surveys.AsNoTracking().Where(s => s.SubjectId == null || allowed.Contains(s.SubjectId.Value));
        if (status is SurveyStatus value) surveys = surveys.Where(s => s.Status == value);
        var total = await surveys.CountAsync(ct);
        var items = await surveys.OrderBy(s => s.Status == SurveyStatus.Closed).ThenByDescending(s => s.UpdatedAtUtc).ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SurveySummaryDto(s.Id, s.Title, s.SubjectId, s.Subject == null ? null : s.Subject.Name, s.Status,
                s.IsAnonymous, s.TriggerType, s.TriggerThreshold, s.Questions.Count,
                db.SurveyResponses.Count(r => r.SurveyId == s.Id), s.CreatedByName, s.CreatedAtUtc, s.PublishedAtUtc, s.ClosesAtUtc))
            .ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<SurveyAdminDto> GetByIdAsync(Guid adminId, int id, CancellationToken ct = default) =>
        await ToDtoAsync(await RequireAsync(adminId, id, ct), ct);

    public async Task<SurveyAdminDto> CreateAsync(Guid adminId, SaveSurveyRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await EnsureScopeAsync(adminId, request, ct);
        var account = await identity.GetProfileAccountAsync(adminId, ct)
            ?? throw new NotFoundException("Profile not found.", "profile.not_found");
        var survey = new Survey { Title = "", CreatedById = adminId, CreatedByName = account.FullName, CreatedAtUtc = DateTime.UtcNow };
        Apply(survey, request);
        db.Surveys.Add(survey);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(adminId, survey.Id, ct);
    }

    public async Task<SurveyAdminDto> SaveDraftAsync(Guid adminId, int id, SaveSurveyRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        await EnsureScopeAsync(adminId, request, ct);
        await db.ExecuteSerializableAsync(async token =>
        {
            var survey = await RequireAsync(adminId, id, token);
            if (survey.Status != SurveyStatus.Draft)
                throw new ConflictException("Questions are locked after publication. Duplicate the survey to change them.", "survey.locked");
            db.SurveyQuestions.RemoveRange(survey.Questions);
            db.SurveyTargetGroups.RemoveRange(survey.TargetGroups);
            await db.SaveChangesAsync(token);
            Apply(survey, request);
            await db.SaveChangesAsync(token);
        }, ct);
        return await GetByIdAsync(adminId, id, ct);
    }

    public async Task<SurveyAdminDto> UpdateDetailsAsync(Guid adminId, int id, UpdateSurveyDetailsRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            throw new ValidationException("The title must have 1–200 characters.");
        if (request.Description?.Length > 4000) throw new ValidationException("The description is too long.");
        var survey = await RequireAsync(adminId, id, ct);
        if (request.ClosesAtUtc is DateTime closes && survey.OpensAtUtc is DateTime opens && closes <= opens)
            throw new ValidationException("The survey must close after it opens.");
        survey.Title = request.Title.Trim();
        survey.Description = request.Description?.Trim() ?? "";
        survey.ClosesAtUtc = Utc(request.ClosesAtUtc);
        survey.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(survey, ct);
    }

    public async Task<SurveyAdminDto> PublishAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        Survey survey = null!;
        await db.ExecuteSerializableAsync(async token =>
        {
            survey = await RequireAsync(adminId, id, token);
            if (survey.Status != SurveyStatus.Draft) throw new ConflictException("The survey is already published.", "survey.not_draft");
            if (survey.Questions.Count == 0) throw new ConflictException("Add at least one question.", "survey.no_questions");
            survey.Status = SurveyStatus.Published;
            survey.PublishedAtUtc = survey.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(token);
        }, ct);
        var audience = await AudienceAsync(survey, ct);
        if (survey.TriggerType == SurveyTriggerType.Immediate &&
            (survey.OpensAtUtc is null || survey.OpensAtUtc <= DateTime.UtcNow))
            await notifications.NotifyAsync(audience, new(NotificationType.SurveyAvailable, survey.Title, survey.CreatedByName,
                $"surveys/{survey.Id}"), ct);
        foreach (var student in audience)
        {
            try { await userNotifier.SurveysChangedAsync(student, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* Подсказка клиенту, не данные. */ }
        }
        return await ToDtoAsync(survey, ct);
    }

    public async Task<SurveyAdminDto> CloseAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        var survey = await RequireAsync(adminId, id, ct);
        if (survey.Status != SurveyStatus.Published) throw new ConflictException("Only a published survey can be closed.", "survey.not_published");
        survey.Status = SurveyStatus.Closed;
        survey.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(survey, ct);
    }

    public async Task<SurveyAdminDto> DuplicateAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        var source = await RequireAsync(adminId, id, ct);
        var account = await identity.GetProfileAccountAsync(adminId, ct)
            ?? throw new NotFoundException("Profile not found.", "profile.not_found");
        var copy = new Survey
        {
            Title = Truncate($"{source.Title} (copy)", 200), Description = source.Description, SubjectId = source.SubjectId,
            CreatedById = adminId, CreatedByName = account.FullName, IsAnonymous = source.IsAnonymous, AllowSnooze = source.AllowSnooze,
            SnoozeHours = source.SnoozeHours, MaxSnoozes = source.MaxSnoozes, TriggerType = source.TriggerType,
            TriggerThreshold = source.TriggerThreshold, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            TargetGroups = source.TargetGroups.Select(g => new SurveyTargetGroup { GroupId = g.GroupId }).ToList(),
            Questions = source.Questions.OrderBy(q => q.Order).Select(q => new SurveyQuestion
            {
                Order = q.Order, Text = q.Text, Description = q.Description, Type = q.Type, IsRequired = q.IsRequired,
                AllowOther = q.AllowOther, MaxLength = q.MaxLength, ScaleMin = q.ScaleMin, ScaleMax = q.ScaleMax,
                ScaleMinLabel = q.ScaleMinLabel, ScaleMaxLabel = q.ScaleMaxLabel,
                Options = q.Options.OrderBy(o => o.Order).Select(o => new SurveyOption { Order = o.Order, Text = o.Text }).ToList(),
            }).ToList(),
        };
        db.Surveys.Add(copy);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(adminId, copy.Id, ct);
    }

    public async Task DeleteAsync(Guid adminId, int id, CancellationToken ct = default)
    {
        var survey = await RequireAsync(adminId, id, ct);
        db.Surveys.Remove(survey);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SurveyResultsDto> GetResultsAsync(Guid adminId, int id, int? groupId = null, CancellationToken ct = default)
    {
        var survey = await RequireAsync(adminId, id, ct);
        var allResponses = await db.SurveyResponses.AsNoTracking().Where(r => r.SurveyId == id).Include(r => r.Answers).ToListAsync(ct);
        var audienceMembers = await AudienceWithGroupsAsync(survey, ct);
        var groupNames = await db.Groups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var groups = audienceMembers.Where(m => m.GroupId.HasValue).GroupBy(m => m.GroupId!.Value)
            .Select(g => new SurveyGroupCountDto(g.Key, groupNames.GetValueOrDefault(g.Key, "—"),
                allResponses.Count(r => r.GroupId == g.Key), g.Count()))
            .OrderBy(g => g.Name).ToList();
        var responses = groupId is int only ? allResponses.Where(r => r.GroupId == only).ToList() : allResponses;
        var audience = groupId is int scope ? audienceMembers.Count(m => m.GroupId == scope) : audienceMembers.Count;
        var hidden = survey.IsAnonymous && !SurveyRules.CanShowAnonymousSlice(responses.Count, allResponses.Count);
        var answers = responses.SelectMany(r => r.Answers.Select(a => (r.Id, Answer: a))).ToList();
        var questions = hidden ? [] : survey.Questions.OrderBy(q => q.Order).Select(q =>
        {
            var mine = answers.Where(a => a.Answer.QuestionId == q.Id).ToList();
            var respondents = mine.Select(a => a.Id).Distinct().Count();
            var options = new List<SurveyOptionResultDto>();
            IReadOnlyList<string> texts = [];
            decimal? average = null, median = null;
            if (SurveyRules.IsChoice(q.Type))
            {
                options = q.Options.OrderBy(o => o.Order)
                    .Select(o => new SurveyOptionResultDto(o.Id, o.Text, mine.Count(a => a.Answer.OptionId == o.Id))).ToList();
                var other = mine.Where(a => a.Answer.OptionId == null && a.Answer.Text != null).ToList();
                if (q.AllowOther) options.Add(new(null, "Другое / Other", other.Count));
                texts = Shuffle(other.Select(a => a.Answer.Text!), survey.IsAnonymous);
            }
            else if (q.Type is SurveyQuestionType.ShortText or SurveyQuestionType.LongText)
                texts = Shuffle(mine.Select(a => a.Answer.Text!).Where(t => t is not null), survey.IsAnonymous);
            else
            {
                var numbers = mine.Where(a => a.Answer.Number.HasValue).Select(a => a.Answer.Number!.Value).OrderBy(n => n).ToList();
                if (numbers.Count > 0)
                {
                    average = Math.Round(numbers.Average(), 2);
                    median = numbers.Count % 2 == 1 ? numbers[numbers.Count / 2] : (numbers[numbers.Count / 2 - 1] + numbers[numbers.Count / 2]) / 2;
                }
                if (q.Type == SurveyQuestionType.Scale && q.ScaleMin is int min && q.ScaleMax is int max)
                    options = Enumerable.Range(min, max - min + 1)
                        .Select(v => new SurveyOptionResultDto(null, v.ToString(), numbers.Count(n => n == v))).ToList();
            }
            return new SurveyQuestionResultDto(q.Id, q.Text, q.Type, respondents, options, texts, average, median);
        }).ToList();
        return new(survey.Id, survey.Title, survey.IsAnonymous, responses.Count, audience, hidden, SurveyRules.AnonymityThreshold, questions,
            groups, groupId);
    }

    public async Task<IReadOnlyList<SurveyIndividualResponseDto>> GetResponsesAsync(Guid adminId, int id, int? groupId = null, CancellationToken ct = default)
    {
        var survey = await RequireAsync(adminId, id, ct);
        var responses = await db.SurveyResponses.AsNoTracking().Where(r => r.SurveyId == id && (groupId == null || r.GroupId == groupId)).Include(r => r.Answers)
            .OrderBy(r => r.SubmittedAtUtc).ThenBy(r => r.Id).ToListAsync(ct);
        var total = groupId is null ? responses.Count : await db.SurveyResponses.CountAsync(r => r.SurveyId == id, ct);
        if (survey.IsAnonymous && !SurveyRules.CanShowAnonymousSlice(responses.Count, total))
            throw new ConflictException("Individual answers of an anonymous survey appear after enough responses.", "survey.anonymity_threshold");
        var accounts = survey.IsAnonymous ? new Dictionary<Guid, ProfileAccountInfo>()
            : await identity.GetProfileAccountsAsync(responses.Where(r => r.RespondentId.HasValue).Select(r => r.RespondentId!.Value), ct);
        var questions = survey.Questions.ToDictionary(q => q.Id);
        var options = survey.Questions.SelectMany(q => q.Options).ToDictionary(o => o.Id, o => o.Text);
        var rows = responses.Select(r => new SurveyIndividualResponseDto(r.Id,
            survey.IsAnonymous ? null : r.RespondentId,
            survey.IsAnonymous ? "—" : r.RespondentId is Guid user && accounts.TryGetValue(user, out var a) ? a.FullName : "—",
            survey.IsAnonymous ? null : r.RespondentId is Guid u && accounts.TryGetValue(u, out var b) ? b.GroupName : null,
            r.SubmittedAtUtc,
            r.Answers.GroupBy(x => x.QuestionId).Where(g => questions.ContainsKey(g.Key))
                .OrderBy(g => questions[g.Key].Order)
                .Select(g => new SurveyAnswerViewDto(g.Key, string.Join("; ", g.Select(x =>
                    x.OptionId is int o ? options.GetValueOrDefault(o, "?") : x.Text ?? x.Number?.ToString("0.####") ?? ""))))
                .ToList(),
            survey.IsAnonymous ? null : r.RespondentId is Guid l && accounts.TryGetValue(l, out var c) ? c.LastName : null,
            survey.IsAnonymous ? null : r.RespondentId is Guid f && accounts.TryGetValue(f, out var d) ? d.FirstName : null)).ToList();
        return survey.IsAnonymous ? rows.OrderBy(_ => Random.Shared.Next()).ToList() : rows;
    }

    /// <summary>Студенты, которым опрос адресован: целевые группы или группы предмета, иначе все активные.</summary>
    internal async Task<List<Guid>> AudienceAsync(Survey survey, CancellationToken ct) =>
        (await AudienceWithGroupsAsync(survey, ct)).Select(m => m.Id).ToList();

    internal async Task<List<(Guid Id, int? GroupId)>> AudienceWithGroupsAsync(Survey survey, CancellationToken ct)
    {
        var students = db.Students.AsNoTracking().Where(s => s.IsActive);
        var groups = survey.TargetGroups.Select(g => g.GroupId).ToList();
        if (groups.Count > 0) students = students.Where(s => s.GroupId != null && groups.Contains(s.GroupId.Value));
        if (survey.SubjectId is int subjectId)
            students = students.Where(s => s.GroupId != null && db.GroupSubjects.Any(gs => gs.GroupId == s.GroupId && gs.SubjectId == subjectId));
        return (await students.Select(s => new { s.Id, s.GroupId }).ToListAsync(ct)).Select(s => (s.Id, s.GroupId)).ToList();
    }

    private async Task<Survey> RequireAsync(Guid adminId, int id, CancellationToken ct)
    {
        var survey = await db.Surveys.AsSplitQuery().Include(s => s.Subject).Include(s => s.TargetGroups)
            .Include(s => s.Questions).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Survey not found.", "survey.not_found");
        if (survey.SubjectId is int subjectId && await adminAccess.IsSubjectRestrictedAsync(adminId, subjectId, ct))
            throw new NotFoundException("Survey not found.", "survey.not_found");
        return survey;
    }

    private async Task EnsureScopeAsync(Guid adminId, SaveSurveyRequest request, CancellationToken ct)
    {
        if (request.SubjectId is int subjectId)
        {
            if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, ct)) throw new NotFoundException("Subject not found.", "subject.not_found");
            await adminAccess.EnsureSubjectAllowedAsync(adminId, subjectId, ct);
        }
        var groups = request.TargetGroupIds ?? [];
        if (groups.Count == 0) return;
        if (await db.Groups.CountAsync(g => groups.Contains(g.Id), ct) != groups.Count)
            throw new NotFoundException("One or more groups no longer exist.", "group.not_found");
        await adminAccess.EnsureGroupsAllowedAsync(adminId, groups, ct);
        if (request.SubjectId is int sid &&
            await db.GroupSubjects.CountAsync(gs => gs.SubjectId == sid && groups.Contains(gs.GroupId), ct) != groups.Count)
            throw new ConflictException("Every target group must study the survey's subject.", "survey.group_not_in_subject");
    }

    private static void Apply(Survey survey, SaveSurveyRequest request)
    {
        survey.Title = request.Title!.Trim();
        survey.Description = request.Description?.Trim() ?? "";
        survey.SubjectId = request.SubjectId;
        survey.IsAnonymous = request.IsAnonymous;
        survey.AllowSnooze = request.AllowSnooze;
        survey.SnoozeHours = request.SnoozeHours;
        survey.MaxSnoozes = request.AllowSnooze ? request.MaxSnoozes : null;
        survey.OpensAtUtc = Utc(request.OpensAtUtc);
        survey.ClosesAtUtc = Utc(request.ClosesAtUtc);
        survey.TriggerType = request.TriggerType;
        survey.TriggerThreshold = request.TriggerType == SurveyTriggerType.Immediate ? 0 : request.TriggerThreshold;
        survey.UpdatedAtUtc = DateTime.UtcNow;
        survey.TargetGroups = (request.TargetGroupIds ?? []).Select(g => new SurveyTargetGroup { GroupId = g }).ToList();
        survey.Questions = (request.Questions ?? []).Select((q, index) => new SurveyQuestion
        {
            Order = index + 1, Text = q.Text!.Trim(), Description = string.IsNullOrWhiteSpace(q.Description) ? null : q.Description.Trim(),
            Type = q.Type, IsRequired = q.IsRequired, AllowOther = SurveyRules.IsChoice(q.Type) && q.AllowOther,
            MaxLength = q.Type is SurveyQuestionType.ShortText or SurveyQuestionType.LongText ? q.MaxLength : null,
            ScaleMin = q.Type is SurveyQuestionType.Scale or SurveyQuestionType.Number ? q.ScaleMin : null,
            ScaleMax = q.Type is SurveyQuestionType.Scale or SurveyQuestionType.Number ? q.ScaleMax : null,
            ScaleMinLabel = q.Type == SurveyQuestionType.Scale ? Clean(q.ScaleMinLabel) : null,
            ScaleMaxLabel = q.Type == SurveyQuestionType.Scale ? Clean(q.ScaleMaxLabel) : null,
            Options = SurveyRules.IsChoice(q.Type)
                ? (q.Options ?? []).Select((text, i) => new SurveyOption { Order = i + 1, Text = text.Trim() }).ToList()
                : [],
        }).ToList();
    }

    private async Task<SurveyAdminDto> ToDtoAsync(Survey s, CancellationToken ct)
    {
        var responses = await db.SurveyResponses.CountAsync(r => r.SurveyId == s.Id, ct);
        return new(s.Id, s.Title, s.Description, s.SubjectId, s.Subject?.Name, s.TargetGroups.Select(g => g.GroupId).Order().ToList(),
            s.Status, s.IsAnonymous, s.AllowSnooze, s.SnoozeHours, s.MaxSnoozes, s.OpensAtUtc, s.ClosesAtUtc, s.TriggerType,
            s.TriggerThreshold, s.CreatedByName, s.CreatedAtUtc, s.PublishedAtUtc, responses, SurveyMapping.Questions(s));
    }

    private static List<string> Shuffle(IEnumerable<string> values, bool anonymous) =>
        anonymous ? values.OrderBy(_ => Random.Shared.Next()).ToList() : values.ToList();

    /// <summary>Клиент шлёт UTC; время без зоны тоже считаем UTC, а не локальным временем сервера.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

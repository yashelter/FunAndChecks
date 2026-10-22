using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Surveys;

public interface ISurveyService
{
    Task<IReadOnlyList<PendingSurveyDto>> GetPendingAsync(Guid studentId, CancellationToken ct = default);
    Task<SurveyFormDto> GetFormAsync(Guid studentId, int surveyId, CancellationToken ct = default);
    Task SubmitAsync(Guid studentId, int surveyId, SubmitSurveyRequest request, CancellationToken ct = default);
    Task SnoozeAsync(Guid studentId, int surveyId, CancellationToken ct = default);
    Task<IReadOnlyList<MySurveyDto>> GetCompletedAsync(Guid studentId, CancellationToken ct = default);

    /// <summary>После сдачи: если у предмета есть опрос с триггером, клиент студента перечитает список.</summary>
    Task NotifySubmissionAsync(Guid studentId, int subjectId, CancellationToken ct = default);
}

/// <summary>
/// Доступность опроса считается на лету по текущим сдачам, а не по событиям: опрос, созданный
/// после сдач, тоже сработает, и сбой уведомления ничего не теряет.
/// </summary>
public class SurveyService(IApplicationDbContext db, IUserNotifier userNotifier, ILogger<SurveyService> logger) : ISurveyService
{
    public async Task<IReadOnlyList<PendingSurveyDto>> GetPendingAsync(Guid studentId, CancellationToken ct = default)
    {
        var surveys = await AvailableAsync(studentId, respectSnooze: true, ct);
        var participation = await ParticipationsAsync(studentId, surveys.Select(s => s.Id), ct);
        return surveys.Select(s => new PendingSurveyDto(s.Id, s.Title, s.Subject?.Name, s.IsAnonymous,
            CanSnooze(s, participation.GetValueOrDefault(s.Id)), s.SnoozeHours, s.Questions.Count, s.ClosesAtUtc)).ToList();
    }

    public async Task<SurveyFormDto> GetFormAsync(Guid studentId, int surveyId, CancellationToken ct = default)
    {
        var survey = await RequireAvailableAsync(studentId, surveyId, ct);
        var participation = (await ParticipationsAsync(studentId, [surveyId], ct)).GetValueOrDefault(surveyId);
        return new(survey.Id, survey.Title, survey.Description, survey.Subject?.Name, survey.IsAnonymous,
            CanSnooze(survey, participation), survey.SnoozeHours, survey.ClosesAtUtc, SurveyMapping.Questions(survey));
    }

    public async Task SubmitAsync(Guid studentId, int surveyId, SubmitSurveyRequest request, CancellationToken ct = default)
    {
        var survey = await RequireAvailableAsync(studentId, surveyId, ct);
        var answers = BuildAnswers(survey, request.Answers ?? []);
        var groupId = await db.Students.Where(s => s.Id == studentId).Select(s => s.GroupId).SingleOrDefaultAsync(ct);
        await db.ExecuteSerializableAsync(async token =>
        {
            var participation = await db.SurveyParticipations
                .SingleOrDefaultAsync(p => p.SurveyId == surveyId && p.UserId == studentId, token);
            if (participation?.Status == SurveyParticipationStatus.Completed)
                throw new ConflictException("You have already answered this survey.", "survey.already_completed");
            var now = DateTime.UtcNow;
            // Для анонимного опроса точное время выдало бы респондента рядом со списком прошедших.
            var stamp = survey.IsAnonymous ? now.Date : now;
            db.SurveyResponses.Add(new SurveyResponse
            {
                SurveyId = surveyId, RespondentId = survey.IsAnonymous ? null : studentId, GroupId = groupId, SubmittedAtUtc = stamp,
                Answers = answers.Select(a => new SurveyAnswer
                    { QuestionId = a.QuestionId, OptionId = a.OptionId, Text = a.Text, Number = a.Number }).ToList(),
            });
            if (participation is null)
            {
                participation = new SurveyParticipation { SurveyId = surveyId, UserId = studentId };
                db.SurveyParticipations.Add(participation);
            }
            participation.Status = SurveyParticipationStatus.Completed;
            participation.CompletedAtUtc = stamp;
            participation.SnoozedUntilUtc = null;
            await db.SaveChangesAsync(token);
        }, ct);
    }

    public async Task SnoozeAsync(Guid studentId, int surveyId, CancellationToken ct = default)
    {
        var survey = await RequireAvailableAsync(studentId, surveyId, ct);
        if (!survey.AllowSnooze) throw new ConflictException("This survey cannot be postponed.", "survey.snooze_not_allowed");
        await db.ExecuteSerializableAsync(async token =>
        {
            var participation = await db.SurveyParticipations
                .SingleOrDefaultAsync(p => p.SurveyId == surveyId && p.UserId == studentId, token);
            if (participation?.Status == SurveyParticipationStatus.Completed)
                throw new ConflictException("You have already answered this survey.", "survey.already_completed");
            if (!CanSnooze(survey, participation))
                throw new ConflictException("You cannot postpone this survey any more.", "survey.snooze_limit");
            if (participation is null)
            {
                participation = new SurveyParticipation { SurveyId = surveyId, UserId = studentId };
                db.SurveyParticipations.Add(participation);
            }
            participation.Status = SurveyParticipationStatus.Snoozed;
            participation.SnoozeCount++;
            participation.SnoozedUntilUtc = DateTime.UtcNow.AddHours(survey.SnoozeHours);
            await db.SaveChangesAsync(token);
        }, ct);
    }

    public async Task<IReadOnlyList<MySurveyDto>> GetCompletedAsync(Guid studentId, CancellationToken ct = default) =>
        await (from p in db.SurveyParticipations.AsNoTracking()
            join s in db.Surveys.AsNoTracking() on p.SurveyId equals s.Id
            where p.UserId == studentId && p.Status == SurveyParticipationStatus.Completed
            orderby p.CompletedAtUtc descending
            select new MySurveyDto(s.Id, s.Title, s.Subject == null ? null : s.Subject.Name, s.IsAnonymous, p.CompletedAtUtc))
            .ToListAsync(ct);

    public async Task NotifySubmissionAsync(Guid studentId, int subjectId, CancellationToken ct = default)
    {
        try
        {
            var hasTrigger = await db.Surveys.AnyAsync(s => s.Status == SurveyStatus.Published && s.SubjectId == subjectId &&
                s.TriggerType != SurveyTriggerType.Immediate, ct);
            if (hasTrigger) await userNotifier.SurveysChangedAsync(studentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Survey availability hint failed for student {StudentId}.", studentId);
        }
    }

    /// <summary>Опросы, которые студент может пройти прямо сейчас.</summary>
    internal async Task<List<Survey>> AvailableAsync(Guid studentId, bool respectSnooze, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().Where(s => s.Id == studentId && s.IsActive)
            .Select(s => new { s.GroupId }).SingleOrDefaultAsync(ct);
        if (student is null) return [];
        var groupId = student.GroupId;
        var subjectIds = groupId is int g
            ? await db.GroupSubjects.Where(gs => gs.GroupId == g).Select(gs => gs.SubjectId).ToListAsync(ct)
            : [];
        var now = DateTime.UtcNow;
        var candidates = await db.Surveys.AsNoTracking().AsSplitQuery()
            .Include(s => s.Subject).Include(s => s.Questions).ThenInclude(q => q.Options).Include(s => s.TargetGroups)
            .Where(s => s.Status == SurveyStatus.Published && (s.OpensAtUtc == null || s.OpensAtUtc <= now) &&
                (s.ClosesAtUtc == null || s.ClosesAtUtc > now))
            .Where(s => s.SubjectId == null || subjectIds.Contains(s.SubjectId.Value))
            .Where(s => !s.TargetGroups.Any() || (groupId != null && s.TargetGroups.Any(t => t.GroupId == groupId)))
            .Where(s => !db.SurveyParticipations.Any(p => p.SurveyId == s.Id && p.UserId == studentId &&
                (p.Status == SurveyParticipationStatus.Completed || (respectSnooze && p.SnoozedUntilUtc > now))))
            .OrderBy(s => s.PublishedAtUtc).ThenBy(s => s.Id)
            .ToListAsync(ct);
        var triggered = candidates.Where(s => s.TriggerType != SurveyTriggerType.Immediate).Select(s => s.SubjectId!.Value)
            .Distinct().ToList();
        var stats = await SubmissionStats.ForStudentAsync(db, studentId, triggered, ct);
        return candidates.Where(s => s.TriggerType switch
        {
            SurveyTriggerType.AcceptedInSubject => stats.GetValueOrDefault(s.SubjectId!.Value)?.AcceptedTasks >= s.TriggerThreshold,
            SurveyTriggerType.AttemptsInSubject => stats.GetValueOrDefault(s.SubjectId!.Value)?.Attempts >= s.TriggerThreshold,
            _ => true,
        }).ToList();
    }

    private async Task<Survey> RequireAvailableAsync(Guid studentId, int surveyId, CancellationToken ct)
    {
        var survey = (await AvailableAsync(studentId, respectSnooze: false, ct)).SingleOrDefault(s => s.Id == surveyId);
        if (survey is not null) return survey;
        if (await db.SurveyParticipations.AnyAsync(p => p.SurveyId == surveyId && p.UserId == studentId &&
                p.Status == SurveyParticipationStatus.Completed, ct))
            throw new ConflictException("You have already answered this survey.", "survey.already_completed");
        throw new NotFoundException("Survey not found.", "survey.not_found");
    }

    private Task<Dictionary<int, SurveyParticipation>> ParticipationsAsync(Guid studentId, IEnumerable<int> surveyIds, CancellationToken ct)
    {
        var ids = surveyIds.ToList();
        return db.SurveyParticipations.AsNoTracking().Where(p => p.UserId == studentId && ids.Contains(p.SurveyId))
            .ToDictionaryAsync(p => p.SurveyId, ct);
    }

    private static bool CanSnooze(Survey survey, SurveyParticipation? participation) =>
        survey.AllowSnooze && (survey.MaxSnoozes is not int max || (participation?.SnoozeCount ?? 0) < max);

    private sealed record AnswerRow(int QuestionId, int? OptionId, string? Text, decimal? Number);

    /// <summary>Проверяет ответы по типам вопросов и раскладывает их в строки.</summary>
    private static List<AnswerRow> BuildAnswers(Survey survey, IReadOnlyList<SubmitSurveyAnswer> answers)
    {
        var questions = survey.Questions.ToDictionary(q => q.Id);
        if (answers.Any(a => !questions.ContainsKey(a.QuestionId)) || answers.Select(a => a.QuestionId).Distinct().Count() != answers.Count)
            throw new ValidationException("The answers do not match the survey questions.");
        var byQuestion = answers.ToDictionary(a => a.QuestionId);
        var rows = new List<AnswerRow>();
        foreach (var question in survey.Questions.OrderBy(q => q.Order))
        {
            var answer = byQuestion.GetValueOrDefault(question.Id);
            var produced = Rows(question, answer);
            if (produced.Count == 0 && question.IsRequired)
                throw new ValidationException($"Answer the required question: {question.Text}");
            rows.AddRange(produced);
        }
        return rows;
    }

    private static List<AnswerRow> Rows(SurveyQuestion question, SubmitSurveyAnswer? answer)
    {
        if (answer is null) return [];
        var other = string.IsNullOrWhiteSpace(answer.OtherText) ? null : answer.OtherText.Trim();
        var text = string.IsNullOrWhiteSpace(answer.Text) ? null : answer.Text.Trim();
        switch (question.Type)
        {
            case SurveyQuestionType.SingleChoice or SurveyQuestionType.Dropdown or SurveyQuestionType.MultipleChoice:
            {
                var ids = answer.OptionIds?.Distinct().ToList() ?? [];
                if (ids.Any(id => question.Options.All(o => o.Id != id)))
                    throw new ValidationException($"Unknown option in: {question.Text}");
                if (other is not null && (!question.AllowOther || other.Length > SurveyRules.ShortTextLimit))
                    throw new ValidationException($"A custom answer is not allowed here: {question.Text}");
                if (question.Type != SurveyQuestionType.MultipleChoice && ids.Count + (other is null ? 0 : 1) > 1)
                    throw new ValidationException($"Choose one option: {question.Text}");
                var rows = ids.Select(id => new AnswerRow(question.Id, id, null, null)).ToList();
                if (other is not null) rows.Add(new(question.Id, null, other, null));
                return rows;
            }
            case SurveyQuestionType.ShortText or SurveyQuestionType.LongText:
            {
                if (text is null) return [];
                var limit = question.MaxLength ?? (question.Type == SurveyQuestionType.ShortText
                    ? SurveyRules.ShortTextLimit : SurveyRules.LongTextLimit);
                if (text.Length > limit) throw new ValidationException($"The answer is longer than {limit} characters: {question.Text}");
                return [new(question.Id, null, text, null)];
            }
            case SurveyQuestionType.Scale or SurveyQuestionType.Number:
            {
                if (answer.Number is not decimal number) return [];
                if (question.Type == SurveyQuestionType.Scale && number != decimal.Truncate(number))
                    throw new ValidationException($"Choose a point on the scale: {question.Text}");
                if ((question.ScaleMin is int min && number < min) || (question.ScaleMax is int max && number > max))
                    throw new ValidationException($"The value is out of range: {question.Text}");
                return [new(question.Id, null, null, number)];
            }
            default:
                return [];
        }
    }
}

internal static class SurveyMapping
{
    public static List<SurveyQuestionDto> Questions(Survey survey) => survey.Questions.OrderBy(q => q.Order)
        .Select(q => new SurveyQuestionDto(q.Id, q.Order, q.Text, q.Description, q.Type, q.IsRequired, q.AllowOther, q.MaxLength,
            q.ScaleMin, q.ScaleMax, q.ScaleMinLabel, q.ScaleMaxLabel,
            q.Options.OrderBy(o => o.Order).Select(o => new SurveyOptionDto(o.Id, o.Text)).ToList()))
        .ToList();
}

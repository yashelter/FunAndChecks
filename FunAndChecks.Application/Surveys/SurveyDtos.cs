using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Surveys;

public record SurveyOptionDto(int Id, string Text);

public record SurveyQuestionDto(int Id, int Order, string Text, string? Description, SurveyQuestionType Type, bool IsRequired,
    bool AllowOther, int? MaxLength, int? ScaleMin, int? ScaleMax, string? ScaleMinLabel, string? ScaleMaxLabel,
    IReadOnlyList<SurveyOptionDto> Options);

public record SurveyQuestionInput(string? Text, string? Description, SurveyQuestionType Type, bool IsRequired, bool AllowOther,
    int? MaxLength, int? ScaleMin, int? ScaleMax, string? ScaleMinLabel, string? ScaleMaxLabel, IReadOnlyList<string>? Options);

/// <summary>Полное содержимое черновика. После публикации меняются только детали (<see cref="UpdateSurveyDetailsRequest"/>).</summary>
public record SaveSurveyRequest(string? Title, string? Description, int? SubjectId, IReadOnlyList<int>? TargetGroupIds,
    bool IsAnonymous, bool AllowSnooze, int SnoozeHours, int? MaxSnoozes, DateTime? OpensAtUtc, DateTime? ClosesAtUtc,
    SurveyTriggerType TriggerType, int TriggerThreshold, IReadOnlyList<SurveyQuestionInput>? Questions);

public record UpdateSurveyDetailsRequest(string? Title, string? Description, DateTime? ClosesAtUtc);

public record SurveySummaryDto(int Id, string Title, int? SubjectId, string? SubjectName, SurveyStatus Status, bool IsAnonymous,
    SurveyTriggerType TriggerType, int TriggerThreshold, int QuestionCount, int ResponseCount, string CreatedByName,
    DateTime CreatedAtUtc, DateTime? PublishedAtUtc, DateTime? ClosesAtUtc);

public record SurveyPageDto(IReadOnlyList<SurveySummaryDto> Items, int TotalCount, int Page, int PageSize);

public record SurveyAdminDto(int Id, string Title, string Description, int? SubjectId, string? SubjectName,
    IReadOnlyList<int> TargetGroupIds, SurveyStatus Status, bool IsAnonymous, bool AllowSnooze, int SnoozeHours, int? MaxSnoozes,
    DateTime? OpensAtUtc, DateTime? ClosesAtUtc, SurveyTriggerType TriggerType, int TriggerThreshold, string CreatedByName,
    DateTime CreatedAtUtc, DateTime? PublishedAtUtc, int ResponseCount, IReadOnlyList<SurveyQuestionDto> Questions);

public record SurveyOptionResultDto(int? OptionId, string Text, int Count);

public record SurveyQuestionResultDto(int QuestionId, string Text, SurveyQuestionType Type, int AnswerCount,
    IReadOnlyList<SurveyOptionResultDto> Options, IReadOnlyList<string> TextAnswers, decimal? Average, decimal? Median);

/// <summary>
/// Итоги. У анонимного опроса с малым числом ответов детали скрыты (<see cref="HiddenForAnonymity"/>),
/// чтобы ответ нельзя было угадать по составу группы.
/// </summary>
public record SurveyResultsDto(int SurveyId, string Title, bool IsAnonymous, int ResponseCount, int AudienceCount,
    bool HiddenForAnonymity, int AnonymityThreshold, IReadOnlyList<SurveyQuestionResultDto> Questions,
    IReadOnlyList<SurveyGroupCountDto>? Groups = null, int? GroupId = null);

/// <summary>Охват опроса в группе: сколько ответили из скольких.</summary>
public record SurveyGroupCountDto(int GroupId, string Name, int ResponseCount, int AudienceCount);

public record SurveyAnswerViewDto(int QuestionId, string Value);

public record SurveyIndividualResponseDto(int ResponseId, Guid? RespondentId, string RespondentName, string? GroupName,
    DateTime SubmittedAtUtc, IReadOnlyList<SurveyAnswerViewDto> Answers, string? LastName = null, string? FirstName = null);

public record PendingSurveyDto(int Id, string Title, string? SubjectName, bool IsAnonymous, bool CanSnooze, int SnoozeHours,
    int QuestionCount, DateTime? ClosesAtUtc);

public record SurveyFormDto(int Id, string Title, string Description, string? SubjectName, bool IsAnonymous, bool CanSnooze,
    int SnoozeHours, DateTime? ClosesAtUtc, IReadOnlyList<SurveyQuestionDto> Questions);

public record SubmitSurveyAnswer(int QuestionId, IReadOnlyList<int>? OptionIds, string? OtherText, string? Text, decimal? Number);

public record SubmitSurveyRequest(IReadOnlyList<SubmitSurveyAnswer>? Answers);

public record MySurveyDto(int Id, string Title, string? SubjectName, bool IsAnonymous, DateTime? CompletedAtUtc);

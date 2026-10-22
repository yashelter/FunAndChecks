namespace FunAndChecks.Domain.Enums;

public enum SurveyStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2,
}

/// <summary>Когда опрос становится доступен студенту.</summary>
public enum SurveyTriggerType
{
    /// <summary>Сразу после публикации всем целевым студентам.</summary>
    Immediate = 0,
    /// <summary>После N задач предмета, у которых последняя попытка принята.</summary>
    AcceptedInSubject = 1,
    /// <summary>После N любых попыток сдачи по задачам предмета (принятых и отклонённых).</summary>
    AttemptsInSubject = 2,
}

public enum SurveyQuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    Dropdown = 2,
    ShortText = 3,
    LongText = 4,
    Scale = 5,
    Number = 6,
}

public enum SurveyParticipationStatus
{
    Snoozed = 0,
    Completed = 1,
}

namespace FunAndChecks.Domain.Enums;

public enum SubmissionStatus
{
    /// <summary>Не сдано (по умолчанию).</summary>
    NotSubmitted = 0,

    /// <summary>Принято.</summary>
    Accepted = 1,

    /// <summary>Не принято.</summary>
    Rejected = 2,
}

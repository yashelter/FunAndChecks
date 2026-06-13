namespace Frontend.Shared.Models;

/// <summary>Статус сдачи задания. Значения совпадают с бэкендом.</summary>
public enum SubmissionStatus
{
    NotSubmitted = 0,
    Accepted = 1,
    Rejected = 2,
}

/// <summary>Статус записи в очереди. Порядок значений влияет на сортировку.</summary>
public enum QueueEntryStatus
{
    Checking = 0,
    Waiting = 1,
    Skipped = 2,
    Finished = 3,
}

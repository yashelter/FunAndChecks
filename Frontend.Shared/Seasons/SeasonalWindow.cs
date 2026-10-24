namespace Frontend.Shared.Seasons;

/// <summary>
/// Ежегодное окно по дате: с <c>Start</c> по <c>End</c> включительно, без года. Может переходить через Новый год
/// (например, с 20 декабря по 10 января).
/// </summary>
public readonly record struct SeasonalWindow(int StartMonth, int StartDay, int EndMonth, int EndDay)
{
    public bool Contains(DateOnly date)
    {
        var day = Key(date.Month, date.Day);
        var start = Key(StartMonth, StartDay);
        var end = Key(EndMonth, EndDay);
        return start <= end ? day >= start && day <= end : day >= start || day <= end;
    }

    private static int Key(int month, int day) => month * 100 + day;

    public override string ToString() => $"{StartDay:00}.{StartMonth:00}–{EndDay:00}.{EndMonth:00}";
}

using Frontend.Shared.Models;

namespace Frontend.Shared.UI;

public static class AttendanceStyles
{
    public static string Text(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "Присутствовал", AttendanceStatus.Absent => "Отсутствовал", _ => "Не отмечен",
    };
    public static string Symbol(AttendanceStatus? status) => status switch
    {
        AttendanceStatus.Present => "✓", AttendanceStatus.Absent => "✕", AttendanceStatus.Unmarked => "—", _ => "·",
    };
    public static string Css(AttendanceStatus? status) => status switch
    {
        AttendanceStatus.Present => "attendance-mark present", AttendanceStatus.Absent => "attendance-mark absent",
        AttendanceStatus.Unmarked => "attendance-mark unmarked", _ => "attendance-mark outside",
    };
    public static DateTime Moscow(DateTime utc) => utc.AddHours(3);
    public static DateTime ToUtc(DateTime date, TimeSpan time) => DateTime.SpecifyKind(date.Date.Add(time).AddHours(-3), DateTimeKind.Utc);
}

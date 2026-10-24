using System.Globalization;

namespace Frontend.Shared.Community;

public static class CommunityText
{
    public static bool Ru => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    public static string T(string ru, string en) => Ru ? ru : en;

    /// <summary>
    /// Число со словом в нужной форме: «1 вопрос», «3 вопроса», «11 вопросов» / «1 question», «3 questions».
    /// Русские формы: one — 1, 21, 31…; few — 2–4, 22–24…; many — остальное (включая 11–14).
    /// </summary>
    public static string Plural(int n, string one, string few, string many, string enOne, string enOther)
    {
        if (!Ru) return $"{n} {(n == 1 ? enOne : enOther)}";
        var (mod10, mod100) = (Math.Abs(n) % 10, Math.Abs(n) % 100);
        var word = mod10 == 1 && mod100 != 11 ? one : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? few : many;
        return $"{n} {word}";
    }

    public static string Questions(int n) => Plural(n, "вопрос", "вопроса", "вопросов", "question", "questions");
    public static string Posts(int n) => Plural(n, "сообщение", "сообщения", "сообщений", "post", "posts");
    public static string Answers(int n) => Plural(n, "ответ", "ответа", "ответов", "answer", "answers");
    public static string Students(int n) => Plural(n, "студент", "студента", "студентов", "student", "students");
    public static string Days(int n) => Plural(n, "день", "дня", "дней", "day", "days");
    public static string Notifications(int n) => Plural(n, "новое уведомление", "новых уведомления", "новых уведомлений", "new notification", "new notifications");
    public static string NewPosts(int n) => Plural(n, "новое", "новых", "новых", "new", "new");

    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return T("только что", "just now");
        if (span.TotalHours < 1) return T($"{(int)span.TotalMinutes} мин назад", $"{(int)span.TotalMinutes} min ago");
        if (span.TotalDays < 1) return T($"{(int)span.TotalHours} ч назад", $"{(int)span.TotalHours} h ago");
        if (span.TotalDays < 7) return T($"{(int)span.TotalDays} дн назад", $"{(int)span.TotalDays} d ago");
        return utc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => T($"{bytes} Б", $"{bytes} B"),
        < 1024 * 1024 => T($"{bytes / 1024d:0.#} КБ", $"{bytes / 1024d:0.#} KB"),
        _ => T($"{bytes / 1024d / 1024d:0.#} МБ", $"{bytes / 1024d / 1024d:0.#} MB"),
    };

    public static string Reaction(ForumReactionKind kind) => kind switch
    {
        ForumReactionKind.Like => "👍",
        ForumReactionKind.Heart => "❤️",
        ForumReactionKind.Laugh => "😂",
        ForumReactionKind.Party => "🎉",
        ForumReactionKind.Thinking => "🤔",
        _ => "👀",
    };

    public static string Notification(NotificationDto n) => n.Type switch
    {
        NotificationType.AchievementGranted => T($"Вам выдано достижение «{n.Subject}»", $"You received the achievement “{n.Subject}”"),
        NotificationType.ForumReply => T($"Новый ответ в теме «{n.Subject}»", $"New reply in “{n.Subject}”"),
        NotificationType.ForumMention => T($"Вас упомянули в теме «{n.Subject}»", $"You were mentioned in “{n.Subject}”"),
        NotificationType.ForumAnnouncement => T($"Новое объявление: «{n.Subject}»", $"New announcement: “{n.Subject}”"),
        NotificationType.SurveyAvailable => T($"Доступен опрос «{n.Subject}»", $"A new survey is available: “{n.Subject}”"),
        _ => n.Subject,
    };

    /// <summary>Код ошибки сервера для заблокированного ответа — в человеческий текст.</summary>
    public static string ReplyBlocked(string? code, DateTime? bannedUntil) => code switch
    {
        "forum.topic_closed" => T("Тема закрыта — новые сообщения не принимаются.", "This topic is closed to new messages."),
        "forum.announcement" => T("Это объявление — отвечать могут только преподаватели.", "This is an announcement; only teachers can reply."),
        "forum.banned" => T($"Вы не можете писать на форуме до {bannedUntil?.ToLocalTime():g}.", $"You cannot post until {bannedUntil?.ToLocalTime():g}."),
        _ => T("Ответить нельзя.", "You cannot reply."),
    };
}

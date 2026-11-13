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
        return $"{n} {PluralCategory(n) switch { "One" => one, "Few" => few, _ => many }}";
    }

    /// <summary>
    /// Форма числа для текущего языка: "One", "Few" или "Many" (в английском только One и Many).
    /// Для строк из AppStrings: ключ «X» + форма — <c>Loc[PluralKey("StudentQueues_Ahead", n), n]</c>.
    /// </summary>
    public static string PluralCategory(int n)
    {
        if (!Ru) return n == 1 ? "One" : "Many";
        var (mod10, mod100) = (Math.Abs(n) % 10, Math.Abs(n) % 100);
        return mod10 == 1 && mod100 != 11 ? "One" : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "Few" : "Many";
    }

    public static string PluralKey(string baseKey, int n) => baseKey + PluralCategory(n);

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

    /// <summary>Ошибка API форума по её коду — по-человечески; незнакомый код показывает текст сервера.</summary>
    public static string Error(Frontend.Shared.Api.ApiException ex) => ex.Code switch
    {
        "forum.direct_not_allowed" => T("Этому человеку нельзя написать первым: студенты пишут преподавателям своих предметов и одногруппникам.",
            "You cannot start a chat with this person: students write to their teachers and groupmates."),
        "forum.saved_admins_only" => T("«Избранное» есть только у преподавателей.", "Saved messages are for teachers."),
        "forum.reaction_disabled" => T("Эта реакция выключена.", "This reaction is turned off."),
        "forum.sticker_not_found" => T("Стикер больше недоступен.", "This sticker is no longer available."),
        "forum.post_deleted" => T("Сообщение уже удалено.", "The message was deleted."),
        "forum.topic_closed" => T("Чат закрыт для новых сообщений.", "This chat is closed to new messages."),
        "forum.announcement" => T("В объявлениях пишут только преподаватели.", "Only teachers write in announcements."),
        "forum.banned" => T("Сейчас вы не можете писать в чатах.", "You cannot write in chats right now."),
        "forum.topic_not_found" => T("Чат не найден или недоступен.", "The chat was not found or is not available."),
        "forum.super_admin_required" => T("Это может только супер-админ.", "Only a super admin can do this."),
        _ => ex.Message,
    };

    public static string Notification(NotificationDto n) => n.Type switch
    {
        NotificationType.AchievementGranted => T($"Вам выдано достижение «{n.Subject}»", $"You received the achievement “{n.Subject}”"),
        NotificationType.ForumReply => T($"Новый ответ в теме «{n.Subject}»", $"New reply in “{n.Subject}”"),
        NotificationType.ForumMention => T($"Вас упомянули в теме «{n.Subject}»", $"You were mentioned in “{n.Subject}”"),
        NotificationType.ForumAnnouncement => T($"Новое объявление: «{n.Subject}»", $"New announcement: “{n.Subject}”"),
        NotificationType.SurveyAvailable => T($"Доступен опрос «{n.Subject}»", $"A new survey is available: “{n.Subject}”"),
        NotificationType.VariantAssigned => T($"Преподаватель выдал вам вариант — {n.Subject}", $"A teacher assigned you a variant — {n.Subject}"),
        NotificationType.DirectMessage => T($"Новое личное сообщение от {n.Subject}", $"New direct message from {n.Subject}"),
        _ => n.Subject,
    };

    /// <summary>Код ошибки сервера для заблокированного ответа — в человеческий текст.</summary>
    public static string ReplyBlocked(string? code, DateTime? bannedUntil) => code switch
    {
        "forum.topic_closed" => T("Чат закрыт — новые сообщения не принимаются.", "This chat is closed to new messages."),
        "forum.announcement" => T("Это объявления — писать могут только преподаватели.", "These are announcements; only teachers can write."),
        "forum.banned" => T($"Вы не можете писать в чатах до {bannedUntil?.ToLocalTime():g}.", $"You cannot write until {bannedUntil?.ToLocalTime():g}."),
        _ => T("Ответить нельзя.", "You cannot reply."),
    };
}

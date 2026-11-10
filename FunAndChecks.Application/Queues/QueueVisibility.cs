using System.Linq.Expressions;
using FunAndChecks.Domain.Entities;

namespace FunAndChecks.Application.Queues;

/// <summary>Какие очереди вернуть: текущие, архив или все.</summary>
public enum QueueListScope
{
    Current,
    Archive,
    All,
}

/// <summary>
/// Одно правило «текущая или архивная очередь» для списков преподавателя, студента и автозаполнения.
/// Текущая — не архивирована вручную и началась не раньше, чем сутки назад. Архив — всё остальное:
/// вручную завершённые (<see cref="QueueEvent.ArchivedAt"/>) и вышедшие за суточный порог («прошедшие»).
/// Суточный порог определяет вкладку, ArchivedAt — ручное завершение и режим только для просмотра.
/// </summary>
public static class QueueVisibility
{
    /// <summary>Сколько событие остаётся текущим после своего начала.</summary>
    public static readonly TimeSpan CurrentWindow = TimeSpan.FromDays(1);

    public static Expression<Func<QueueEvent, bool>> Current(DateTime utcNow)
    {
        var threshold = utcNow - CurrentWindow;
        return qe => qe.ArchivedAt == null && qe.EventDateTime > threshold;
    }

    public static Expression<Func<QueueEvent, bool>> Archive(DateTime utcNow)
    {
        var threshold = utcNow - CurrentWindow;
        return qe => qe.ArchivedAt != null || qe.EventDateTime <= threshold;
    }

    public static IQueryable<QueueEvent> InScope(this IQueryable<QueueEvent> events, QueueListScope scope, DateTime utcNow) => scope switch
    {
        QueueListScope.Current => events.Where(Current(utcNow)),
        QueueListScope.Archive => events.Where(Archive(utcNow)),
        _ => events,
    };
}

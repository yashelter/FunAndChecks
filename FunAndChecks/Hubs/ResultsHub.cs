using FunAndChecks.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Hubs;

[Authorize]
public class ResultsHub(IApplicationDbContext db) : Hub
{
    /// <summary>
    /// Странице нужен один предмет; запас на несколько вкладок одного соединения не нужен — у каждой своё.
    /// Предел не даёт одному соединению создать тысячи групп.
    /// </summary>
    internal const int MaxSubscriptionsPerConnection = 8;

    private const string SubscriptionsKey = "results-subscriptions";

    public static string GroupName(int subjectId) => $"results-subject-{subjectId}";

    /// <summary>Подписка клиента на обновления результатов по предмету.</summary>
    public async Task SubscribeToSubjectResults(int subjectId)
    {
        var subscriptions = Subscriptions();
        if (subscriptions.Contains(subjectId)) return;
        if (subscriptions.Count >= MaxSubscriptionsPerConnection)
            throw new HubException("Too many result subscriptions on one connection.");
        // Группы для несуществующих предметов не создаём: подписка по произвольному ID только тратила бы память.
        if (!await db.Subjects.AnyAsync(s => s.Id == subjectId, Context.ConnectionAborted))
            throw new HubException("Subject not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(subjectId));
        subscriptions.Add(subjectId);
    }

    public async Task UnsubscribeFromSubjectResults(int subjectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(subjectId));
        Subscriptions().Remove(subjectId);
    }

    private HashSet<int> Subscriptions()
    {
        if (Context.Items.TryGetValue(SubscriptionsKey, out var value) && value is HashSet<int> set) return set;
        set = [];
        Context.Items[SubscriptionsKey] = set;
        return set;
    }
}

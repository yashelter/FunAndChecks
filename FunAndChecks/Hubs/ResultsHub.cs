using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FunAndChecks.Hubs;

[Authorize]
public class ResultsHub : Hub
{
    public static string GroupName(int subjectId) => $"results-subject-{subjectId}";

    /// <summary>Подписка клиента на обновления результатов по предмету.</summary>
    public async Task SubscribeToSubjectResults(int subjectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(subjectId));
    }

    public async Task UnsubscribeFromSubjectResults(int subjectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(subjectId));
    }
}

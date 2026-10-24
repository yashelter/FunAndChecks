using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace FunAndChecks.Hubs;

/// <summary>
/// Персональный канал: сервер пишет в Clients.User(id), подписки не нужны.
/// Идентификатор берётся из claim NameIdentifier (провайдер SignalR по умолчанию).
/// </summary>
[Authorize]
public class NotificationsHub : Hub;

using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;

namespace Frontend.Shared.Community;

/// <summary>
/// Одно персональное соединение на вкладку: уведомления и подсказки про опросы.
/// При смене аккаунта соединение пересоздаётся, чтобы события прошлого пользователя не долетали.
/// </summary>
public sealed class CommunityRealtime : IAsyncDisposable
{
    private readonly NavigationManager _nav;
    private readonly AuthService _auth;
    private readonly AuthenticationStateProvider _authentication;
    private HubConnection? _hub;
    private Task? _starting;

    public event Action<NotificationDto>? NotificationReceived;
    public event Action? SurveysChanged;

    /// <summary>После переподключения события могли потеряться — подписчикам стоит перечитать данные.</summary>
    public event Action? Reconnected;

    public CommunityRealtime(NavigationManager nav, AuthService auth, AuthenticationStateProvider authentication)
    {
        _nav = nav;
        _auth = auth;
        _authentication = authentication;
        _authentication.AuthenticationStateChanged += AccountChanged;
    }

    public Task EnsureStartedAsync() => _starting ??= StartAsync();

    private async Task StartAsync()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(_nav.ToAbsoluteUri("/apiHub/notificationsHub"), options => options.AccessTokenProvider = () => _auth.GetTokenAsync())
            .WithAutomaticReconnect()
            .Build();
        hub.On<NotificationDto>("NotificationCreated", n => NotificationReceived?.Invoke(n));
        hub.On("SurveysChanged", () => SurveysChanged?.Invoke());
        hub.Reconnected += _ =>
        {
            Reconnected?.Invoke();
            SurveysChanged?.Invoke();
            return Task.CompletedTask;
        };
        _hub = hub;
        try { await hub.StartAsync(); }
        catch (Exception)
        {
            // Без живого канала всё работает по запросу; попробуем при следующем открытии страницы.
            _hub = null;
            _starting = null;
            await hub.DisposeAsync();
        }
    }

    private async void AccountChanged(Task<AuthenticationState> state)
    {
        var hub = _hub;
        _hub = null;
        _starting = null;
        if (hub is not null)
        {
            try { await hub.DisposeAsync(); }
            catch (Exception) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _authentication.AuthenticationStateChanged -= AccountChanged;
        if (_hub is not null) await _hub.DisposeAsync();
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Shared.Profiles;

/// <summary>Shares visible media only. Two transfers at a time; a blob lives while a view holds it.</summary>
public sealed class ProfileMediaCache : IAsyncDisposable
{
    private readonly ProfileApi _api;
    private readonly IJSRuntime _js;
    private readonly AuthenticationStateProvider _authentication;
    private readonly Dictionary<(Guid, bool), Entry> _entries = [];
    private readonly SemaphoreSlim _transfers = new(2, 2);
    private CancellationTokenSource _accountLifetime = new();
    private IJSObjectReference? _module;
    private string? _account;
    private bool _initialized;
    private bool _disposed;
    private int _stateVersion;
    public ProfileMediaCache(ProfileApi api, IJSRuntime js, AuthenticationStateProvider authentication)
    {
        _api = api; _js = js; _authentication = authentication;
        authentication.AuthenticationStateChanged += AccountChanged;
    }
    public async Task<MediaBlobLease> AcquireAsync(MediaDto media, bool poster)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) { _initialized = true; _account = (await _authentication.GetAuthenticationStateAsync()).User.FindFirst(ClaimTypes.NameIdentifier)?.Value; }
        var key = (media.Id, poster);
        if (!_entries.TryGetValue(key, out var entry)) { entry = new Entry(); _entries[key] = entry; entry.Load = LoadAsync(media, poster, _accountLifetime.Token); }
        entry.References++;
        try { return new MediaBlobLease(await entry.Load, () => ReleaseAsync(key, entry)); }
        catch { await ReleaseAsync(key, entry); throw; }
    }
    private async Task<string> LoadAsync(MediaDto media, bool poster, CancellationToken ct)
    {
        await _transfers.WaitAsync(ct);
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ProfileMediaModule.Url);
            string? url = null;
            await _api.ConsumeMediaAsync(media.Id, poster, async stream =>
            {
                using var reference = new DotNetStreamReference(stream, leaveOpen: true);
                url = await _module.InvokeAsync<string>("objectUrl", ct, reference, poster ? "image/webp" : media.ContentType);
            }, ct);
            if (ct.IsCancellationRequested) { if (url is not null) await _module.InvokeVoidAsync("release", url); ct.ThrowIfCancellationRequested(); }
            return url!;
        }
        finally { _transfers.Release(); }
    }
    private async ValueTask ReleaseAsync((Guid, bool) key, Entry entry)
    {
        if (entry.Released) return;
        if (--entry.References > 0) return;
        if (_entries.GetValueOrDefault(key) == entry) _entries.Remove(key);
        entry.Released = true;
        try { var url = await entry.Load; if (_module is not null) await _module.InvokeVoidAsync("release", url); }
        catch (Exception ex) when (ex is OperationCanceledException or JSException or Frontend.Shared.Api.ApiException) { }
    }
    private async void AccountChanged(Task<AuthenticationState> pending)
    {
        var version = ++_stateVersion;
        try
        {
            var account = (await pending).User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (_disposed || version != _stateVersion) return;
            if (_initialized && account == _account) return;
            _account = account; _initialized = true;
            await ClearAsync();
        }
        catch (Exception ex) when (ex is JSException or OperationCanceledException or ObjectDisposedException) { }
    }
    /// <summary>
    /// Все ссылки отозваны (смена аккаунта). Показанные медиа держат уже мёртвый blob-адрес —
    /// они подписываются и перезагружаются, иначе на месте аватара остаётся пустота.
    /// </summary>
    public event Action? Cleared;

    private async Task ClearAsync()
    {
        _accountLifetime.Cancel(); _accountLifetime.Dispose(); _accountLifetime = new();
        var entries = _entries.Values.ToArray(); _entries.Clear();
        foreach (var entry in entries)
            try { entry.Released = true; var url = await entry.Load; if (_module is not null) await _module.InvokeVoidAsync("release", url); }
            catch (Exception ex) when (ex is OperationCanceledException or JSException or Frontend.Shared.Api.ApiException) { }
        if (entries.Length > 0 && !_disposed) Cleared?.Invoke();
    }
    public async ValueTask DisposeAsync()
    {
        _disposed = true; _authentication.AuthenticationStateChanged -= AccountChanged;
        await ClearAsync(); _accountLifetime.Cancel(); _accountLifetime.Dispose();
        if (_module is not null) await _module.DisposeAsync();
        _transfers.Dispose();
    }
    private sealed class Entry { public Task<string> Load { get; set; } = Task.FromResult(""); public int References { get; set; } public bool Released { get; set; } }
}

public sealed class MediaBlobLease(string url, Func<ValueTask> release) : IAsyncDisposable
{
    public string Url { get; } = url;
    private bool _disposed;
    public async ValueTask DisposeAsync() { if (_disposed) return; _disposed = true; await release(); }
}

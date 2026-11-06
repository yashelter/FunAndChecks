namespace FunAndChecks.Application.Backups;

/// <summary>
/// Флаг технического режима на время восстановления БД из бэкапа: API отвечает 503,
/// фоновые задачи, пишущие в БД, пропускают итерации. Singleton.
/// Также считает выполняющиеся API-запросы (чтобы восстановление дождалось уже начатых)
/// и идущие создания бэкапов (чтобы очистка не удаляла файлы, пока их архивируют).
/// </summary>
public sealed class MaintenanceModeState
{
    private volatile bool _active;
    private int _requestsInFlight;
    private int _backupsInProgress;

    public bool IsActive => _active;

    public void Enter() => _active = true;

    public void Exit() => _active = false;

    /// <summary>Число API-запросов, пропущенных в обработку и ещё не завершённых.</summary>
    public int RequestsInFlight => Volatile.Read(ref _requestsInFlight);

    public void RequestStarted() => Interlocked.Increment(ref _requestsInFlight);

    public void RequestFinished() => Interlocked.Decrement(ref _requestsInFlight);

    /// <summary>
    /// Ждёт завершения уже начатых запросов (новые в техрежиме не пропускаются).
    /// <paramref name="ignored"/> — запросы самого вызывающего, которые ещё в обработке.
    /// false — не дождались за <paramref name="timeout"/>.
    /// </summary>
    public async Task<bool> WaitForRequestsAsync(TimeSpan timeout, CancellationToken cancellationToken, int ignored = 0)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (RequestsInFlight > ignored)
        {
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(50, cancellationToken);
        }
        return true;
    }

    /// <summary>Идёт создание бэкапа: дамп уже снят, а каталог вложений ещё архивируется.</summary>
    public bool BackupInProgress => Volatile.Read(ref _backupsInProgress) > 0;

    public IDisposable BeginBackup()
    {
        Interlocked.Increment(ref _backupsInProgress);
        return new BackupScope(this);
    }

    private sealed class BackupScope(MaintenanceModeState owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref owner._backupsInProgress);
        }
    }
}

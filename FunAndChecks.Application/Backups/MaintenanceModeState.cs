namespace FunAndChecks.Application.Backups;

/// <summary>
/// Флаг технического режима на время восстановления БД из бэкапа: API отвечает 503,
/// фоновые задачи, пишущие в БД, пропускают итерации. Singleton.
/// </summary>
public sealed class MaintenanceModeState
{
    private volatile bool _active;

    public bool IsActive => _active;

    public void Enter() => _active = true;

    public void Exit() => _active = false;
}

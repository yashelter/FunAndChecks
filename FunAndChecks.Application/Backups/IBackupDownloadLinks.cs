namespace FunAndChecks.Application.Backups;

/// <summary>
/// Короткоживущие подписанные ссылки на скачивание бэкапа. Браузер качает файл напрямую, потоком на диск:
/// загрузка через WASM держала бы весь дамп в памяти вкладки. Ссылку выдаёт только суперадмин.
/// </summary>
public interface IBackupDownloadLinks
{
    /// <summary>Относительный адрес скачивания, действительный несколько минут.</summary>
    string CreateUrl(string fileName);

    bool Verify(string fileName, long expiresUnixSeconds, string? signature);
}

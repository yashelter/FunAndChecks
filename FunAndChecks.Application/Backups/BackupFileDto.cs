namespace FunAndChecks.Application.Backups;

/// <summary>Подписанная ссылка на скачивание бэкапа (относительно корня сайта).</summary>
public record BackupDownloadLinkDto(string Url);

public record BackupFileDto(string FileName, long SizeBytes, DateTime LastModifiedUtc);

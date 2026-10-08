namespace FunAndChecks.Application.Backups;

public record BackupFileDto(string FileName, long SizeBytes, DateTime LastModifiedUtc);

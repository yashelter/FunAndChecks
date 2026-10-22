namespace FunAndChecks.Observability;

public sealed class AdminAuditOptions
{
    public bool Enabled { get; set; } = true;
    public string Directory { get; set; } = "../logs";
    public int QueueCapacity { get; set; } = 2000;
    public long FileSizeLimitBytes { get; set; } = 16 * 1024 * 1024;
    public int RetainedFileCount { get; set; } = 128;
    public int RetainedDays { get; set; } = 14;
}

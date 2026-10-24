namespace FunAndChecks.Infrastructure.Profiles;

public sealed class ProfileMediaOptions
{
    public const string SectionName = "ProfileMedia";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public string TemporaryDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "funandchecks-profile-media");
    public int SessionLifetimeMinutes { get; set; } = 20;
    public int CleanupIntervalMinutes { get; set; } = 5;
    public int MaxSessionsPerUser { get; set; } = 2;
    public int MaxSessions { get; set; } = 32;
    public long TemporaryQuotaBytes { get; set; } = 1024L * 1024 * 1024;
    public int ProcessingTimeoutSeconds { get; set; } = 60;
    public long ProcessMemoryLimitBytes { get; set; } = 512L * 1024 * 1024;
}

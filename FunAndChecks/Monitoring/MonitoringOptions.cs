namespace FunAndChecks.Monitoring;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";
    public bool Enabled { get; set; }
    public string ProxyKey { get; set; } = "";
    public string ScrapeKey { get; set; } = "";
}

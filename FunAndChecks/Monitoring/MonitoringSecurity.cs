using System.Security.Cryptography;
using System.Text;

namespace FunAndChecks.Monitoring;

public static class MonitoringSecurity
{
    public static bool HasValidSecret(string? supplied, string configured)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(configured))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
    }

    public static string SafeGrafanaReturn(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("/metrics/", StringComparison.Ordinal) ||
            value.Contains('\\') || value.Any(char.IsControl))
            return "/metrics/";
        // Normalize dot segments (including encoded dots) before accepting a
        // path, so a purported Grafana deep link cannot escape its mount point.
        return Uri.TryCreate("https://monitoring.invalid" + value, UriKind.Absolute, out var target) &&
               target.AbsolutePath.StartsWith("/metrics/", StringComparison.Ordinal)
            ? target.PathAndQuery + target.Fragment
            : "/metrics/";
    }
}

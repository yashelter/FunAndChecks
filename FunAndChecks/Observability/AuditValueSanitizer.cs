using System.Collections;
using System.Text.RegularExpressions;

namespace FunAndChecks.Observability;

/// <summary>Defence in depth for the safe application snapshots. No arbitrary object reflection.</summary>
public static partial class AuditValueSanitizer
{
    public static IReadOnlyDictionary<string, object?>? Sanitize(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null) return null;
        return values.ToDictionary(pair => pair.Key, pair => SafeProperty(pair.Key, pair.Value, 0));
    }

    public static string Redact(string text)
    {
        var credentials = Credentials().Replace(text, match =>
        {
            var value = match.Groups["value"].Value;
            var masked = value.StartsWith('"') ? "\"[redacted]\"" : value.StartsWith('\'') ? "'[redacted]'" : "[redacted]";
            return match.Groups["prefix"].Value + masked;
        });
        return Jwt().Replace(Bearer().Replace(credentials, "Bearer [redacted]"), "[redacted]");
    }

    private static object? SafeProperty(string key, object? value, int depth) =>
        key.Equals("passwordChanged", StringComparison.OrdinalIgnoreCase) && value is bool
            ? value : SensitiveKey().IsMatch(key) ? "[redacted]" : SafeValue(value, depth);

    private static object? SafeValue(object? value, int depth)
    {
        if (depth > 8) return "[omitted]";
        return value switch
        {
            null => null,
            string text => Redact(text),
            byte[] => "[binary omitted]",
            Enum enumValue => enumValue.ToString(),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or Guid or DateTime or DateTimeOffset => value,
            IReadOnlyDictionary<string, object?> dictionary => dictionary.ToDictionary(pair => pair.Key,
                pair => SafeProperty(pair.Key, pair.Value, depth + 1)),
            IEnumerable sequence => sequence.Cast<object?>().Select(item => SafeValue(item, depth + 1)).ToArray(),
            _ => "[unsupported value omitted]"
        };
    }

    [GeneratedRegex("password|passwordhash|securitystamp|secret|token|verification|confirmationcode|resetcode|connectionstring|apikey", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKey();
    [GeneratedRegex("""(?i)(?<prefix>["']?\b(?:password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|api[_-]?key)["']?\s*[:=]\s*)(?<value>"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[^\s;,&}]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex Credentials();
    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex Bearer();
    [GeneratedRegex("\\beyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Jwt();
}

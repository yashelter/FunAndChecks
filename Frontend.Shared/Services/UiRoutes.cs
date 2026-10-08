namespace Frontend.Shared.Services;

/// <summary>Maps a UI page to its equivalent in the other interface.</summary>
public static class UiRoutes
{
    public static bool IsLegacy(string path) =>
        path.Equals("/legacy", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/legacy/", StringComparison.OrdinalIgnoreCase);

    public static string OtherVersion(string localUrl)
    {
        if (!localUrl.StartsWith('/') || localUrl.StartsWith("//", StringComparison.Ordinal) || localUrl.Contains('\\'))
            throw new ArgumentException("Expected a local absolute UI path.", nameof(localUrl));

        var end = localUrl.IndexOfAny(['?', '#']);
        var path = end < 0 ? localUrl : localUrl[..end];
        var suffix = end < 0 ? string.Empty : localUrl[end..];
        if (IsLegacy(path))
        {
            var target = path[7..];
            return (string.IsNullOrEmpty(target) ? "/" : target) + suffix;
        }

        return (path == "/" ? "/legacy" : "/legacy" + path) + suffix;
    }
}

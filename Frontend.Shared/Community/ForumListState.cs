namespace Frontend.Shared.Community;

/// <summary>Последний открытый список тем (раздел, поиск, страница) — ссылка «назад» из темы ведёт в него.</summary>
public sealed class ForumListState
{
    private readonly Dictionary<string, string> _last = [];

    public void Remember(string basePath, string url) => _last[basePath] = url;

    public string LastUrl(string basePath) => _last.GetValueOrDefault(basePath) ?? $"{basePath}/forum";
}

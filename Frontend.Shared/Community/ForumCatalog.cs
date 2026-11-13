namespace Frontend.Shared.Community;

/// <summary>
/// Набор реакций и стикеров на вкладку: грузится один раз и переиспользуется всеми сообщениями.
/// Админка набора сбрасывает кеш после правок.
/// </summary>
public sealed class ForumCatalog(ForumApi api)
{
    private Task<List<ForumReactionTypeDto>>? _reactions;
    private Task<List<ForumStickerPackDto>>? _stickers;

    public async Task<IReadOnlyList<ForumReactionTypeDto>> ReactionsAsync()
    {
        try { return await (_reactions ??= api.ReactionTypesAsync()); }
        catch (Exception) { _reactions = null; return []; }
    }

    public async Task<IReadOnlyList<ForumStickerPackDto>> StickersAsync()
    {
        try { return await (_stickers ??= api.StickerPacksAsync()); }
        catch (Exception) { _stickers = null; return []; }
    }

    public void Invalidate()
    {
        _reactions = null;
        _stickers = null;
    }

    /// <summary>Часто используемые эмодзи для вставки в текст.</summary>
    public static readonly string[] Emoji =
    [
        "😀", "😁", "😂", "🤣", "😊", "😉", "😍", "🥰", "😎", "🤓", "🤔", "🤨", "😐", "🙄", "😏", "😴",
        "😮", "😱", "😭", "😅", "😬", "🥲", "🙃", "😇", "🤯", "🥳", "😤", "😡", "🤝", "👍", "👎", "👏",
        "🙏", "💪", "👀", "👋", "✌️", "🤞", "👌", "☝️", "🔥", "✨", "🎉", "💯", "✅", "❌", "⚠️", "❓",
        "❤️", "💔", "⭐", "📌", "📎", "📚", "📝", "💻", "⌨️", "🐛", "🚀", "⏰", "☕", "🍕",
    ];
}

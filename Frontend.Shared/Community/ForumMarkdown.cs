using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Frontend.Shared.Community;

/// <summary>
/// Markdown сообщений форума. Сырой HTML выключен, ссылки — только http(s)/mailto и упоминания
/// вида @[Имя](user:GUID), картинки по внешним URL превращаются в обычные ссылки (не трекаем читателей).
/// </summary>
public static partial class ForumMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    [GeneratedRegex(@"^user:(?<id>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$")]
    private static partial Regex MentionUrl();

    public static string ToHtml(string markdown, string basePath)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            link.IsImage = false;
            var url = link.Url?.Trim() ?? "";
            var mention = MentionUrl().Match(url);
            if (mention.Success)
            {
                link.Url = $"{basePath}/profile/{mention.Groups["id"].Value}";
                link.GetAttributes().AddClass("fc-mention");
                // Старый формат @[Имя](…): «@» стоял перед ссылкой — переносим его внутрь, как в новом [@Имя](…).
                if (link.PreviousSibling is LiteralInline before && before.Content.Length > 0 &&
                    before.Content.Text[before.Content.End] == '@')
                {
                    before.Content = new Markdig.Helpers.StringSlice(before.Content.Text, before.Content.Start, before.Content.End - 1);
                    if (link.FirstChild is LiteralInline label && !label.Content.ToString().StartsWith('@'))
                        label.Content = new Markdig.Helpers.StringSlice("@" + label.Content);
                }
                continue;
            }
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                link.GetAttributes().AddPropertyIfNotExist("target", "_blank");
                link.GetAttributes().AddPropertyIfNotExist("rel", "nofollow noopener noreferrer");
            }
            else if (!url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                link.Url = "#";
        }
        foreach (var link in document.Descendants<AutolinkInline>())
        {
            if (!link.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !link.IsEmail) link.Url = "#";
            link.GetAttributes().AddPropertyIfNotExist("rel", "nofollow noopener noreferrer");
        }
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>Текст без разметки для цитат и превью: «**жирный** [ссылка](…)» → «жирный ссылка».</summary>
    public static string ToPlainText(string markdown, int maxLength = 160)
    {
        var plain = Markdown.ToPlainText(markdown, Pipeline).ReplaceLineEndings(" ").Trim();
        while (plain.Contains("  ")) plain = plain.Replace("  ", " ");
        return plain.Length <= maxLength ? plain : plain[..maxLength].TrimEnd() + "…";
    }

    /// <summary>Вставка упоминания в текст сообщения.</summary>
    public static string Mention(string name, Guid userId) =>
        $"[@{name.Replace("]", "").Replace("[", "")}](user:{userId})";
}

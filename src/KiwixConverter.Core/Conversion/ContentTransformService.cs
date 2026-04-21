using System.Text;
using HtmlAgilityPack;
using KiwixConverter.Core.Models;
using ReverseMarkdown;

namespace KiwixConverter.Core.Conversion;

public sealed class ContentTransformService
{
    private static readonly string[] CandidateXpaths =
    [
        "//main",
        "//article",
        "//*[@id='mw-content-text']",
        "//*[contains(concat(' ', normalize-space(@class), ' '), ' mw-parser-output ')]",
        "//*[@id='content']",
        "//*[contains(concat(' ', normalize-space(@class), ' '), ' content ')]",
        "//body"
    ];

    private readonly Converter _markdownConverter = new(new Config
    {
        GithubFlavored = true,
        SmartHrefHandling = true,
        RemoveComments = true,
        UnknownTags = Config.UnknownTagsOption.Bypass
    });

    public PreparedArticleContent PrepareMainContent(string html, string fallbackTitle)
    {
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true
        };
        document.LoadHtml(html);

        var title = ExtractTitle(document, fallbackTitle);
        var candidateResults = new List<(string Strategy, HtmlNode Node, int Score)>();

        foreach (var xpath in CandidateXpaths)
        {
            var nodes = document.DocumentNode.SelectNodes(xpath);
            if (nodes is null)
            {
                continue;
            }

            foreach (var node in nodes)
            {
                var clone = node.CloneNode(true);
                StripNoise(clone);
                NormalizeScienceMarkup(clone);
                var score = MeasureContentScore(clone);
                if (score > 50)
                {
                    candidateResults.Add((xpath, clone, score));
                }
            }
        }

        if (candidateResults.Count == 0)
        {
            var fallbackNode = document.DocumentNode.SelectSingleNode("//body")?.CloneNode(true)
                ?? document.DocumentNode.CloneNode(true);
            StripNoise(fallbackNode);
            NormalizeScienceMarkup(fallbackNode);
            candidateResults.Add(("fallback-body", fallbackNode, MeasureContentScore(fallbackNode)));
        }

        var bestCandidate = candidateResults
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Strategy, StringComparer.Ordinal)
            .First();

        return new PreparedArticleContent
        {
            Title = title,
            Strategy = bestCandidate.Strategy,
            HtmlFragment = bestCandidate.Node.OuterHtml
        };
    }

    public HtmlDocument CreateDocumentFromFragment(string htmlFragment)
    {
        var wrappedHtml = $"<html><body>{htmlFragment}</body></html>";
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true
        };
        document.LoadHtml(wrappedHtml);
        return document;
    }

    public string ConvertToMarkdown(HtmlDocument document)
    {
        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var markdown = _markdownConverter.Convert(body.InnerHtml);
        markdown = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        while (markdown.Contains("\n\n\n", StringComparison.Ordinal))
        {
            markdown = markdown.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return markdown.Trim() + Environment.NewLine;
    }

    public IReadOnlyList<RagChunk> CreateChunks(string markdown, string articleUrl, int maxCharacters = 1800, int overlapCharacters = 250)
    {
        var normalizedText = markdown.Trim();
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return [];
        }

        var chunks = new List<RagChunk>();
        var paragraphs = normalizedText.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();
        string? currentHeading = null;

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.StartsWith('#'))
            {
                currentHeading = paragraph.Trim();
            }

            var separator = builder.Length == 0 ? string.Empty : "\n\n";
            if (builder.Length > 0 && builder.Length + separator.Length + paragraph.Length > maxCharacters)
            {
                chunks.Add(CreateChunk(articleUrl, chunks.Count, builder.ToString(), currentHeading));
                var overlapText = TakeOverlap(builder.ToString(), overlapCharacters);
                builder.Clear();
                builder.Append(overlapText);
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            if (paragraph.Length > maxCharacters)
            {
                foreach (var section in SplitLongParagraph(paragraph, maxCharacters))
                {
                    if (builder.Length > 0 && builder.Length + 2 + section.Length > maxCharacters)
                    {
                        chunks.Add(CreateChunk(articleUrl, chunks.Count, builder.ToString(), currentHeading));
                        var overlapText = TakeOverlap(builder.ToString(), overlapCharacters);
                        builder.Clear();
                        builder.Append(overlapText);
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append("\n\n");
                    }

                    builder.Append(section);
                }

                continue;
            }

            builder.Append(paragraph);
        }

        if (builder.Length > 0)
        {
            chunks.Add(CreateChunk(articleUrl, chunks.Count, builder.ToString(), currentHeading));
        }

        return chunks;
    }

    private static string ExtractTitle(HtmlDocument document, string fallbackTitle)
    {
        var title = document.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty)
            ?? document.DocumentNode.SelectSingleNode("//title")?.InnerText
            ?? document.DocumentNode.SelectSingleNode("//h1")?.InnerText
            ?? fallbackTitle;

        title = HtmlEntity.DeEntitize(title).Trim();
        return string.IsNullOrWhiteSpace(title) ? fallbackTitle : title;
    }

    private static void StripNoise(HtmlNode node)
    {
        foreach (var xpath in new[]
                 {
                     ".//script",
                     ".//style",
                     ".//noscript",
                     ".//nav",
                     ".//header",
                     ".//footer",
                     ".//aside",
                     ".//*[contains(concat(' ', normalize-space(@class), ' '), ' navbox ')]",
                     ".//*[contains(concat(' ', normalize-space(@class), ' '), ' infobox ')]",
                     ".//*[contains(concat(' ', normalize-space(@class), ' '), ' sidebar ')]",
                     ".//*[contains(concat(' ', normalize-space(@class), ' '), ' toc ')]",
                     ".//*[contains(concat(' ', normalize-space(@class), ' '), ' metadata ')]",
                     ".//*[contains(concat(' ', normalize-space(@class), ' '), ' reference ')]"
                 })
        {
            var nodes = node.SelectNodes(xpath);
            if (nodes is null)
            {
                continue;
            }

            foreach (var child in nodes.ToList())
            {
                child.Remove();
            }
        }
    }

    private static void NormalizeScienceMarkup(HtmlNode root)
    {
        foreach (var mathNode in root.Descendants().Where(static descendant => descendant.Name.Equals("math", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var formula = ExtractFormulaText(mathNode);
            if (string.IsNullOrWhiteSpace(formula))
            {
                formula = HtmlEntity.DeEntitize(mathNode.InnerText).Trim();
            }

            var isBlock = string.Equals(mathNode.GetAttributeValue("display", string.Empty), "block", StringComparison.OrdinalIgnoreCase)
                || formula.Contains('\n');
            var markdown = isBlock
                ? Environment.NewLine + Environment.NewLine + "$$" + Environment.NewLine + formula + Environment.NewLine + "$$" + Environment.NewLine + Environment.NewLine
                : "$" + formula.Replace("$", string.Empty, StringComparison.Ordinal) + "$";

            mathNode.ParentNode?.ReplaceChild(mathNode.OwnerDocument.CreateTextNode(markdown), mathNode);
        }

        foreach (var imageNode in root.Descendants("img"))
        {
            var alt = HtmlEntity.DeEntitize(imageNode.GetAttributeValue("alt", string.Empty)).Trim();
            if (!string.IsNullOrWhiteSpace(alt) && !imageNode.Attributes.Contains("title"))
            {
                imageNode.SetAttributeValue("title", alt);
            }
        }
    }

    private static string ExtractFormulaText(HtmlNode mathNode)
    {
        var altText = mathNode.GetAttributeValue("alttext", string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(altText))
        {
            return altText.Replace("{\\displaystyle ", string.Empty, StringComparison.Ordinal)
                .Replace("}", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        var annotation = mathNode.Descendants().FirstOrDefault(static descendant => descendant.Name.Equals("annotation", StringComparison.OrdinalIgnoreCase));
        if (annotation is not null)
        {
            return HtmlEntity.DeEntitize(annotation.InnerText).Trim();
        }

        return HtmlEntity.DeEntitize(mathNode.InnerText).Trim();
    }

    private static int MeasureContentScore(HtmlNode node)
    {
        var text = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty);
        var normalizedLength = text.Where(static character => !char.IsWhiteSpace(character)).Count();
        var paragraphBonus = node.Descendants("p").Count() * 25;
        return normalizedLength + paragraphBonus;
    }

    private static RagChunk CreateChunk(string articleUrl, int index, string text, string? heading)
    {
        var normalizedUrl = LinkPathService.NormalizeUrl(articleUrl).Replace('/', '_');
        var value = text.Trim();
        return new RagChunk
        {
            ChunkId = $"{normalizedUrl}_{index:D4}",
            Index = index,
            Text = value,
            CharacterCount = value.Length,
            Heading = heading
        };
    }

    private static string TakeOverlap(string text, int overlapCharacters)
    {
        var value = text.Trim();
        if (value.Length <= overlapCharacters)
        {
            return value;
        }

        return value[^overlapCharacters..];
    }

    private static IEnumerable<string> SplitLongParagraph(string paragraph, int maxCharacters)
    {
        var value = paragraph.Trim();
        while (value.Length > maxCharacters)
        {
            var breakIndex = value.LastIndexOf(' ', maxCharacters);
            if (breakIndex < maxCharacters / 2)
            {
                breakIndex = maxCharacters;
            }

            yield return value[..breakIndex].Trim();
            value = value[breakIndex..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            yield return value;
        }
    }
}
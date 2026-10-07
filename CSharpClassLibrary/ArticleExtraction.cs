using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CSharpClassLibrary;

public enum ExtractionSource
{
    PreloadedData,
    JsonLd,
    RenderedHtml,
}

public sealed record Inline(string Text, Uri? Link = null);

public abstract record ArticleBlock;

public sealed record Paragraph(IReadOnlyList<Inline> Inlines) : ArticleBlock
{
    public Paragraph(string text) : this([new Inline(text)])
    {
    }

    public string Text => TextTools.Normalize(string.Concat(Inlines.Select(inline => inline.Text)));

    public IEnumerable<Inline> Links => Inlines.Where(inline => inline.Link is not null);
}

public sealed record Heading(int Level, string Text) : ArticleBlock;

public sealed record Quote(string Text) : ArticleBlock;

public sealed record ItemList(IReadOnlyList<string> Items, bool Ordered) : ArticleBlock;

public sealed record Figure(string? Caption, string? Credit) : ArticleBlock;

public sealed record Graphic(string? Title, string? Description, string? Note, string? Source) : ArticleBlock;

public sealed record Note(string Text) : ArticleBlock;

public sealed record Article(
    string Headline,
    string? Summary,
    IReadOnlyList<string> Authors,
    DateTimeOffset? Published,
    DateTimeOffset? Modified,
    Uri? Url,
    IReadOnlyList<string> Sections,
    int? ReportedWordCount,
    IReadOnlyList<ArticleBlock> Body,
    ExtractionSource Source)
{
    public IEnumerable<Paragraph> Paragraphs => Body.OfType<Paragraph>();

    public string Byline => Authors.Count == 0 ? "" : "By " + TextTools.JoinNames(Authors);

    public int WordCount => Body.Sum(block => block switch
    {
        Paragraph paragraph => TextTools.CountWords(paragraph.Text),
        Heading heading => TextTools.CountWords(heading.Text),
        Quote quote => TextTools.CountWords(quote.Text),
        ItemList list => list.Items.Sum(TextTools.CountWords),
        _ => 0,
    });
}

public sealed record ArticleDigest(Article Article, string Prose, string Summary);

public sealed record PageMetadata(
    string? Headline,
    string? Summary,
    IReadOnlyList<string> Authors,
    DateTimeOffset? Published,
    DateTimeOffset? Modified,
    Uri? Url,
    IReadOnlyList<string> Sections,
    string? ArticleBody)
{
    public static PageMetadata Empty { get; } = new(null, null, [], null, null, null, [], null);
}

public sealed record FormatOptions(bool IncludeMetadata = true, bool IncludeFigures = false, bool IncludeGraphics = false)
{
    public static FormatOptions Default { get; } = new();
}

public sealed class ArticleExtractionException : Exception
{
    public ArticleExtractionException()
    {
    }

    public ArticleExtractionException(string message) : base(message)
    {
    }

    public ArticleExtractionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public interface IArticleExtractor
{
    Article? Extract(HtmlPage page);
}

public interface IArticleFormatter
{
    string Format(Article article);
}

public interface ISummarizer
{
    string Summarize(Article article);
}

public static partial class TextTools
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;

        foreach (char c in text)
        {
            if (c is '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF' or '\u00AD')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    public static string StripTags(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        string spaced = BreakTagPattern().Replace(html, " ");
        string stripped = TagPattern().Replace(spaced, "");
        return Normalize(WebUtility.HtmlDecode(stripped));
    }

    public static int CountWords(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Any(char.IsLetterOrDigit));
    }

    public static IReadOnlyList<string> Words(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. WordPattern().Matches(text).Select(match => match.Value.ToLowerInvariant().Replace('\u2019', '\''))];
    }

    public static string JoinNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6])\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTagPattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['\u2019\-][\p{L}\p{N}]+)*")]
    private static partial Regex WordPattern();
}

public static class ScriptData
{
    public static bool TryExtractAssignedObject(string source, string variableName, [NotNullWhen(true)] out string? json)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(variableName);

        int searchFrom = 0;
        while (true)
        {
            int nameIndex = source.IndexOf(variableName, searchFrom, StringComparison.Ordinal);
            if (nameIndex < 0)
            {
                json = null;
                return false;
            }

            searchFrom = nameIndex + variableName.Length;

            if (nameIndex > 0 && IsIdentifierChar(source[nameIndex - 1]))
            {
                continue;
            }

            int position = SkipWhitespace(source, searchFrom);
            if (position >= source.Length || source[position] != '=')
            {
                continue;
            }

            position = SkipWhitespace(source, position + 1);
            if (position < source.Length && source[position] is '{' or '[' && TryReadLiteral(source, position, out json))
            {
                return true;
            }
        }
    }

    private static bool TryReadLiteral(string source, int start, [NotNullWhen(true)] out string? json)
    {
        const string Undefined = "undefined";
        var builder = new StringBuilder();
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < source.Length; i++)
        {
            char c = source[i];

            if (inString)
            {
                builder.Append(c);
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    builder.Append(c);
                    break;
                case '{' or '[':
                    depth++;
                    builder.Append(c);
                    break;
                case '}' or ']':
                    depth--;
                    builder.Append(c);
                    if (depth == 0)
                    {
                        json = builder.ToString();
                        return true;
                    }

                    break;
                case 'u' when string.CompareOrdinal(source, i, Undefined, 0, Undefined.Length) == 0
                    && !IsIdentifierChar(source[i - 1])
                    && (i + Undefined.Length >= source.Length || !IsIdentifierChar(source[i + Undefined.Length])):
                    builder.Append("null");
                    i += Undefined.Length - 1;
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        json = null;
        return false;
    }

    private static int SkipWhitespace(string source, int position)
    {
        while (position < source.Length && char.IsWhiteSpace(source[position]))
        {
            position++;
        }

        return position;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}

public static class SentenceSplitter
{
    private const string Closers = "\"'\u201D\u2019)]";

    public static IReadOnlyList<string> Split(string text)
    {
        string normalized = TextTools.Normalize(text);
        var sentences = new List<string>();
        int start = 0;

        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (c is not ('.' or '!' or '?' or '\u2026'))
            {
                continue;
            }

            int end = i + 1;
            while (end < normalized.Length && Closers.Contains(normalized[end], StringComparison.Ordinal))
            {
                end++;
            }

            if (end < normalized.Length && (normalized[end] != ' ' || !StartsSentence(normalized[end + 1])))
            {
                continue;
            }

            if (c == '.' && end == i + 1 && IsAbbreviation(normalized, i))
            {
                continue;
            }

            Add(sentences, normalized[start..end]);
            start = end;
        }

        Add(sentences, normalized[start..]);
        return sentences;
    }

    private static bool StartsSentence(char c) =>
        char.IsUpper(c) || char.IsDigit(c) || c is '"' or '\u201C' or '\u2018' or '\'' or '(' or '[';

    private static bool IsAbbreviation(string text, int periodIndex)
    {
        int wordStart = periodIndex;
        while (wordStart > 0 && (char.IsLetter(text[wordStart - 1]) || text[wordStart - 1] == '.'))
        {
            wordStart--;
        }

        string word = text[wordStart..periodIndex];
        return (word.Length == 1 && char.IsUpper(word[0])) || IsAbbreviationWord(word.ToLowerInvariant());
    }

    private static bool IsAbbreviationWord(string word) => word is
        "mr" or "mrs" or "ms" or "dr" or "prof" or "sen" or "rep" or "gov" or "gen" or "lt"
        or "col" or "sgt" or "capt" or "adm" or "maj" or "st" or "jr" or "sr" or "inc" or "co"
        or "corp" or "ltd" or "vs" or "etc" or "no" or "mt" or "ft" or "ave" or "blvd" or "jan"
        or "feb" or "mar" or "apr" or "aug" or "sept" or "sep" or "oct" or "nov" or "dec" or "u.s"
        or "u.n" or "u.k" or "e.u" or "d.c" or "a.m" or "p.m" or "i.e" or "e.g";

    private static void Add(List<string> sentences, string candidate)
    {
        string trimmed = candidate.Trim();
        if (trimmed.Length > 0)
        {
            sentences.Add(trimmed);
        }
    }
}

public sealed partial class HtmlPage
{
    private static JsonDocumentOptions JsonOptions => new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256 };

    private HtmlPage(string html, PageMetadata metadata)
    {
        Html = html;
        Metadata = metadata;
    }

    public string Html { get; }

    public PageMetadata Metadata { get; }

    public static HtmlPage Parse(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        IReadOnlyDictionary<string, string> meta = ReadMetaTags(html);
        PageMetadata fromJsonLd = ReadJsonLd(html);
        string? title = TitlePattern().Match(html) is { Success: true } match ? TextTools.StripTags(match.Groups[1].Value) : null;

        var metadata = new PageMetadata(
            Headline: FirstNonEmpty(fromJsonLd.Headline, Get(meta, "og:title"), Get(meta, "twitter:title"), title),
            Summary: FirstNonEmpty(fromJsonLd.Summary, Get(meta, "description"), Get(meta, "og:description")),
            Authors: fromJsonLd.Authors.Count > 0 ? fromJsonLd.Authors : ParseByline(Get(meta, "byl") ?? Get(meta, "author")),
            Published: fromJsonLd.Published ?? ParseDate(Get(meta, "article:published_time")),
            Modified: fromJsonLd.Modified ?? ParseDate(Get(meta, "article:modified_time")),
            Url: fromJsonLd.Url ?? ParseUri(Get(meta, "og:url")) ?? ParseUri(Get(meta, "url")),
            Sections: fromJsonLd.Sections.Count > 0 ? fromJsonLd.Sections : SplitSections(Get(meta, "article:section")),
            ArticleBody: fromJsonLd.ArticleBody);

        return new HtmlPage(html, metadata);
    }

    internal static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : null;

    internal static Uri? ParseUri(string? value, Uri? baseUri = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? absolute) && absolute.Scheme is "http" or "https")
        {
            return absolute;
        }

        return baseUri is not null && Uri.TryCreate(baseUri, value.Trim(), out Uri? relative) ? relative : null;
    }

    internal static string? FirstNonEmpty(params ReadOnlySpan<string?> values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return TextTools.Normalize(value);
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> ParseByline(string? byline)
    {
        if (string.IsNullOrWhiteSpace(byline))
        {
            return [];
        }

        string names = TextTools.Normalize(byline);
        if (names.StartsWith("By ", StringComparison.OrdinalIgnoreCase))
        {
            names = names[3..];
        }

        return [.. BylineSeparatorPattern().Split(names).Select(TextTools.Normalize).Where(name => name.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    private static IReadOnlyList<string> SplitSections(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string? Get(IReadOnlyDictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out string? value) ? value : null;

    private static Dictionary<string, string> ReadMetaTags(string html)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match tag in MetaTagPattern().Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in AttributePattern().Matches(tag.Value))
            {
                attributes.TryAdd(attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["value"].Value));
            }

            if (attributes.TryGetValue("content", out string? content)
                && (attributes.TryGetValue("property", out string? key) || attributes.TryGetValue("name", out key))
                && !string.IsNullOrWhiteSpace(content))
            {
                meta.TryAdd(key, content);
            }
        }

        return meta;
    }

    private static PageMetadata ReadJsonLd(string html)
    {
        foreach (Match script in JsonLdPattern().Matches(html))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(script.Groups[1].Value, JsonOptions);
                if (FindArticleNode(document.RootElement) is { } node)
                {
                    return ReadJsonLdArticle(node);
                }
            }
            catch (JsonException)
            {
            }
        }

        return PageMetadata.Empty;
    }

    private static JsonElement? FindArticleNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (FindArticleNode(item) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            case JsonValueKind.Object:
                if (JsonTypes(element).Any(IsArticleType))
                {
                    return element;
                }

                return element.Child("@graph") is { } graph ? FindArticleNode(graph) : null;
            default:
                return null;
        }
    }

    private static IEnumerable<string> JsonTypes(JsonElement element) => element.Child("@type") switch
    {
        { ValueKind: JsonValueKind.String } single => [single.GetString()!],
        { ValueKind: JsonValueKind.Array } many => many.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!),
        _ => [],
    };

    private static bool IsArticleType(string type) =>
        type.EndsWith("Article", StringComparison.Ordinal) || type is "BlogPosting" or "Report";

    private static PageMetadata ReadJsonLdArticle(JsonElement node)
    {
        IReadOnlyList<string> authors = node.Child("author") switch
        {
            { ValueKind: JsonValueKind.Array } many => [.. many.EnumerateArray().Select(AuthorName).OfType<string>().Distinct(StringComparer.Ordinal)],
            { } single when AuthorName(single) is { } name => [name],
            _ => [],
        };

        IReadOnlyList<string> sections = node.Child("articleSection") switch
        {
            { ValueKind: JsonValueKind.String } single => SplitSections(single.GetString()),
            { ValueKind: JsonValueKind.Array } many => [.. many.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!.Trim())],
            _ => [],
        };

        return new PageMetadata(
            Headline: FirstNonEmpty(node.StringAt("alternativeHeadline"), node.StringAt("headline"), node.StringAt("name")),
            Summary: FirstNonEmpty(node.StringAt("description")),
            Authors: authors,
            Published: ParseDate(node.StringAt("datePublished")),
            Modified: ParseDate(node.StringAt("dateModified")),
            Url: ParseUri(node.StringAt("url")) ?? ParseUri(node.StringAt("@id")),
            Sections: sections,
            ArticleBody: node.StringAt("articleBody"));
    }

    private static string? AuthorName(JsonElement author) => author.ValueKind switch
    {
        JsonValueKind.String => FirstNonEmpty(author.GetString()),
        JsonValueKind.Object => FirstNonEmpty(author.StringAt("name")),
        _ => null,
    };

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagPattern();

    [GeneratedRegex(@"(?<name>[\w:.-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"<script\b[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdPattern();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"\s*,\s*(?:and\s+)?|\s+and\s+")]
    private static partial Regex BylineSeparatorPattern();
}

internal static class JsonElementExtensions
{
    extension(JsonElement element)
    {
        public JsonElement? Child(string name) =>
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value
                : null;

        public string? StringAt(string name) =>
            element.Child(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

        public IEnumerable<JsonElement> ArrayAt(string name) =>
            element.Child(name) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }
}

public sealed class PreloadedDataExtractor : IArticleExtractor
{
    public const string VariableName = "window.__preloadedData";

    private static Uri DefaultBase => new("https://www.nytimes.com/");

    private static JsonDocumentOptions JsonOptions => new() { AllowTrailingCommas = true, MaxDepth = 512 };

    public Article? Extract(HtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!ScriptData.TryExtractAssignedObject(page.Html, VariableName, out string? json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            return FindArticle(document.RootElement) is { } node ? ReadArticle(node, page.Metadata) : null;
        }
    }

    private static JsonElement? FindArticle(JsonElement root)
    {
        ReadOnlySpan<string> containers = ["loaderData", "initialData", "initialState"];
        foreach (string container in containers)
        {
            if (root.Child(container)?.Child("data")?.Child("article") is { ValueKind: JsonValueKind.Object } found && BodyContent(found).Any())
            {
                return found;
            }
        }

        return Search(root, depth: 0);
    }

    private static JsonElement? Search(JsonElement element, int depth)
    {
        if (depth > 12)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object && element.Child("headline") is not null && BodyContent(element).Any())
        {
            return element;
        }

        IEnumerable<JsonElement> children = element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Select(property => property.Value),
            JsonValueKind.Array => element.EnumerateArray(),
            _ => Enumerable.Empty<JsonElement>(),
        };

        foreach (JsonElement child in children)
        {
            if (Search(child, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> BodyContent(JsonElement article) =>
        (article.Child("sprinkledBody") ?? article.Child("body")) is { } body ? body.ArrayAt("content") : [];

    private static Article? ReadArticle(JsonElement node, PageMetadata metadata)
    {
        Uri? url = HtmlPage.ParseUri(node.StringAt("url")) ?? metadata.Url;
        Uri linkBase = url ?? DefaultBase;
        List<ArticleBlock> body = [.. BodyContent(node).SelectMany(block => ReadBlock(block, linkBase))];

        if (!body.OfType<Paragraph>().Any())
        {
            return null;
        }

        IReadOnlyList<string> authors = [.. node.ArrayAt("bylines")
            .SelectMany(byline => byline.ArrayAt("creators"))
            .Select(creator => HtmlPage.FirstNonEmpty(creator.StringAt("displayName")))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];

        if (authors.Count == 0)
        {
            authors = [.. node.ArrayAt("bylines").SelectMany(byline => HtmlPage.ParseByline(byline.StringAt("renderedRepresentation")))];
        }

        IReadOnlyList<string> sections = [.. new[] { node.Child("section"), node.Child("subsection") }
            .Select(section => section is { } value ? HtmlPage.FirstNonEmpty(value.StringAt("displayName"), value.StringAt("name")) : null)
            .OfType<string>()];

        JsonElement? headline = node.Child("headline");

        return new Article(
            Headline: HtmlPage.FirstNonEmpty(headline?.StringAt("default"), headline?.StringAt("seo"), metadata.Headline) ?? "",
            Summary: HtmlPage.FirstNonEmpty(node.StringAt("summary"), metadata.Summary),
            Authors: authors.Count > 0 ? authors : metadata.Authors,
            Published: HtmlPage.ParseDate(node.StringAt("firstPublished")) ?? metadata.Published,
            Modified: HtmlPage.ParseDate(node.StringAt("lastModified")) ?? metadata.Modified,
            Url: url,
            Sections: sections.Count > 0 ? sections : metadata.Sections,
            ReportedWordCount: node.Child("wordCount") is { ValueKind: JsonValueKind.Number } count && count.TryGetInt32(out int words) ? words : null,
            Body: body,
            Source: ExtractionSource.PreloadedData);
    }

    private static IEnumerable<ArticleBlock> ReadBlock(JsonElement block, Uri linkBase)
    {
        switch (block.StringAt("__typename"))
        {
            case "ParagraphBlock":
                if (ReadParagraph(block, linkBase) is { } paragraph)
                {
                    yield return paragraph;
                }

                break;
            case "BlockquoteBlock":
                string quote = TextTools.Normalize(string.Join(' ', block.ArrayAt("content").Select(PlainText)));
                if (quote.Length > 0)
                {
                    yield return new Quote(quote);
                }

                break;
            case "ListBlock":
                List<string> items = [.. block.ArrayAt("content").Select(item => TextTools.Normalize(PlainText(item))).Where(item => item.Length > 0)];
                if (items.Count > 0)
                {
                    yield return new ItemList(items, string.Equals(block.StringAt("style"), "ORDERED", StringComparison.OrdinalIgnoreCase));
                }

                break;
            case "ImageBlock":
                if (ReadFigure(block) is { } figure)
                {
                    yield return figure;
                }

                break;
            case "InteractiveBlock":
                if (ReadGraphic(block) is { } graphic)
                {
                    yield return graphic;
                }

                break;
            case "DetailBlock":
                string note = TextTools.Normalize(PlainText(block));
                if (note.Length > 0)
                {
                    yield return new Note(note);
                }

                break;
            case { } type when HeadingLevel(type) is int level:
                string heading = TextTools.Normalize(PlainText(block));
                if (heading.Length > 0)
                {
                    yield return new Heading(level, heading);
                }

                break;
            case { } type when type.StartsWith("Header", StringComparison.Ordinal):
                if (block.Child("ledeMedia") is { } lede && lede.StringAt("__typename") == "ImageBlock" && ReadFigure(lede) is { } ledeFigure)
                {
                    yield return ledeFigure;
                }

                break;
        }
    }

    private static int? HeadingLevel(string type) =>
        type.Length == "Heading1Block".Length
        && type.StartsWith("Heading", StringComparison.Ordinal)
        && type.EndsWith("Block", StringComparison.Ordinal)
        && type[7] is >= '1' and <= '6'
            ? type[7] - '0'
            : null;

    private static Paragraph? ReadParagraph(JsonElement block, Uri linkBase)
    {
        var inlines = new List<Inline>();

        foreach (Inline inline in ReadInlines(block, linkBase))
        {
            if (inlines.Count > 0 && inlines[^1].Link == inline.Link)
            {
                inlines[^1] = inlines[^1] with { Text = inlines[^1].Text + inline.Text };
            }
            else
            {
                inlines.Add(inline);
            }
        }

        var paragraph = new Paragraph(inlines);
        return paragraph.Text.Length > 0 ? paragraph : null;
    }

    private static IEnumerable<Inline> ReadInlines(JsonElement container, Uri linkBase)
    {
        foreach (JsonElement inline in container.ArrayAt("content"))
        {
            if (inline.StringAt("text") is { } text)
            {
                Uri? link = inline.ArrayAt("formats")
                    .Where(format => format.StringAt("__typename") == "LinkFormat")
                    .Select(format => HtmlPage.ParseUri(format.StringAt("url"), linkBase))
                    .FirstOrDefault(uri => uri is not null);
                yield return new Inline(text, link);
            }
            else if (inline.StringAt("__typename") == "LineBreakInline")
            {
                yield return new Inline(" ");
            }
            else
            {
                foreach (Inline nested in ReadInlines(inline, linkBase))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string PlainText(JsonElement element)
    {
        if (element.StringAt("text") is { } text)
        {
            return text;
        }

        if (element.StringAt("__typename") == "LineBreakInline")
        {
            return " ";
        }

        var builder = new StringBuilder();
        foreach (JsonElement child in element.ArrayAt("content"))
        {
            bool isInline = child.StringAt("text") is not null
                || (child.StringAt("__typename") is { } type && type.EndsWith("Inline", StringComparison.Ordinal));

            if (!isInline)
            {
                builder.Append(' ');
            }

            builder.Append(PlainText(child));
        }

        return builder.ToString();
    }

    private static Figure? ReadFigure(JsonElement block)
    {
        if (block.Child("media") is not { } media)
        {
            return null;
        }

        string? caption = HtmlPage.FirstNonEmpty(
            media.Child("caption")?.StringAt("text"),
            media.StringAt("legacyHtmlCaption") is { } legacy ? TextTools.StripTags(legacy) : null);
        string? credit = HtmlPage.FirstNonEmpty(media.StringAt("credit"));

        return caption is null && credit is null ? null : new Figure(caption, credit);
    }

    private static Graphic? ReadGraphic(JsonElement block)
    {
        if (block.Child("media") is not { } media)
        {
            return null;
        }

        var graphic = new Graphic(
            HtmlPage.FirstNonEmpty(media.Child("headline")?.StringAt("default")),
            HtmlPage.FirstNonEmpty(media.StringAt("leadin")),
            HtmlPage.FirstNonEmpty(media.StringAt("note")),
            HtmlPage.FirstNonEmpty(media.StringAt("dataSource")));

        return graphic is { Title: null, Description: null, Note: null, Source: null } ? null : graphic;
    }
}

public sealed partial class JsonLdExtractor : IArticleExtractor
{
    public Article? Extract(HtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (string.IsNullOrWhiteSpace(page.Metadata.ArticleBody))
        {
            return null;
        }

        List<ArticleBlock> body = [.. ParagraphBreakPattern().Split(page.Metadata.ArticleBody)
            .Select(TextTools.Normalize)
            .Where(text => text.Length > 0)
            .Select(text => new Paragraph(text))];

        return RenderedHtmlExtractor.FromMetadata(page.Metadata, body, ExtractionSource.JsonLd);
    }

    [GeneratedRegex(@"\r?\n\s*")]
    private static partial Regex ParagraphBreakPattern();
}

public sealed partial class RenderedHtmlExtractor : IArticleExtractor
{
    public Article? Extract(HtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        (string region, string regionTag) = FindRegion(page.Html);
        region = CommentPattern().Replace(region, "");
        string[] removed = ["script", "style", "noscript", "template", "svg", "figure", "aside", "nav", "form", "button", "header", "footer", regionTag];
        foreach (string element in removed)
        {
            region = HtmlElements.Remove(region, element);
        }

        var candidates = BlockPattern().Matches(region)
            .Select(match => (Tag: match.Groups["tag"].Value.ToLowerInvariant(), Class: ClassOf(match.Groups["attributes"].Value), Html: match.Groups["inner"].Value))
            .ToList();

        string? dominantClass = candidates
            .Where(candidate => candidate.Tag == "p")
            .GroupBy(candidate => candidate.Class)
            .OrderByDescending(group => group.Sum(candidate => TextTools.StripTags(candidate.Html).Length))
            .Select(group => group.Key)
            .FirstOrDefault();

        List<ArticleBlock> body = [.. candidates
            .Where(candidate => candidate.Tag != "p" || candidate.Class == dominantClass)
            .Select(candidate => ToBlock(candidate.Tag, candidate.Html, page.Metadata.Url))
            .OfType<ArticleBlock>()];

        return FromMetadata(page.Metadata, body, ExtractionSource.RenderedHtml);
    }

    internal static Article? FromMetadata(PageMetadata metadata, IReadOnlyList<ArticleBlock> body, ExtractionSource source) =>
        body.OfType<Paragraph>().Any()
            ? new Article(metadata.Headline ?? "", metadata.Summary, metadata.Authors, metadata.Published, metadata.Modified, metadata.Url, metadata.Sections, null, body, source)
            : null;

    private static (string Region, string Tag) FindRegion(string html)
    {
        Regex[] starts = [ArticleBodySectionPattern(), ArticleTagPattern(), BodyTagPattern()];

        foreach (Regex start in starts)
        {
            if (start.Match(html) is { Success: true } match)
            {
                string tag = match.Groups["tag"].Value.ToLowerInvariant();
                return (HtmlElements.Content(html, match.Index, tag), tag);
            }
        }

        return (html, "body");
    }

    private static string? ClassOf(string attributes) =>
        ClassPattern().Match(attributes) is { Success: true } match ? match.Groups["value"].Value : null;

    private static ArticleBlock? ToBlock(string tag, string html, Uri? baseUri)
    {
        if (tag == "p")
        {
            var paragraph = new Paragraph(ReadInlines(html, baseUri));
            return paragraph.Text.Length > 0 ? paragraph : null;
        }

        string text = TextTools.StripTags(html);
        if (text.Length == 0)
        {
            return null;
        }

        return tag == "blockquote" ? new Quote(text) : new Heading(tag[1] - '0', text);
    }

    private static List<Inline> ReadInlines(string html, Uri? baseUri)
    {
        var inlines = new List<Inline>();
        int position = 0;

        foreach (Match anchor in AnchorPattern().Matches(html))
        {
            AddText(inlines, html[position..anchor.Index], null);
            AddText(inlines, anchor.Groups["inner"].Value, HtmlPage.ParseUri(WebUtility.HtmlDecode(anchor.Groups["href"].Value), baseUri));
            position = anchor.Index + anchor.Length;
        }

        AddText(inlines, html[position..], null);
        return inlines;
    }

    private static void AddText(List<Inline> inlines, string html, Uri? link)
    {
        string text = WebUtility.HtmlDecode(InlineTagPattern().Replace(html, ""));
        if (text.Length > 0)
        {
            inlines.Add(new Inline(text, link));
        }
    }

    [GeneratedRegex(@"<(?<tag>section)\b[^>]*\bname\s*=\s*[""']articleBody[""'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleBodySectionPattern();

    [GeneratedRegex(@"<(?<tag>article)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleTagPattern();

    [GeneratedRegex(@"<(?<tag>body)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyTagPattern();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"<(?<tag>p|h[2-6]|blockquote)\b(?<attributes>[^>]*)>(?<inner>.*?)</\k<tag>\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    [GeneratedRegex(@"\bclass\s*=\s*[""'](?<value>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"<a\b[^>]*?\bhref\s*=\s*[""'](?<href>[^""']*)[""'][^>]*>(?<inner>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorPattern();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex InlineTagPattern();
}

public static class HtmlElements
{
    private static readonly ConcurrentDictionary<(string Pattern, RegexOptions Options), Regex> Cache = new();

    public static string Content(string html, int startTagIndex, string tag)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrEmpty(tag);

        int openEnd = html.IndexOf('>', startTagIndex);
        if (openEnd < 0)
        {
            return "";
        }

        int contentStart = openEnd + 1;
        return FindClose(html, contentStart, tag) is int close ? html[contentStart..close] : html[contentStart..];
    }

    public static string Remove(string html, string tag)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrEmpty(tag);

        var builder = new StringBuilder(html.Length);
        int position = 0;
        Regex open = Compile($@"<{Regex.Escape(tag)}\b[^>]*>");

        while (open.Match(html, position) is { Success: true } match)
        {
            builder.Append(html, position, match.Index - position);

            if (match.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                position = match.Index + match.Length;
                continue;
            }

            if (FindClose(html, match.Index + match.Length, tag) is not int close)
            {
                return builder.ToString();
            }

            position = html.IndexOf('>', close) + 1;
        }

        builder.Append(html, position, html.Length - position);
        return builder.ToString();
    }

    private static int? FindClose(string html, int from, string tag)
    {
        Regex pattern = Compile($@"<(?<close>/)?{Regex.Escape(tag)}\b[^>]*>");
        int depth = 1;

        foreach (Match match in pattern.Matches(html, from))
        {
            if (match.Groups["close"].Success)
            {
                depth--;
                if (depth == 0)
                {
                    return match.Index;
                }
            }
            else if (!match.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                depth++;
            }
        }

        return null;
    }

    private static Regex Compile(string pattern) =>
        Cache.GetOrAdd((pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), static key => new Regex(key.Pattern, key.Options));
}

public sealed class CompositeArticleExtractor(IEnumerable<IArticleExtractor> extractors) : IArticleExtractor
{
    private readonly IReadOnlyList<IArticleExtractor> _extractors = [.. extractors ?? throw new ArgumentNullException(nameof(extractors))];

    public static CompositeArticleExtractor Default => new([new PreloadedDataExtractor(), new JsonLdExtractor(), new RenderedHtmlExtractor()]);

    public Article? Extract(HtmlPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return _extractors.Select(extractor => extractor.Extract(page)).FirstOrDefault(article => article is not null);
    }
}

public sealed class PlainTextFormatter(FormatOptions? options = null) : IArticleFormatter
{
    private readonly FormatOptions _options = options ?? FormatOptions.Default;

    public string Format(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);

        var sections = new List<string>();

        if (_options.IncludeMetadata)
        {
            string header = string.Join('\n', new[]
            {
                article.Headline,
                article.Summary,
                article.Byline,
                article.Published is { } published ? "Published " + ArticleFormatting.Timestamp(published) : null,
                article.Url?.AbsoluteUri,
            }.Where(line => !string.IsNullOrWhiteSpace(line)));

            if (header.Length > 0)
            {
                sections.Add(header);
            }
        }

        sections.AddRange(article.Body.Select(Render).OfType<string>());
        return string.Join("\n\n", sections);
    }

    private string? Render(ArticleBlock block) => block switch
    {
        Paragraph paragraph => paragraph.Text,
        Heading heading => heading.Text,
        Quote quote => quote.Text,
        ItemList list => string.Join('\n', list.Items.Select((item, index) => (list.Ordered ? $"{index + 1}. " : "- ") + item)),
        Note note => note.Text,
        Figure figure when _options.IncludeFigures => "[Image] " + ArticleFormatting.Caption(figure),
        Graphic graphic when _options.IncludeGraphics => "[Graphic] " + ArticleFormatting.Describe(graphic),
        _ => null,
    };
}

public sealed class MarkdownFormatter(FormatOptions? options = null) : IArticleFormatter
{
    private readonly FormatOptions _options = options ?? FormatOptions.Default;

    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\\' or '*' or '_' or '[' or ']' or '`' or '<')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    public string Format(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);

        var sections = new List<string>();

        if (_options.IncludeMetadata)
        {
            if (article.Headline.Length > 0)
            {
                sections.Add("# " + Escape(article.Headline));
            }

            if (!string.IsNullOrWhiteSpace(article.Summary))
            {
                sections.Add("_" + Escape(article.Summary) + "_");
            }

            string details = string.Join("  \n", new[]
            {
                article.Byline.Length > 0 ? "**" + Escape(article.Byline) + "**" : null,
                article.Published is { } published ? "Published " + ArticleFormatting.Timestamp(published) : null,
                article.Url is { } url ? $"<{url.AbsoluteUri}>" : null,
            }.OfType<string>());

            if (details.Length > 0)
            {
                sections.Add(details);
            }
        }

        sections.AddRange(article.Body.Select(Render).OfType<string>());
        return string.Join("\n\n", sections);
    }

    private static string RenderInlines(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (Inline inline in paragraph.Inlines)
        {
            string text = TextTools.Normalize(inline.Text);
            bool leading = inline.Text.Length > 0 && char.IsWhiteSpace(inline.Text[0]);
            bool trailing = inline.Text.Length > 0 && char.IsWhiteSpace(inline.Text[^1]);

            if (leading)
            {
                builder.Append(' ');
            }

            builder.Append(inline.Link is { } link && text.Length > 0 ? $"[{Escape(text)}]({link.AbsoluteUri})" : Escape(text));

            if (trailing)
            {
                builder.Append(' ');
            }
        }

        return TextTools.Normalize(builder.ToString());
    }

    private string? Render(ArticleBlock block) => block switch
    {
        Paragraph paragraph => RenderInlines(paragraph),
        Heading heading => new string('#', Math.Clamp(heading.Level, 2, 6)) + " " + Escape(heading.Text),
        Quote quote => "> " + Escape(quote.Text),
        ItemList list => string.Join('\n', list.Items.Select((item, index) => (list.Ordered ? $"{index + 1}. " : "- ") + Escape(item))),
        Note note => "_" + Escape(note.Text) + "_",
        Figure figure when _options.IncludeFigures => "> _" + Escape(ArticleFormatting.Caption(figure)) + "_",
        Graphic graphic when _options.IncludeGraphics => "> **Graphic:** " + Escape(ArticleFormatting.Describe(graphic)),
        _ => null,
    };
}

internal static class ArticleFormatting
{
    public static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Caption(Figure figure) => (figure.Caption, figure.Credit) switch
    {
        ({ } caption, { } credit) => $"{caption} ({credit})",
        ({ } caption, null) => caption,
        (null, { } credit) => $"({credit})",
        _ => "",
    };

    public static string Describe(Graphic graphic) =>
        string.Join(" ", new[] { graphic.Title, graphic.Description, graphic.Note, graphic.Source }
            .OfType<string>()
            .Select(part => part[^1] is '.' or '?' or '!' ? part : part + "."));
}

public sealed class LeadSummarizer : ISummarizer
{
    public LeadSummarizer(int maxWords = 80)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWords, 1);
        MaxWords = maxWords;
    }

    public int MaxWords { get; }

    public string Summarize(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);

        var chosen = new List<string>();
        int words = 0;

        foreach (string sentence in article.Paragraphs.SelectMany(paragraph => SentenceSplitter.Split(paragraph.Text)))
        {
            int count = TextTools.CountWords(sentence);
            if (chosen.Count > 0 && words + count > MaxWords)
            {
                break;
            }

            chosen.Add(sentence);
            words += count;
        }

        return string.Join(' ', chosen);
    }
}

public sealed class ExtractiveSummarizer : ISummarizer
{
    private const int MinimumSentenceWords = 6;
    private const double DuplicateThreshold = 0.5;

    public ExtractiveSummarizer(int maxSentences = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSentences, 1);
        MaxSentences = maxSentences;
    }

    public int MaxSentences { get; }

    public static IReadOnlyList<string> ContentWords(string text) =>
        [.. TextTools.Words(text).Where(word => word.Length > 2 && !IsStopWord(word))];

    public string Summarize(Article article)
    {
        ArgumentNullException.ThrowIfNull(article);

        var sentences = article.Paragraphs
            .SelectMany(paragraph => SentenceSplitter.Split(paragraph.Text))
            .Select((text, index) => (Text: text, Index: index, Words: ContentWords(text)))
            .ToList();

        if (sentences.Count == 0)
        {
            return "";
        }

        Dictionary<string, int> frequency = sentences
            .SelectMany(sentence => sentence.Words)
            .CountBy(word => word)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        HashSet<string> topicWords = [.. ContentWords(article.Headline + " " + article.Summary)];

        var ranked = sentences
            .Where(sentence => sentence.Words.Count > 0 && (TextTools.CountWords(sentence.Text) >= MinimumSentenceWords || sentences.Count == 1))
            .Select(sentence => (sentence.Text, sentence.Index, Terms: sentence.Words.ToHashSet(StringComparer.Ordinal), Score: Score(sentence.Words, sentence.Index, frequency, topicWords)))
            .OrderByDescending(sentence => sentence.Score);

        var chosen = new List<(string Text, int Index, HashSet<string> Terms)>();
        foreach (var candidate in ranked)
        {
            if (chosen.Count == MaxSentences)
            {
                break;
            }

            if (chosen.All(existing => Similarity(existing.Terms, candidate.Terms) < DuplicateThreshold))
            {
                chosen.Add((candidate.Text, candidate.Index, candidate.Terms));
            }
        }

        if (chosen.Count == 0)
        {
            return sentences[0].Text;
        }

        return string.Join(' ', chosen.OrderBy(sentence => sentence.Index).Select(sentence => sentence.Text));
    }

    private static double Score(IReadOnlyList<string> words, int index, Dictionary<string, int> frequency, HashSet<string> topicWords)
    {
        double weight = words.Distinct(StringComparer.Ordinal).Sum(word => frequency[word] * (topicWords.Contains(word) ? 2.0 : 1.0));
        double position = 1.0 + 1.0 / (1 + index);
        return weight / Math.Sqrt(words.Count) * position;
    }

    private static double Similarity(HashSet<string> left, HashSet<string> right)
    {
        int shared = left.Count(right.Contains);
        return (double)shared / (left.Count + right.Count - shared);
    }

    private static bool IsStopWord(string word) => word is
        "about" or "above" or "after" or "again" or "against" or "all" or "also" or "and" or "any" or "are"
        or "because" or "been" or "before" or "being" or "below" or "between" or "both" or "but" or "can" or "could"
        or "did" or "does" or "doing" or "down" or "during" or "each" or "even" or "few" or "for" or "from"
        or "further" or "had" or "has" or "have" or "having" or "her" or "here" or "hers" or "herself" or "him"
        or "himself" or "his" or "how" or "into" or "its" or "itself" or "just" or "last" or "like" or "made"
        or "make" or "many" or "may" or "more" or "most" or "mrs" or "much" or "myself" or "new" or "nor"
        or "not" or "now" or "off" or "once" or "one" or "only" or "other" or "our" or "ours" or "ourselves"
        or "out" or "over" or "own" or "said" or "same" or "say" or "says" or "she" or "should" or "since"
        or "some" or "still" or "such" or "than" or "that" or "the" or "their" or "theirs" or "them" or "themselves"
        or "then" or "there" or "these" or "they" or "this" or "those" or "through" or "too" or "two" or "under"
        or "until" or "very" or "was" or "were" or "what" or "when" or "where" or "which" or "while" or "who"
        or "whom" or "why" or "will" or "with" or "would" or "year" or "years" or "yet" or "you" or "your"
        or "yours" or "yourself" or "yourselves" or "it's" or "don't" or "didn't" or "that's" or "he's" or "she's" or "they're";
}

public sealed class ArticleProcessor(IArticleExtractor extractor, IArticleFormatter formatter, ISummarizer summarizer)
{
    private readonly IArticleExtractor _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
    private readonly IArticleFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly ISummarizer _summarizer = summarizer ?? throw new ArgumentNullException(nameof(summarizer));

    public static ArticleProcessor Default => new(CompositeArticleExtractor.Default, new PlainTextFormatter(), new ExtractiveSummarizer());

    public ArticleDigest Process(string html)
    {
        Article article = _extractor.Extract(HtmlPage.Parse(html))
            ?? throw new ArticleExtractionException("No article content was found in the HTML.");

        return new ArticleDigest(article, _formatter.Format(article), _summarizer.Summarize(article));
    }

    public async Task<ArticleDigest> ProcessAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return Process(html);
    }

    public async Task<ArticleDigest> ProcessAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await ProcessAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArticleDigest> ProcessFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string html = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        try
        {
            return Process(html);
        }
        catch (ArticleExtractionException exception)
        {
            throw new ArticleExtractionException($"{path}: {exception.Message}", exception);
        }
    }

    public async Task<IReadOnlyList<ArticleDigest>> ProcessFilesAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return await Task.WhenAll(paths.Select(path => ProcessFileAsync(path, cancellationToken))).ConfigureAwait(false);
    }
}

public static class ArticleFiles
{
    public static IReadOnlyList<string> Resolve(IEnumerable<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        return [.. inputs.SelectMany(Expand)];
    }

    private static IEnumerable<string> Expand(string input)
    {
        if (Directory.Exists(input))
        {
            return Directory
                .EnumerateFiles(input, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal);
        }

        return File.Exists(input) ? [input] : throw new FileNotFoundException($"No file or directory exists at '{input}'.", input);
    }
}

public static class DigestReport
{
    public const int NameWidth = 60;

    public static string Render(string path, ArticleDigest digest)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(digest);

        Article article = digest.Article;
        string words = article.ReportedWordCount is { } reported ? $"{article.WordCount}/{reported}" : $"{article.WordCount}";
        string rule = new('=', NameWidth + 20);

        return string.Join('\n',
            rule,
            new FilenameString(Path.GetFileName(path)).Truncate(NameWidth),
            $"Source: {article.Source}  Paragraphs: {article.Paragraphs.Count()}  Words: {words}",
            rule,
            "",
            "SUMMARY",
            "",
            digest.Summary,
            "",
            "ARTICLE",
            "",
            digest.Prose,
            "");
    }
}

using System.Text;
using CSharpClassLibrary;
using Xunit;

namespace CSharpUnitTests;

internal static class Fixtures
{
    public static string PreloadedPage(string article, string root = "loaderData", string trailer = "") =>
        $$$"""<html><head><title>Page Title</title></head><body><script>window.__preloadedData = {"{{{root}}}":{"data":{"article":{{{article}}}}},"errors":undefined}{{{trailer}}};</script></body></html>""";

    public static string ArticleJson(string blocks, string extra = "") =>
        $$"""
        {
          "headline": {"default": "Display Headline", "seo": "SEO Headline"},
          "summary": "The dek.",
          "url": "https://www.example.com/2026/01/01/story.html",
          "firstPublished": "2026-01-01T12:30:00.000Z",
          "lastModified": "2026-01-02T08:00:00.000Z",
          "wordCount": 42,
          "section": {"displayName": "U.S.", "name": "us"},
          "subsection": {"displayName": "Politics", "name": "politics"},
          "bylines": [{"creators": [{"displayName": "Ada Lovelace"}, {"displayName": "Alan Turing"}], "renderedRepresentation": "By Ada Lovelace and Alan Turing"}],
          "sprinkledBody": {"content": [{{blocks}}]}{{extra}}
        }
        """;

    public static string ParagraphJson(params string[] texts)
    {
        string inlines = string.Join(',', texts.Select(text => $$"""{"__typename":"TextInline","text":"{{text}}","formats":[]}"""));
        return $$"""{"__typename":"ParagraphBlock","content":[{{inlines}}]}""";
    }

    public static Article Article(params ArticleBlock[] body) =>
        new("Headline", "Summary text.", ["Ada Lovelace"], new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero), null, new Uri("https://www.example.com/story"), ["U.S."], null, body, ExtractionSource.PreloadedData);

    public static Article ArticleOf(params string[] paragraphs) =>
        Article([.. paragraphs.Select(text => new Paragraph(text))]);

    public static string SamplePath(string relative) =>
        Path.Combine([AppContext.BaseDirectory, "nytimes", .. relative.Split('/')]);
}

public class TextToolsTests
{
    [Theory]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("a\u00A0b", "a b")]
    [InlineData("a\tb\r\nc", "a b c")]
    [InlineData("zero\u200Bwidth", "zerowidth")]
    [InlineData("soft\u00ADhyphen", "softhyphen")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_CollapsesWhitespaceAndRemovesInvisibleCharacters(string input, string expected)
    {
        Assert.Equal(expected, TextTools.Normalize(input));
    }

    [Fact]
    public void Normalize_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TextTools.Normalize(null!));
    }

    [Theory]
    [InlineData("<p>Hello <b>there</b></p>", "Hello there")]
    [InlineData("Tom &amp; Jerry &#8217;s &quot;show&quot;", "Tom & Jerry \u2019s \"show\"")]
    [InlineData("one<br>two<br/>three", "one two three")]
    [InlineData("<p>first</p><p>second</p>", "first second")]
    [InlineData("plain", "plain")]
    public void StripTags_RemovesMarkupAndDecodesEntities(string input, string expected)
    {
        Assert.Equal(expected, TextTools.StripTags(input));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one two  three", 3)]
    [InlineData("well \u2014 then", 2)]
    [InlineData("It\u2019s 10 o\u2019clock.", 3)]
    public void CountWords_CountsTokensContainingLettersOrDigits(string input, int expected)
    {
        Assert.Equal(expected, TextTools.CountWords(input));
    }

    [Fact]
    public void Words_LowercasesAndNormalizesApostrophes()
    {
        Assert.Equal(new[] { "don't", "stop", "re-election", "2026" }, TextTools.Words("Don\u2019t STOP re-election, 2026!"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("A", "A")]
    [InlineData("A|B", "A and B")]
    [InlineData("A|B|C", "A, B and C")]
    public void JoinNames_UsesNewspaperStyle(string names, string expected)
    {
        string[] list = names.Length == 0 ? [] : names.Split('|');
        Assert.Equal(expected, TextTools.JoinNames(list));
    }
}

public class ScriptDataTests
{
    private const string Name = "window.__data";

    [Fact]
    public void TryExtract_ReturnsTheAssignedObject()
    {
        Assert.True(ScriptData.TryExtractAssignedObject("""x=1; window.__data = {"a":1}; y=2;""", Name, out string? json));
        Assert.Equal("""{"a":1}""", json);
    }

    [Fact]
    public void TryExtract_ReturnsAnAssignedArray()
    {
        Assert.True(ScriptData.TryExtractAssignedObject("window.__data=[1,[2,3]];", Name, out string? json));
        Assert.Equal("[1,[2,3]]", json);
    }

    [Fact]
    public void TryExtract_ReplacesBareUndefinedWithNull()
    {
        Assert.True(ScriptData.TryExtractAssignedObject("""window.__data = {"a":undefined,"b":[undefined]};""", Name, out string? json));
        Assert.Equal("""{"a":null,"b":[null]}""", json);
    }

    [Fact]
    public void TryExtract_LeavesUndefinedInsideStringsAndIdentifiersAlone()
    {
        Assert.True(ScriptData.TryExtractAssignedObject("""window.__data = {"a":"undefined","undefinedKey":1};""", Name, out string? json));
        Assert.Equal("""{"a":"undefined","undefinedKey":1}""", json);
    }

    [Fact]
    public void TryExtract_IgnoresBracesAndEscapedQuotesInsideStrings()
    {
        const string Literal = """{"a":"} { \" ] [","b":"\\"}""";
        Assert.True(ScriptData.TryExtractAssignedObject($"window.__data = {Literal};", Name, out string? json));
        Assert.Equal(Literal, json);
    }

    [Fact]
    public void TryExtract_SkipsComparisonsAndPropertyAccess()
    {
        const string Source = """if (window.__data == null) {} window.__data.x; window.__data = {"ok":true};""";
        Assert.True(ScriptData.TryExtractAssignedObject(Source, Name, out string? json));
        Assert.Equal("""{"ok":true}""", json);
    }

    [Fact]
    public void TryExtract_SkipsLongerIdentifiersThatEndWithTheName()
    {
        const string Source = """mywindow.__data = {"wrong":1}; window.__data = {"right":1};""";
        Assert.True(ScriptData.TryExtractAssignedObject(Source, Name, out string? json));
        Assert.Equal("""{"right":1}""", json);
    }

    [Theory]
    [InlineData("nothing here")]
    [InlineData("window.__data = 5;")]
    [InlineData("window.__data = {\"a\":1")]
    [InlineData("window.__data")]
    public void TryExtract_WhenNoCompleteLiteral_ReturnsFalse(string source)
    {
        Assert.False(ScriptData.TryExtractAssignedObject(source, Name, out string? json));
        Assert.Null(json);
    }

    [Fact]
    public void TryExtract_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ScriptData.TryExtractAssignedObject(null!, Name, out _));
        Assert.Throws<ArgumentException>(() => ScriptData.TryExtractAssignedObject("x", "", out _));
    }
}

public class SentenceSplitterTests
{
    [Fact]
    public void Split_SeparatesSimpleSentences()
    {
        Assert.Equal(new[] { "One here.", "Two here!", "Three here?" }, SentenceSplitter.Split("One here. Two here! Three here?"));
    }

    [Theory]
    [InlineData("Mr. Brown spoke. He left.", 2)]
    [InlineData("Sen. Jon Husted of Ohio spoke. Gov. Smith agreed.", 2)]
    [InlineData("Reid J. Epstein wrote it. It ran.", 2)]
    [InlineData("It rose 2.5 percent. Then it fell.", 2)]
    [InlineData("The U.S. Navy said so. Others agreed.", 2)]
    [InlineData("Wait for it\u2026 Then go.", 2)]
    [InlineData("No terminal punctuation", 1)]
    [InlineData("", 0)]
    public void Split_HandlesAbbreviationsInitialsAndNumbers(string text, int expected)
    {
        Assert.Equal(expected, SentenceSplitter.Split(text).Count);
    }

    [Fact]
    public void Split_KeepsClosingQuotesWithTheirSentence()
    {
        Assert.Equal(new[] { "\u201CIs it him?\u201D she asked.", "\u201CYes.\u201D" }, SentenceSplitter.Split("\u201CIs it him?\u201D she asked. \u201CYes.\u201D"));
    }

    [Fact]
    public void Split_DoesNotBreakBeforeLowercaseContinuation()
    {
        Assert.Single(SentenceSplitter.Split("\u201CReally?\u201D she asked again."));
    }

    [Fact]
    public void Split_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SentenceSplitter.Split(null!));
    }
}

public class HtmlElementsTests
{
    [Fact]
    public void Content_ReturnsInnerHtmlRespectingNesting()
    {
        const string Html = "<section id=a><section>inner</section>tail</section>after";
        Assert.Equal("<section>inner</section>tail", HtmlElements.Content(Html, 0, "section"));
    }

    [Fact]
    public void Content_WhenUnclosed_ReturnsRemainder()
    {
        Assert.Equal("open", HtmlElements.Content("<div>open", 0, "div"));
    }

    [Fact]
    public void Remove_DropsElementsIncludingNestedOnes()
    {
        Assert.Equal("ab", HtmlElements.Remove("a<figure><figure>x</figure>y</figure>b", "figure"));
    }

    [Fact]
    public void Remove_HandlesSelfClosingAndCaseInsensitiveTags()
    {
        Assert.Equal("a b c", HtmlElements.Remove("a <SVG/>b <svg>x</SVG>c", "svg"));
    }

    [Fact]
    public void Remove_DoesNotMatchLongerTagNames()
    {
        Assert.Equal("<pre>x</pre>", HtmlElements.Remove("<pre>x</pre>", "p"));
    }
}

public class HtmlPageTests
{
    private const string JsonLdPage = """
        <html><head>
        <title>Fallback &amp; Title</title>
        <meta name="description" content="Meta description">
        <script type="application/ld+json">{"@type":"NewsArticle","headline":"LD Headline","description":"LD description","author":[{"@type":"Person","name":"Grace Hopper"},{"name":"Linus"}],"datePublished":"2026-03-04T05:06:07Z","dateModified":"2026-03-05T00:00:00Z","url":"https://example.com/a","articleSection":"World, Europe","articleBody":"First.\nSecond."}</script>
        </head></html>
        """;

    [Fact]
    public void Parse_ReadsJsonLdArticle()
    {
        PageMetadata metadata = HtmlPage.Parse(JsonLdPage).Metadata;

        Assert.Equal("LD Headline", metadata.Headline);
        Assert.Equal("LD description", metadata.Summary);
        Assert.Equal(new[] { "Grace Hopper", "Linus" }, metadata.Authors);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), metadata.Published);
        Assert.Equal(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), metadata.Modified);
        Assert.Equal(new Uri("https://example.com/a"), metadata.Url);
        Assert.Equal(new[] { "World", "Europe" }, metadata.Sections);
        Assert.Equal("First.\nSecond.", metadata.ArticleBody);
    }

    [Fact]
    public void Parse_FindsArticleInsideGraphAndPrefersAlternativeHeadline()
    {
        const string Html = """<script type="application/ld+json">{"@graph":[{"@type":"WebSite","name":"Site"},{"@type":["Thing","ReportageNewsArticle"],"headline":"SEO","alternativeHeadline":"Display","author":{"name":"Solo"}}]}</script>""";
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal("Display", metadata.Headline);
        Assert.Equal(new[] { "Solo" }, metadata.Authors);
    }

    [Fact]
    public void Parse_SkipsMalformedJsonLd()
    {
        const string Html = """<script type="application/ld+json">{not json</script><script type="application/ld+json">{"@type":"Article","headline":"Good"}</script>""";
        Assert.Equal("Good", HtmlPage.Parse(Html).Metadata.Headline);
    }

    [Fact]
    public void Parse_FallsBackToMetaTags()
    {
        const string Html = """
            <meta property="og:title" content="OG &amp; Title"/>
            <meta name="description" content="Desc"/>
            <meta name="byl" content="By Ann Lee, Bo Kim and Cy Young"/>
            <meta property="article:published_time" content="2026-09-28T16:31:46.000Z"/>
            <meta property="article:section" content="Business"/>
            <meta property="og:url" content="https://example.com/b"/>
            """;
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal("OG & Title", metadata.Headline);
        Assert.Equal("Desc", metadata.Summary);
        Assert.Equal(new[] { "Ann Lee", "Bo Kim", "Cy Young" }, metadata.Authors);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 16, 31, 46, TimeSpan.Zero), metadata.Published);
        Assert.Equal(new[] { "Business" }, metadata.Sections);
        Assert.Equal(new Uri("https://example.com/b"), metadata.Url);
    }

    [Fact]
    public void Parse_UsesTitleWhenNothingElseExists()
    {
        Assert.Equal("Only Title", HtmlPage.Parse("<title> Only  Title </title>").Metadata.Headline);
    }

    [Fact]
    public void Parse_EmptyDocumentHasEmptyMetadata()
    {
        PageMetadata metadata = HtmlPage.Parse("").Metadata;

        Assert.Null(metadata.Headline);
        Assert.Null(metadata.Summary);
        Assert.Empty(metadata.Authors);
        Assert.Null(metadata.Published);
        Assert.Null(metadata.Modified);
        Assert.Null(metadata.Url);
        Assert.Empty(metadata.Sections);
        Assert.Null(metadata.ArticleBody);
    }

    [Fact]
    public void Parse_KeepsOriginalHtml()
    {
        Assert.Equal("<p>x</p>", HtmlPage.Parse("<p>x</p>").Html);
    }

    [Fact]
    public void Parse_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HtmlPage.Parse(null!));
    }
}

public class PreloadedDataExtractorTests
{
    private static Article Extract(string blocks, string extra = "", string root = "loaderData")
    {
        Article? article = new PreloadedDataExtractor().Extract(HtmlPage.Parse(Fixtures.PreloadedPage(Fixtures.ArticleJson(blocks, extra), root)));
        Assert.NotNull(article);
        return article;
    }

    [Fact]
    public void Extract_ReadsMetadata()
    {
        Article article = Extract(Fixtures.ParagraphJson("Body."));

        Assert.Equal("Display Headline", article.Headline);
        Assert.Equal("The dek.", article.Summary);
        Assert.Equal(new[] { "Ada Lovelace", "Alan Turing" }, article.Authors);
        Assert.Equal("By Ada Lovelace and Alan Turing", article.Byline);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero), article.Published);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero), article.Modified);
        Assert.Equal(new Uri("https://www.example.com/2026/01/01/story.html"), article.Url);
        Assert.Equal(new[] { "U.S.", "Politics" }, article.Sections);
        Assert.Equal(42, article.ReportedWordCount);
        Assert.Equal(ExtractionSource.PreloadedData, article.Source);
    }

    [Fact]
    public void Extract_JoinsSplitTextFragmentsIntoOneParagraph()
    {
        Article article = Extract(Fixtures.ParagraphJson("Split ", "into ", " pieces."));
        Assert.Equal("Split into pieces.", Assert.Single(article.Paragraphs).Text);
    }

    [Fact]
    public void Extract_SkipsDropzonesAndUnknownBlocks()
    {
        Article article = Extract($$"""{"__typename":"Dropzone","index":0},{{Fixtures.ParagraphJson("Kept.")}},{"__typename":"MysteryBlock","content":[{"text":"Hidden"}]}""");
        Assert.IsType<Paragraph>(Assert.Single(article.Body));
    }

    [Fact]
    public void Extract_KeepsLinksAndMergesAdjacentRunsWithTheSameLink()
    {
        const string Block = """{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"See ","formats":[]},{"__typename":"TextInline","text":"the ","formats":[{"__typename":"LinkFormat","url":"/2026/a.html"}]},{"__typename":"TextInline","text":"polls","formats":[{"__typename":"LinkFormat","url":"/2026/a.html"}]},{"__typename":"TextInline","text":".","formats":[]}]}""";
        Paragraph paragraph = Assert.Single(Extract(Block).Paragraphs);

        Assert.Equal("See the polls.", paragraph.Text);
        Assert.Equal(3, paragraph.Inlines.Count);
        Inline link = Assert.Single(paragraph.Links);
        Assert.Equal("the polls", link.Text);
        Assert.Equal(new Uri("https://www.example.com/2026/a.html"), link.Link);
    }

    [Fact]
    public void Extract_ReadsHeadingsListsQuotesNotesFiguresAndGraphics()
    {
        const string Blocks = """
            {"__typename":"HeaderBasicBlock","ledeMedia":{"__typename":"ImageBlock","media":{"caption":{"text":"Lede caption."},"credit":"Lede credit"}}},
            {"__typename":"Heading2Block","content":[{"__typename":"TextInline","text":"Subhead"}]},
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Para."}]},
            {"__typename":"ListBlock","style":"ORDERED","content":[{"__typename":"ListItemBlock","content":[{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"O"},{"__typename":"TextInline","text":"ne"}]}]},{"__typename":"ListItemBlock","content":[{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Two"}]}]}]},
            {"__typename":"BlockquoteBlock","content":[{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Quoted."}]}]},
            {"__typename":"ImageBlock","media":{"legacyHtmlCaption":"<p>Legacy &amp; caption</p>","credit":"Photographer"}},
            {"__typename":"InteractiveBlock","media":{"headline":{"default":"Chart"},"leadin":"Lead","note":"Note","dataSource":"Source: AP"}},
            {"__typename":"DetailBlock","content":[{"__typename":"TextInline","text":"Someone"},{"__typename":"TextInline","text":" contributed reporting."}]}
            """;
        Article article = Extract(Blocks);

        Assert.Collection(
            article.Body,
            block => Assert.Equal(new Figure("Lede caption.", "Lede credit"), block),
            block => Assert.Equal(new Heading(2, "Subhead"), block),
            block => Assert.Equal("Para.", Assert.IsType<Paragraph>(block).Text),
            block =>
            {
                ItemList list = Assert.IsType<ItemList>(block);
                Assert.True(list.Ordered);
                Assert.Equal(new[] { "One", "Two" }, list.Items);
            },
            block => Assert.Equal(new Quote("Quoted."), block),
            block => Assert.Equal(new Figure("Legacy & caption", "Photographer"), block),
            block => Assert.Equal(new Graphic("Chart", "Lead", "Note", "Source: AP"), block),
            block => Assert.Equal(new Note("Someone contributed reporting."), block));
    }

    [Fact]
    public void Extract_SkipsEmptyParagraphsFiguresAndGraphics()
    {
        const string Blocks = """
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"  "}]},
            {"__typename":"ImageBlock","media":{"credit":""}},
            {"__typename":"ImageBlock"},
            {"__typename":"InteractiveBlock","media":{}},
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Real."}]}
            """;
        Assert.IsType<Paragraph>(Assert.Single(Extract(Blocks).Body));
    }

    [Fact]
    public void Extract_TreatsLineBreaksAsSpaces()
    {
        const string Block = """{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"a"},{"__typename":"LineBreakInline"},{"__typename":"TextInline","text":"b"}]}""";
        Assert.Equal("a b", Assert.Single(Extract(Block).Paragraphs).Text);
    }

    [Fact]
    public void Extract_SupportsTheLegacyInitialDataRoot()
    {
        Assert.Equal("Legacy.", Assert.Single(Extract(Fixtures.ParagraphJson("Legacy."), root: "initialData").Paragraphs).Text);
    }

    [Fact]
    public void Extract_FindsTheArticleAnywhereWhenThePathIsUnknown()
    {
        Assert.Equal("Anywhere.", Assert.Single(Extract(Fixtures.ParagraphJson("Anywhere."), root: "somethingElse").Paragraphs).Text);
    }

    [Fact]
    public void Extract_FallsBackToBodyWhenSprinkledBodyIsMissing()
    {
        string json = Fixtures.ArticleJson(Fixtures.ParagraphJson("Sprinkled.")).Replace("sprinkledBody", "body", StringComparison.Ordinal);
        Article? article = new PreloadedDataExtractor().Extract(HtmlPage.Parse(Fixtures.PreloadedPage(json)));
        Assert.NotNull(article);
        Assert.Equal("Sprinkled.", Assert.Single(article.Paragraphs).Text);
    }

    [Fact]
    public void Extract_UsesRenderedBylineWhenCreatorsAreMissing()
    {
        string json = Fixtures.ArticleJson(Fixtures.ParagraphJson("x")).Replace("\"displayName\": \"Ada Lovelace\"", "\"id\": \"1\"", StringComparison.Ordinal).Replace("\"displayName\": \"Alan Turing\"", "\"id\": \"2\"", StringComparison.Ordinal);
        Article? article = new PreloadedDataExtractor().Extract(HtmlPage.Parse(Fixtures.PreloadedPage(json)));
        Assert.NotNull(article);
        Assert.Equal(new[] { "Ada Lovelace", "Alan Turing" }, article.Authors);
    }

    [Fact]
    public void Extract_FallsBackToPageMetadata()
    {
        const string Json = """{"headline":{},"sprinkledBody":{"content":[{"__typename":"ParagraphBlock","content":[{"text":"Only body."}]}]}}""";
        string html = """<meta property="og:title" content="Meta Headline"><meta name="description" content="Meta dek">""" + Fixtures.PreloadedPage(Json);
        Article? article = new PreloadedDataExtractor().Extract(HtmlPage.Parse(html));

        Assert.NotNull(article);
        Assert.Equal("Meta Headline", article.Headline);
        Assert.Equal("Meta dek", article.Summary);
        Assert.Null(article.ReportedWordCount);
        Assert.Empty(article.Authors);
    }

    [Theory]
    [InlineData("<html><body>No script</body></html>")]
    [InlineData("<script>window.__preloadedData = {broken json};</script>")]
    [InlineData("<script>window.__preloadedData = {\"loaderData\":{\"data\":{}}};</script>")]
    [InlineData("<script>window.__preloadedData = {\"loaderData\":{\"data\":{\"article\":{\"headline\":{\"default\":\"H\"},\"sprinkledBody\":{\"content\":[{\"__typename\":\"Dropzone\"}]}}}}};</script>")]
    public void Extract_WhenNoUsableArticle_ReturnsNull(string html)
    {
        Assert.Null(new PreloadedDataExtractor().Extract(HtmlPage.Parse(html)));
    }

    [Fact]
    public void Extract_WhenPageIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PreloadedDataExtractor().Extract(null!));
    }
}

public class JsonLdExtractorTests
{
    [Fact]
    public void Extract_SplitsArticleBodyIntoParagraphs()
    {
        const string Html = """<script type="application/ld+json">{"@type":"NewsArticle","headline":"H","articleBody":"First para.\n\nSecond para.\r\nThird."}</script>""";
        Article? article = new JsonLdExtractor().Extract(HtmlPage.Parse(Html));

        Assert.NotNull(article);
        Assert.Equal(ExtractionSource.JsonLd, article.Source);
        Assert.Equal("H", article.Headline);
        Assert.Equal(new[] { "First para.", "Second para.", "Third." }, article.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Theory]
    [InlineData("<p>no json-ld</p>")]
    [InlineData("<script type=\"application/ld+json\">{\"@type\":\"NewsArticle\",\"headline\":\"H\"}</script>")]
    [InlineData("<script type=\"application/ld+json\">{\"@type\":\"NewsArticle\",\"articleBody\":\"  \"}</script>")]
    public void Extract_WhenNoArticleBody_ReturnsNull(string html)
    {
        Assert.Null(new JsonLdExtractor().Extract(HtmlPage.Parse(html)));
    }
}

public class RenderedHtmlExtractorTests
{
    private static Article Extract(string html)
    {
        Article? article = new RenderedHtmlExtractor().Extract(HtmlPage.Parse(html));
        Assert.NotNull(article);
        return article;
    }

    [Fact]
    public void Extract_ReadsParagraphsFromTheArticleBodySection()
    {
        const string Html = """
            <p class="body">Outside.</p>
            <section name="articleBody"><p class="body">One.</p><h2>Sub</h2><p class="body">Two <a href="/x">link</a>.</p></section>
            <p class="body">After.</p>
            """;
        Article article = Extract(Html);

        Assert.Equal(ExtractionSource.RenderedHtml, article.Source);
        Assert.Collection(
            article.Body,
            block => Assert.Equal("One.", Assert.IsType<Paragraph>(block).Text),
            block => Assert.Equal(new Heading(2, "Sub"), block),
            block => Assert.Equal("Two link.", Assert.IsType<Paragraph>(block).Text));
    }

    [Fact]
    public void Extract_ResolvesRelativeLinksAgainstThePageUrl()
    {
        const string Html = """<meta property="og:url" content="https://example.com/2026/story.html"><article><p>See <a href="/other.html">this</a>.</p></article>""";
        Inline link = Assert.Single(Assert.Single(Extract(Html).Paragraphs).Links);

        Assert.Equal("this", link.Text);
        Assert.Equal(new Uri("https://example.com/other.html"), link.Link);
    }

    [Fact]
    public void Extract_IgnoresScriptsFiguresAsidesAndNestedSections()
    {
        const string Html = """
            <section name="articleBody">
            <script>var p = "<p>script</p>";</script>
            <figure><p>caption</p></figure>
            <aside><p>aside</p></aside>
            <section><p>chart note</p></section>
            <!-- <p>comment</p> -->
            <p>Real text.</p>
            </section>
            """;
        Assert.Equal("Real text.", Assert.Single(Extract(Html).Paragraphs).Text);
    }

    [Fact]
    public void Extract_KeepsOnlyTheDominantParagraphStyle()
    {
        const string Html = """<article><p class="story">A long real paragraph of text.</p><p class="story">Another real paragraph.</p><p class="notice">Loading…</p></article>""";
        Assert.Equal(new[] { "A long real paragraph of text.", "Another real paragraph." }, Extract(Html).Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void Extract_ReadsBlockquotes()
    {
        Assert.Contains<ArticleBlock>(new Quote("Quoted words."), Extract("<body><p>Para.</p><blockquote>Quoted <em>words</em>.</blockquote></body>").Body);
    }

    [Fact]
    public void Extract_FallsBackToTheWholeDocument()
    {
        Assert.Equal("Loose.", Assert.Single(Extract("<p>Loose.</p>").Paragraphs).Text);
    }

    [Fact]
    public void Extract_UsesPageMetadata()
    {
        Article article = Extract("""<title>T</title><meta name="byl" content="By Pat"><p>x</p>""");
        Assert.Equal("T", article.Headline);
        Assert.Equal(new[] { "Pat" }, article.Authors);
        Assert.Null(article.ReportedWordCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<div>No paragraphs</div>")]
    [InlineData("<p>  </p><p><img src=x></p>")]
    public void Extract_WhenNoParagraphs_ReturnsNull(string html)
    {
        Assert.Null(new RenderedHtmlExtractor().Extract(HtmlPage.Parse(html)));
    }
}

public class CompositeArticleExtractorTests
{
    private sealed class StubExtractor(Article? result) : IArticleExtractor
    {
        public int Calls { get; private set; }

        public Article? Extract(HtmlPage page)
        {
            Calls++;
            return result;
        }
    }

    [Fact]
    public void Extract_ReturnsTheFirstNonNullResultAndStops()
    {
        Article expected = Fixtures.ArticleOf("x");
        var first = new StubExtractor(null);
        var second = new StubExtractor(expected);
        var third = new StubExtractor(Fixtures.ArticleOf("y"));

        Assert.Same(expected, new CompositeArticleExtractor([first, second, third]).Extract(HtmlPage.Parse("")));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(0, third.Calls);
    }

    [Fact]
    public void Extract_WithNoExtractors_ReturnsNull()
    {
        Assert.Null(new CompositeArticleExtractor([]).Extract(HtmlPage.Parse("<p>x</p>")));
    }

    [Fact]
    public void Constructor_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeArticleExtractor(null!));
    }

    [Fact]
    public void Default_PrefersPreloadedDataOverRenderedHtml()
    {
        string html = "<section name=\"articleBody\"><p>Rendered.</p></section>" + Fixtures.PreloadedPage(Fixtures.ArticleJson(Fixtures.ParagraphJson("Preloaded.")));
        Article? article = CompositeArticleExtractor.Default.Extract(HtmlPage.Parse(html));

        Assert.NotNull(article);
        Assert.Equal(ExtractionSource.PreloadedData, article.Source);
    }

    [Fact]
    public void Default_FallsBackToRenderedHtml()
    {
        Article? article = CompositeArticleExtractor.Default.Extract(HtmlPage.Parse("<p>Rendered.</p>"));

        Assert.NotNull(article);
        Assert.Equal(ExtractionSource.RenderedHtml, article.Source);
    }
}

public class ArticleTests
{
    [Fact]
    public void WordCount_CountsReadableTextButNotCaptions()
    {
        Article article = Fixtures.Article(
            new Paragraph("one two three"),
            new Heading(2, "four"),
            new Quote("five six"),
            new ItemList(["seven", "eight nine"], Ordered: false),
            new Figure("not counted", "credit"),
            new Graphic("not", "counted", null, null),
            new Note("not counted"));

        Assert.Equal(9, article.WordCount);
    }

    [Fact]
    public void Byline_IsEmptyWithoutAuthors()
    {
        Article article = Fixtures.ArticleOf("x") with { Authors = [] };
        Assert.Equal("", article.Byline);
    }

    [Fact]
    public void Paragraph_TextNormalizesAcrossInlines()
    {
        var paragraph = new Paragraph([new Inline("  a "), new Inline(" b", new Uri("https://x.test/")), new Inline("\u00A0c  ")]);
        Assert.Equal("a b c", paragraph.Text);
    }
}

public class PlainTextFormatterTests
{
    [Fact]
    public void Format_WritesHeaderThenBlankLineSeparatedBlocks()
    {
        Article article = Fixtures.Article(new Paragraph("First."), new Heading(2, "Sub"), new Paragraph("Second."), new Note("Credit line."));

        string expected = string.Join('\n',
            "Headline",
            "Summary text.",
            "By Ada Lovelace",
            "Published 2026-01-01 12:30 UTC",
            "https://www.example.com/story",
            "",
            "First.",
            "",
            "Sub",
            "",
            "Second.",
            "",
            "Credit line.");

        Assert.Equal(expected, new PlainTextFormatter().Format(article));
    }

    [Fact]
    public void Format_OmitsFiguresAndGraphicsByDefault()
    {
        Article article = Fixtures.Article(new Figure("Caption", "Credit"), new Paragraph("Body."), new Graphic("Chart", null, null, null));
        Assert.Equal("Body.", new PlainTextFormatter(new FormatOptions(IncludeMetadata: false)).Format(article));
    }

    [Fact]
    public void Format_IncludesFiguresAndGraphicsWhenAsked()
    {
        Article article = Fixtures.Article(new Figure("Caption", "Credit"), new Figure(null, "Only credit"), new Graphic("Chart", "Lead.", null, "Source"));
        string text = new PlainTextFormatter(new FormatOptions(IncludeMetadata: false, IncludeFigures: true, IncludeGraphics: true)).Format(article);

        Assert.Equal("[Image] Caption (Credit)\n\n[Image] (Only credit)\n\n[Graphic] Chart. Lead. Source.", text);
    }

    [Fact]
    public void Format_RendersLists()
    {
        Article article = Fixtures.Article(new ItemList(["a", "b"], Ordered: true), new ItemList(["c"], Ordered: false));
        Assert.Equal("1. a\n2. b\n\n- c", new PlainTextFormatter(new FormatOptions(IncludeMetadata: false)).Format(article));
    }

    [Fact]
    public void Format_SkipsMissingMetadata()
    {
        var article = new Article("", null, [], null, null, null, [], null, [new Paragraph("Only.")], ExtractionSource.RenderedHtml);
        Assert.Equal("Only.", new PlainTextFormatter().Format(article));
    }

    [Fact]
    public void Format_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PlainTextFormatter().Format(null!));
    }
}

public class MarkdownFormatterTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a*b_c", "a\\*b\\_c")]
    [InlineData("[x](y)", "\\[x\\](y)")]
    [InlineData("<tag> `code` \\", "\\<tag> \\`code\\` \\\\")]
    public void Escape_EscapesMarkdownSyntax(string input, string expected)
    {
        Assert.Equal(expected, MarkdownFormatter.Escape(input));
    }

    [Fact]
    public void Format_WritesHeadlineDekAndDetails()
    {
        string markdown = new MarkdownFormatter().Format(Fixtures.ArticleOf("Body."));

        Assert.Equal("# Headline\n\n_Summary text._\n\n**By Ada Lovelace**  \nPublished 2026-01-01 12:30 UTC  \n<https://www.example.com/story>\n\nBody.", markdown);
    }

    [Fact]
    public void Format_RendersLinksWithSpacesOutsideTheBrackets()
    {
        var paragraph = new Paragraph([new Inline("See"), new Inline(" the polls ", new Uri("https://example.com/p")), new Inline("now.")]);
        string markdown = new MarkdownFormatter(new FormatOptions(IncludeMetadata: false)).Format(Fixtures.Article(paragraph));

        Assert.Equal("See [the polls](https://example.com/p) now.", markdown);
    }

    [Fact]
    public void Format_RendersOtherBlocks()
    {
        Article article = Fixtures.Article(
            new Heading(2, "Sub"),
            new Heading(4, "Deep"),
            new Quote("Said *this*."),
            new ItemList(["x", "y"], Ordered: false),
            new Note("Credit."),
            new Figure("Cap", null),
            new Graphic("Chart", null, null, null));
        string markdown = new MarkdownFormatter(new FormatOptions(IncludeMetadata: false, IncludeFigures: true, IncludeGraphics: true)).Format(article);

        Assert.Equal("## Sub\n\n#### Deep\n\n> Said \\*this\\*.\n\n- x\n- y\n\n_Credit._\n\n> _Cap_\n\n> **Graphic:** Chart.", markdown);
    }
}

public class LeadSummarizerTests
{
    [Fact]
    public void Summarize_TakesLeadingSentencesWithinTheWordBudget()
    {
        Article article = Fixtures.ArticleOf("One two three. Four five six.", "Seven eight nine.");
        Assert.Equal("One two three. Four five six.", new LeadSummarizer(maxWords: 7).Summarize(article));
    }

    [Fact]
    public void Summarize_AlwaysIncludesAtLeastOneSentence()
    {
        Article article = Fixtures.ArticleOf("This first sentence is longer than the budget.");
        Assert.Equal("This first sentence is longer than the budget.", new LeadSummarizer(maxWords: 2).Summarize(article));
    }

    [Fact]
    public void Summarize_WithoutParagraphs_ReturnsEmpty()
    {
        Assert.Equal("", new LeadSummarizer().Summarize(Fixtures.Article(new Figure("c", null))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_RejectsNonPositiveBudgets(int maxWords)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeadSummarizer(maxWords));
    }
}

public class ExtractiveSummarizerTests
{
    private static readonly Article Story = Fixtures.Article(
        new Paragraph("Oil exports from the Gulf rebounded sharply this month as tankers returned."),
        new Paragraph("The weather at the port was mild and pleasant for most of the week."),
        new Paragraph("Analysts said oil exports and tanker traffic could keep rising through the Gulf."),
        new Paragraph("A local bakery reopened after renovations that lasted several months."),
        new Paragraph("Higher oil exports may ease prices, though tanker insurance remains costly."));

    [Fact]
    public void Summarize_PicksTopicalSentencesInOriginalOrder()
    {
        string summary = new ExtractiveSummarizer(maxSentences: 2).Summarize(Story with { Headline = "Oil exports rebound as tankers return" });
        IReadOnlyList<string> sentences = SentenceSplitter.Split(summary);

        Assert.Equal(2, sentences.Count);
        Assert.All(sentences, sentence => Assert.Contains("oil", sentence, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("bakery", summary, StringComparison.Ordinal);
        List<int> positions = [.. sentences.Select(sentence => Story.Paragraphs.ToList().FindIndex(paragraph => paragraph.Text == sentence))];
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void Summarize_IsDeterministic()
    {
        var summarizer = new ExtractiveSummarizer();
        Assert.Equal(summarizer.Summarize(Story), summarizer.Summarize(Story));
    }

    [Fact]
    public void Summarize_SkipsNearDuplicateSentences()
    {
        Article article = Fixtures.ArticleOf(
            "Senate race tightens in Ohio as voters weigh candidates.",
            "Senate race tightens in Ohio as voters weigh the candidates.",
            "Farmers at the county fair discussed the Senate race and prices.");
        string summary = new ExtractiveSummarizer(maxSentences: 2).Summarize(article);

        Assert.Contains("Farmers", summary, StringComparison.Ordinal);
        Assert.Equal(2, SentenceSplitter.Split(summary).Count);
    }

    [Fact]
    public void Summarize_ReturnsEverythingWhenShorterThanTheLimit()
    {
        Article article = Fixtures.ArticleOf("Only one sentence here with enough words.");
        Assert.Equal("Only one sentence here with enough words.", new ExtractiveSummarizer(maxSentences: 5).Summarize(article));
    }

    [Fact]
    public void Summarize_FallsBackToTheFirstSentenceWhenAllAreShort()
    {
        Article article = Fixtures.ArticleOf("Too short.", "Also short.");
        Assert.Equal("Too short.", new ExtractiveSummarizer().Summarize(article));
    }

    [Fact]
    public void Summarize_WithoutParagraphs_ReturnsEmpty()
    {
        Assert.Equal("", new ExtractiveSummarizer().Summarize(Fixtures.Article()));
    }

    [Fact]
    public void ContentWords_DropsStopWordsAndShortWords()
    {
        Assert.Equal(new[] { "husted", "senator", "senator", "husted", "ohio" }, ExtractiveSummarizer.ContentWords("Mr. Husted is the senator? No: Senator Husted of Ohio."));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveSentenceCounts(int maxSentences)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractiveSummarizer(maxSentences));
    }
}

public class ArticleProcessorTests
{
    private static readonly string Html = Fixtures.PreloadedPage(Fixtures.ArticleJson(Fixtures.ParagraphJson("Alpha beta gamma delta epsilon zeta.")));

    [Fact]
    public void Process_ReturnsArticleProseAndSummary()
    {
        ArticleDigest digest = ArticleProcessor.Default.Process(Html);

        Assert.Equal("Display Headline", digest.Article.Headline);
        Assert.EndsWith("Alpha beta gamma delta epsilon zeta.", digest.Prose, StringComparison.Ordinal);
        Assert.Equal("Alpha beta gamma delta epsilon zeta.", digest.Summary);
    }

    [Fact]
    public void Process_UsesTheInjectedComponents()
    {
        var processor = new ArticleProcessor(new RenderedHtmlExtractor(), new MarkdownFormatter(new FormatOptions(IncludeMetadata: false)), new LeadSummarizer(1));
        ArticleDigest digest = processor.Process("<p>Some *bold* claim. Second.</p>");

        Assert.Equal(ExtractionSource.RenderedHtml, digest.Article.Source);
        Assert.Equal("Some \\*bold\\* claim. Second.", digest.Prose);
        Assert.Equal("Some *bold* claim.", digest.Summary);
    }

    [Fact]
    public void Process_WhenNothingIsFound_Throws()
    {
        Assert.Throws<ArticleExtractionException>(() => ArticleProcessor.Default.Process("<div>empty</div>"));
    }

    [Fact]
    public void Process_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ArticleProcessor.Default.Process(null!));
    }

    [Fact]
    public void Constructor_RejectsNullComponents()
    {
        Assert.Throws<ArgumentNullException>(() => new ArticleProcessor(null!, new PlainTextFormatter(), new LeadSummarizer()));
        Assert.Throws<ArgumentNullException>(() => new ArticleProcessor(new RenderedHtmlExtractor(), null!, new LeadSummarizer()));
        Assert.Throws<ArgumentNullException>(() => new ArticleProcessor(new RenderedHtmlExtractor(), new PlainTextFormatter(), null!));
    }

    [Fact]
    public async Task ProcessAsync_ReadsFromTextReader()
    {
        using var reader = new StringReader(Html);
        ArticleDigest digest = await ArticleProcessor.Default.ProcessAsync(reader, TestContext.Current.CancellationToken);
        Assert.Equal("Display Headline", digest.Article.Headline);
    }

    [Fact]
    public async Task ProcessAsync_ReadsUtf8StreamWithBomAndLeavesItOpen()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Html.Replace("Alpha", "\u00C4lpha", StringComparison.Ordinal))];
        using var stream = new MemoryStream(bytes);
        ArticleDigest digest = await ArticleProcessor.Default.ProcessAsync(stream, TestContext.Current.CancellationToken);

        Assert.StartsWith("\u00C4lpha", digest.Summary, StringComparison.Ordinal);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ProcessAsync_HonoursCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var reader = new StringReader(Html);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArticleProcessor.Default.ProcessAsync(reader, cancellation.Token));
    }

    [Fact]
    public async Task ProcessFileAsync_IncludesThePathInExtractionErrors()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(path, "<div>nothing</div>", TestContext.Current.CancellationToken);

        try
        {
            ArticleExtractionException exception = await Assert.ThrowsAsync<ArticleExtractionException>(() => ArticleProcessor.Default.ProcessFileAsync(path, TestContext.Current.CancellationToken));
            Assert.StartsWith(path, exception.Message, StringComparison.Ordinal);
            Assert.IsType<ArticleExtractionException>(exception.InnerException);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProcessFileAsync_WhenMissing_ThrowsFileNotFound()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        await Assert.ThrowsAsync<FileNotFoundException>(() => ArticleProcessor.Default.ProcessFileAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProcessFileAsync_RejectsBlankPaths(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ArticleProcessor.Default.ProcessFileAsync(path, TestContext.Current.CancellationToken));
    }
}

public sealed class ArticleFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("articles-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Resolve_FindsHtmlFilesRecursivelyInOrdinalOrder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "b", "c"));
        File.WriteAllText(Path.Combine(_root, "b", "c", "z.html"), "");
        File.WriteAllText(Path.Combine(_root, "a.HTML"), "");
        File.WriteAllText(Path.Combine(_root, "b", "page.htm"), "");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "");

        IReadOnlyList<string> files = ArticleFiles.Resolve([_root]);

        Assert.Equal(
            new[] { Path.Combine(_root, "a.HTML"), Path.Combine(_root, "b", "c", "z.html"), Path.Combine(_root, "b", "page.htm") },
            files);
    }

    [Fact]
    public void Resolve_PassesFilesThroughInTheGivenOrder()
    {
        string first = Path.Combine(_root, "2.txt");
        string second = Path.Combine(_root, "1.txt");
        File.WriteAllText(first, "");
        File.WriteAllText(second, "");

        Assert.Equal(new[] { first, second }, ArticleFiles.Resolve([first, second]));
    }

    [Fact]
    public void Resolve_WhenInputIsMissing_Throws()
    {
        string missing = Path.Combine(_root, "missing.html");
        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() => ArticleFiles.Resolve([missing]));
        Assert.Equal(missing, exception.FileName);
    }

    [Fact]
    public void Resolve_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ArticleFiles.Resolve(null!));
    }
}

public class DigestReportTests
{
    [Fact]
    public void Render_ShowsNameStatsSummaryAndProse()
    {
        Article article = Fixtures.ArticleOf("one two three") with { ReportedWordCount = 4 };
        string report = DigestReport.Render("/some/dir/story.html", new ArticleDigest(article, "PROSE", "SUMMARY TEXT"));
        string[] lines = report.Split('\n');

        Assert.Equal("story.html", lines[1]);
        Assert.Equal("Source: PreloadedData  Paragraphs: 1  Words: 3/4", lines[2]);
        Assert.Contains("\nSUMMARY TEXT\n", report, StringComparison.Ordinal);
        Assert.EndsWith("\nPROSE\n", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MiddleTruncatesLongFileNames()
    {
        string name = new string('a', 80) + "_final_v2.html";
        string[] lines = DigestReport.Render(name, new ArticleDigest(Fixtures.ArticleOf("x"), "", "")).Split('\n');

        Assert.Equal(new FilenameString(name).Truncate(DigestReport.NameWidth), lines[1]);
        Assert.EndsWith("v2.html", lines[1], StringComparison.Ordinal);
        Assert.Equal("Source: PreloadedData  Paragraphs: 1  Words: 1", lines[2]);
    }
}

public class SampleArticleTests
{
    private const string Husted = "2026/09/28/us/politics/john-husted-ohio-president-trump-midterms.html";
    private const string Oil = "2026/09/29/business/oil-exports-strait-of-hormuz.html";
    private const string Turnout = "2026/09/29/us/politics/democrats-voter-turnout.html";

    private static async Task<Article> LoadAsync(string relative)
    {
        ArticleDigest digest = await ArticleProcessor.Default.ProcessFileAsync(Fixtures.SamplePath(relative), TestContext.Current.CancellationToken);
        return digest.Article;
    }

    [Theory]
    [InlineData(Husted, 32, 1444, 1, 5, 0, 0)]
    [InlineData(Oil, 34, 1287, 1, 1, 0, 1)]
    [InlineData(Turnout, 16, 663, 3, 0, 2, 0)]
    public async Task Sample_ExtractsTheCompleteBodyFromPreloadedData(string relative, int paragraphs, int reportedWords, int authors, int figures, int graphics, int notes)
    {
        Article article = await LoadAsync(relative);

        Assert.Equal(ExtractionSource.PreloadedData, article.Source);
        Assert.Equal(paragraphs, article.Paragraphs.Count());
        Assert.Equal(reportedWords, article.ReportedWordCount);
        Assert.InRange(article.WordCount, reportedWords * 99 / 100, reportedWords);
        Assert.Equal(authors, article.Authors.Count);
        Assert.Equal(figures, article.Body.OfType<Figure>().Count());
        Assert.Equal(graphics, article.Body.OfType<Graphic>().Count());
        Assert.Equal(notes, article.Body.OfType<Note>().Count());
    }

    [Theory]
    [InlineData(Husted, "In Ohio, a Humbling Question for Senator Jon Husted", "Emily Davies", "2026-09-28T16:31:46Z")]
    [InlineData(Oil, "Mideast Oil Exports Rebound", "Peter Eavis", "2026-09-29T09:01:57Z")]
    [InlineData(Turnout, "Democratic Turnout Surged in the Primaries", "Reid J. Epstein", "2026-09-29T09:01:57Z")]
    public async Task Sample_ReadsMetadata(string relative, string headlineStart, string firstAuthor, string published)
    {
        Article article = await LoadAsync(relative);

        Assert.StartsWith(headlineStart, article.Headline, StringComparison.Ordinal);
        Assert.Equal(firstAuthor, article.Authors[0]);
        Assert.Equal(DateTimeOffset.Parse(published, System.Globalization.CultureInfo.InvariantCulture), article.Published);
        Assert.Equal(new Uri("https://www.nytimes.com/" + relative), article.Url);
        Assert.False(string.IsNullOrWhiteSpace(article.Summary));
        Assert.NotEmpty(article.Sections);
    }

    [Theory]
    [InlineData(Husted)]
    [InlineData(Oil)]
    [InlineData(Turnout)]
    public async Task Sample_ProseIsCleanText(string relative)
    {
        ArticleDigest digest = await ArticleProcessor.Default.ProcessFileAsync(Fixtures.SamplePath(relative), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("__typename", digest.Prose, StringComparison.Ordinal);
        Assert.DoesNotContain("Dropzone", digest.Prose, StringComparison.Ordinal);
        Assert.DoesNotContain("<", digest.Prose, StringComparison.Ordinal);
        Assert.DoesNotContain("verify access", digest.Prose, StringComparison.Ordinal);
        Assert.DoesNotContain("  ", digest.Prose, StringComparison.Ordinal);
        string lastBlock = digest.Article.Body
            .Select(block => block switch { Paragraph paragraph => paragraph.Text, Note note => note.Text, _ => null })
            .OfType<string>()
            .Last();

        Assert.StartsWith(digest.Article.Headline, digest.Prose, StringComparison.Ordinal);
        Assert.EndsWith("\n\n" + lastBlock, digest.Prose, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Husted)]
    [InlineData(Oil)]
    [InlineData(Turnout)]
    public async Task Sample_SummaryIsThreeSentencesTakenFromTheArticle(string relative)
    {
        ArticleDigest digest = await ArticleProcessor.Default.ProcessFileAsync(Fixtures.SamplePath(relative), TestContext.Current.CancellationToken);
        IReadOnlyList<string> sentences = SentenceSplitter.Split(digest.Summary);

        Assert.Equal(3, sentences.Count);
        Assert.All(sentences, sentence => Assert.Contains(sentence, digest.Prose, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Husted, 5)]
    [InlineData(Oil, 6)]
    [InlineData(Turnout, 5)]
    public async Task Sample_RenderedHtmlFallbackMatchesThePreloadedOpening(string relative, int renderedParagraphs)
    {
        string html = await File.ReadAllTextAsync(Fixtures.SamplePath(relative), TestContext.Current.CancellationToken);
        Article? full = new PreloadedDataExtractor().Extract(HtmlPage.Parse(html));
        Article? rendered = CompositeArticleExtractor.Default.Extract(HtmlPage.Parse(html.Replace(PreloadedDataExtractor.VariableName, "window.__removed", StringComparison.Ordinal)));

        Assert.NotNull(full);
        Assert.NotNull(rendered);
        Assert.Equal(ExtractionSource.RenderedHtml, rendered.Source);
        Assert.Equal(full.Headline, rendered.Headline);
        Assert.Equal(full.Paragraphs.Take(renderedParagraphs).Select(paragraph => paragraph.Text), rendered.Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public async Task Sample_LinksAreAbsolute()
    {
        Article article = await LoadAsync(Husted);
        List<Inline> links = [.. article.Paragraphs.SelectMany(paragraph => paragraph.Links)];

        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.True(link.Link!.IsAbsoluteUri));
    }

    [Fact]
    public async Task Sample_ProcessFilesAsyncPreservesInputOrder()
    {
        string[] samples = [Turnout, Husted, Oil];
        IReadOnlyList<ArticleDigest> digests = await ArticleProcessor.Default.ProcessFilesAsync(samples.Select(Fixtures.SamplePath), TestContext.Current.CancellationToken);

        Assert.Equal(samples.Select(sample => new Uri("https://www.nytimes.com/" + sample)), digests.Select(digest => digest.Article.Url));
    }

    [Fact]
    public void Sample_FilesAreDiscoveredFromTheOutputDirectory()
    {
        Assert.Equal(28, ArticleFiles.Resolve([Path.Combine(AppContext.BaseDirectory, "nytimes")]).Count);
    }
}

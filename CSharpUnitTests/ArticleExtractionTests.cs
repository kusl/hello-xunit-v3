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

    public static string ScriptPage(string json) =>
        "<html><body><script>window.__preloadedData = " + json + ";</script></body></html>";

    public static string RootedArticle(string blocks, string root = "loaderData") =>
        "{\"" + root + "\":{\"data\":{\"article\":{\"sprinkledBody\":{\"content\":[" + blocks + "]}}}}}";
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
    [InlineData("window.__data =")]
    [InlineData("window.__data = foo({\"a\":1});")]
    [InlineData("window.__data = [undefined")]
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

    [Fact]
    public void TryExtract_SkipsUsesThatAreNotAssignments()
    {
        const string Source = """window.__data({"wrong":1}); window.__data = {"right":1};""";
        Assert.True(ScriptData.TryExtractAssignedObject(Source, Name, out string? json));
        Assert.Equal("""{"right":1}""", json);
    }

    [Fact]
    public void TryExtract_ReadsEmptyStrings()
    {
        Assert.True(ScriptData.TryExtractAssignedObject("""window.__data = {"":1};""", Name, out string? json));
        Assert.Equal("""{"":1}""", json);
    }

    [Theory]
    [InlineData("window.__data = [u];", "[u]")]
    [InlineData("window.__data = [undefinedX];", "[undefinedX]")]
    [InlineData("window.__data = [xundefined];", "[xundefined]")]
    public void TryExtract_ReplacesOnlyStandaloneUndefined(string source, string expected)
    {
        Assert.True(ScriptData.TryExtractAssignedObject(source, Name, out string? json));
        Assert.Equal(expected, json);
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
        Assert.Throws<ArgumentNullException>("text", () => SentenceSplitter.Split(null!));
    }

    [Theory]
    [InlineData("\u201CGo now.\u201D Then leave.", "\u201CGo now.\u201D|Then leave.")]
    [InlineData("She wrote \u201CMr.\u201D Then left.", "She wrote \u201CMr.\u201D|Then left.")]
    [InlineData("Use the.NET runtime.", "Use the.NET runtime.")]
    [InlineData("Version 1.5. Next one.", "Version 1.5.|Next one.")]
    [InlineData("Hello Dr! Next.", "Hello Dr!|Next.")]
    [InlineData("Wait... Then go.", "Wait...|Then go.")]
    [InlineData("Really?! Yes.", "Really?!|Yes.")]
    public void Split_HandlesClosersRunsAndInnerPeriods(string text, string expected)
    {
        Assert.Equal(expected.Split('|'), SentenceSplitter.Split(text));
    }

    [Theory]
    [InlineData("mr")]
    [InlineData("mrs")]
    [InlineData("ms")]
    [InlineData("dr")]
    [InlineData("prof")]
    [InlineData("sen")]
    [InlineData("rep")]
    [InlineData("gov")]
    [InlineData("gen")]
    [InlineData("lt")]
    [InlineData("col")]
    [InlineData("sgt")]
    [InlineData("capt")]
    [InlineData("adm")]
    [InlineData("maj")]
    [InlineData("st")]
    [InlineData("jr")]
    [InlineData("sr")]
    [InlineData("inc")]
    [InlineData("co")]
    [InlineData("corp")]
    [InlineData("ltd")]
    [InlineData("vs")]
    [InlineData("etc")]
    [InlineData("no")]
    [InlineData("mt")]
    [InlineData("ft")]
    [InlineData("ave")]
    [InlineData("blvd")]
    [InlineData("jan")]
    [InlineData("feb")]
    [InlineData("mar")]
    [InlineData("apr")]
    [InlineData("aug")]
    [InlineData("sept")]
    [InlineData("sep")]
    [InlineData("oct")]
    [InlineData("nov")]
    [InlineData("dec")]
    [InlineData("u.s")]
    [InlineData("u.n")]
    [InlineData("u.k")]
    [InlineData("e.u")]
    [InlineData("d.c")]
    [InlineData("a.m")]
    [InlineData("p.m")]
    [InlineData("i.e")]
    [InlineData("e.g")]
    public void Split_DoesNotBreakAfterKnownAbbreviations(string abbreviation)
    {
        Assert.Single(SentenceSplitter.Split($"See {abbreviation}. Next"));
    }

    [Fact]
    public void Split_BreaksAfterUnknownWords()
    {
        Assert.Equal(2, SentenceSplitter.Split("See xyz. Next").Count);
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

    [Fact]
    public void Content_StartsAfterTheFirstClosingAngleBracket()
    {
        Assert.Equal("abc", HtmlElements.Content(">abc</x>", 0, "x"));
    }

    [Fact]
    public void Content_WhenStartTagIsUnterminated_ReturnsEmpty()
    {
        Assert.Equal("", HtmlElements.Content("<div", 0, "div"));
    }

    [Fact]
    public void Content_DoesNotCountSelfClosingTagsAsNesting()
    {
        Assert.Equal("<div/>inner", HtmlElements.Content("<div><div/>inner</div>tail", 0, "div"));
    }

    [Fact]
    public void Remove_WhenUnclosed_DropsTheRest()
    {
        Assert.Equal("a", HtmlElements.Remove("a<figure>x", "figure"));
    }

    [Fact]
    public void Content_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>("html", () => HtmlElements.Content(null!, 0, "p"));
        Assert.Throws<ArgumentException>("tag", () => HtmlElements.Content("<p>", 0, ""));
    }

    [Fact]
    public void Remove_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>("html", () => HtmlElements.Remove(null!, "p"));
        Assert.Throws<ArgumentException>("tag", () => HtmlElements.Remove("<p>", ""));
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
        Assert.Throws<ArgumentNullException>("html", () => HtmlPage.Parse(null!));
    }

    [Fact]
    public void Parse_ReadsSecondaryMetaTags()
    {
        const string Html = """
            <meta name="twitter:title" content="Twitter Title">
            <meta property="og:description" content="OG description">
            <meta name="author" content="Ann Lee and Bo Kim">
            <meta property="article:modified_time" content="2026-09-29T10:00:00Z">
            <meta name="url" content="http://example.com/plain">
            """;
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal("Twitter Title", metadata.Headline);
        Assert.Equal("OG description", metadata.Summary);
        Assert.Equal(new[] { "Ann Lee", "Bo Kim" }, metadata.Authors);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), metadata.Modified);
        Assert.Equal(new Uri("http://example.com/plain"), metadata.Url);
    }

    [Fact]
    public void Parse_PrefersPrimaryMetaTags()
    {
        const string Html = """
            <meta name="byl" content="By Primary Person">
            <meta name="author" content="Secondary Person">
            <meta property="og:url" content="https://example.com/og">
            <meta name="url" content="https://example.com/plain">
            <meta name="description" content="First" content="Second">
            """;
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal(new[] { "Primary Person" }, metadata.Authors);
        Assert.Equal(new Uri("https://example.com/og"), metadata.Url);
        Assert.Equal("First", metadata.Summary);
    }

    [Fact]
    public void Parse_PrefersJsonLdOverMetaTags()
    {
        const string Html = """
            <meta property="article:published_time" content="2020-01-01T00:00:00Z">
            <meta property="article:modified_time" content="2020-01-02T00:00:00Z">
            <meta property="og:url" content="https://example.com/meta">
            <script type="application/ld+json">{"@type":"NewsArticle","datePublished":"2026-03-04T05:06:07Z","dateModified":"2026-03-05T00:00:00Z","url":"https://example.com/ld","@id":"https://example.com/id"}</script>
            """;
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero), metadata.Published);
        Assert.Equal(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), metadata.Modified);
        Assert.Equal(new Uri("https://example.com/ld"), metadata.Url);
    }

    [Fact]
    public void Parse_ReadsJsonLdFallbackFields()
    {
        const string Html = """
            <meta name="byl" content="By Meta Author">
            <meta property="article:section" content="Business">
            <script type="application/ld+json">{"@type":"Article","name":"Named","@id":"https://example.com/id"}</script>
            """;
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal("Named", metadata.Headline);
        Assert.Equal(new[] { "Meta Author" }, metadata.Authors);
        Assert.Equal(new[] { "Business" }, metadata.Sections);
        Assert.Equal(new Uri("https://example.com/id"), metadata.Url);
    }

    [Fact]
    public void Parse_ReadsStringAuthorsAndSectionArrays()
    {
        const string Html = """<script type="application/ld+json">{"@type":"Article","author":"Solo Writer","articleSection":[" World ",5,"Europe"]}</script>""";
        PageMetadata metadata = HtmlPage.Parse(Html).Metadata;

        Assert.Equal(new[] { "Solo Writer" }, metadata.Authors);
        Assert.Equal(new[] { "World", "Europe" }, metadata.Sections);
    }

    [Theory]
    [InlineData("NewsArticle")]
    [InlineData("ReportageNewsArticle")]
    [InlineData("BlogPosting")]
    [InlineData("Report")]
    public void Parse_RecognizesArticleTypes(string type)
    {
        string html = $$"""<script type="application/ld+json">{"@type":"{{type}}","headline":"Typed"}</script>""";
        Assert.Equal("Typed", HtmlPage.Parse(html).Metadata.Headline);
    }

    [Fact]
    public void Parse_IgnoresOtherJsonLdTypes()
    {
        Assert.Null(HtmlPage.Parse("""<script type="application/ld+json">{"@type":"WebPage","headline":"Page"}</script>""").Metadata.Headline);
    }

    [Fact]
    public void Parse_ToleratesCommentsAndTrailingCommasInJsonLd()
    {
        const string Html = """<script type="application/ld+json">/* note */ {"@type":"Article","headline":"Lenient",}</script>""";
        Assert.Equal("Lenient", HtmlPage.Parse(Html).Metadata.Headline);
    }

    [Theory]
    [InlineData("By Ann,, Bo", "Ann|Bo")]
    [InlineData("Ann Lee and Bo Kim", "Ann Lee|Bo Kim")]
    public void Parse_SplitsBylines(string byline, string expected)
    {
        Assert.Equal(expected.Split('|'), HtmlPage.Parse($"<meta name=\"byl\" content=\"{byline}\">").Metadata.Authors);
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

    private static Article? TryExtract(string html) => new PreloadedDataExtractor().Extract(HtmlPage.Parse(html));

    [Theory]
    [InlineData("loaderData")]
    [InlineData("initialData")]
    [InlineData("initialState")]
    public void Extract_FindsHeadlinelessArticlesAtKnownRoots(string root)
    {
        Article? article = TryExtract(Fixtures.ScriptPage(Fixtures.RootedArticle(Fixtures.ParagraphJson("Rooted."), root)));

        Assert.NotNull(article);
        Assert.Equal("Rooted.", Assert.Single(article.Paragraphs).Text);
    }

    [Fact]
    public void Extract_IgnoresHeadlinelessArticlesElsewhere()
    {
        string json = "{\"x\":{\"sprinkledBody\":{\"content\":[" + Fixtures.ParagraphJson("Orphan.") + "]}}}";
        Assert.Null(TryExtract(Fixtures.ScriptPage(json)));
    }

    [Fact]
    public void Extract_SkipsKnownRootsWithoutBody()
    {
        string json = "{\"loaderData\":{\"data\":{\"article\":{\"headline\":{\"default\":\"Empty\"}}}},\"initialData\":{\"data\":{\"article\":{\"sprinkledBody\":{\"content\":[" + Fixtures.ParagraphJson("Second root.") + "]}}}}}";
        Article? article = TryExtract(Fixtures.ScriptPage(json));

        Assert.NotNull(article);
        Assert.Equal("Second root.", Assert.Single(article.Paragraphs).Text);
    }

    private static string Nested(int depth)
    {
        string json = "{\"headline\":{\"default\":\"Deep\"},\"sprinkledBody\":{\"content\":[" + Fixtures.ParagraphJson("Deep.") + "]}}";
        for (int level = 0; level < depth; level++)
        {
            json = "{\"x\":" + json + "}";
        }

        return Fixtures.ScriptPage(json);
    }

    [Fact]
    public void Extract_SearchesTwelveLevelsDeep()
    {
        Assert.NotNull(TryExtract(Nested(12)));
    }

    [Fact]
    public void Extract_StopsSearchingBelowTwelveLevels()
    {
        Assert.Null(TryExtract(Nested(13)));
    }

    [Fact]
    public void Extract_TreatsNullSprinkledBodyAsMissing()
    {
        string json = Fixtures.ArticleJson("").Replace("\"sprinkledBody\": {\"content\": []}", "\"sprinkledBody\": null, \"body\": {\"content\": [" + Fixtures.ParagraphJson("From body.") + "]}", StringComparison.Ordinal);
        Article? article = TryExtract(Fixtures.PreloadedPage(json));

        Assert.NotNull(article);
        Assert.Equal("From body.", Assert.Single(article.Paragraphs).Text);
    }

    [Fact]
    public void Extract_PrefersSprinkledBodyOverBody()
    {
        Article article = Extract(Fixtures.ParagraphJson("Sprinkled."), ", \"body\": {\"content\": [" + Fixtures.ParagraphJson("Plain body.") + "]}");
        Assert.Equal("Sprinkled.", Assert.Single(article.Paragraphs).Text);
    }

    [Fact]
    public void Extract_ToleratesTrailingCommas()
    {
        Assert.Equal("Lenient.", Assert.Single(Extract(Fixtures.ParagraphJson("Lenient."), ",").Paragraphs).Text);
    }

    [Fact]
    public void Extract_ResolvesRelativeLinksAgainstNytimesWhenNoUrlIsKnown()
    {
        const string Block = """{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Link","formats":[{"__typename":"LinkFormat","url":"/2026/a.html"}]}]}""";
        Article? article = TryExtract(Fixtures.ScriptPage(Fixtures.RootedArticle(Block)));

        Assert.NotNull(article);
        Assert.Equal(new Uri("https://www.nytimes.com/2026/a.html"), Assert.Single(Assert.Single(article.Paragraphs).Links).Link);
    }

    private const string MetaTags = """<meta name="byl" content="By Pat Lee"><meta property="article:published_time" content="2026-02-03T04:05:06Z"><meta property="article:modified_time" content="2026-02-04T00:00:00Z"><meta property="article:section" content="Science"><meta property="og:url" content="https://example.com/meta">""";

    [Fact]
    public void Extract_FallsBackToPageMetadataForEveryField()
    {
        Article? article = TryExtract(MetaTags + Fixtures.ScriptPage(Fixtures.RootedArticle(Fixtures.ParagraphJson("Body."))));

        Assert.NotNull(article);
        Assert.Equal("", article.Headline);
        Assert.Equal(new[] { "Pat Lee" }, article.Authors);
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero), article.Published);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 0, 0, 0, TimeSpan.Zero), article.Modified);
        Assert.Equal(new[] { "Science" }, article.Sections);
        Assert.Equal(new Uri("https://example.com/meta"), article.Url);
    }

    [Fact]
    public void Extract_PrefersArticleFieldsOverPageMetadata()
    {
        Article? article = TryExtract(MetaTags + Fixtures.PreloadedPage(Fixtures.ArticleJson(Fixtures.ParagraphJson("Body."))));

        Assert.NotNull(article);
        Assert.Equal(new[] { "Ada Lovelace", "Alan Turing" }, article.Authors);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero), article.Published);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero), article.Modified);
        Assert.Equal(new[] { "U.S.", "Politics" }, article.Sections);
        Assert.Equal(new Uri("https://www.example.com/2026/01/01/story.html"), article.Url);
    }

    [Fact]
    public void Extract_PrefersCreatorsOverTheRenderedByline()
    {
        string json = Fixtures.ArticleJson(Fixtures.ParagraphJson("x")).Replace("By Ada Lovelace and Alan Turing", "By Someone Else", StringComparison.Ordinal);
        Article? article = TryExtract(Fixtures.PreloadedPage(json));

        Assert.NotNull(article);
        Assert.Equal(new[] { "Ada Lovelace", "Alan Turing" }, article.Authors);
    }

    [Fact]
    public void Extract_UsesSeoHeadlineAndSectionNamesAsFallbacks()
    {
        string json = Fixtures.ArticleJson(Fixtures.ParagraphJson("x"))
            .Replace("\"default\": \"Display Headline\", ", "", StringComparison.Ordinal)
            .Replace("\"displayName\": \"Politics\", ", "", StringComparison.Ordinal);
        Article? article = TryExtract(Fixtures.PreloadedPage(json));

        Assert.NotNull(article);
        Assert.Equal("SEO Headline", article.Headline);
        Assert.Equal(new[] { "U.S.", "politics" }, article.Sections);
    }

    [Fact]
    public void Extract_SkipsEmptyQuotesListsNotesAndHeadings()
    {
        const string Blocks = """
            {"__typename":"BlockquoteBlock","content":[{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":" "}]}]},
            {"__typename":"ListBlock","content":[{"__typename":"ListItemBlock","content":[]}]},
            {"__typename":"ListBlock","content":[{"__typename":"ListItemBlock","content":[]},{"__typename":"ListItemBlock","content":[{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Item"}]}]}]},
            {"__typename":"DetailBlock","content":[]},
            {"__typename":"Heading2Block","content":[]},
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Real."}]}
            """;

        Assert.Collection(
            Extract(Blocks).Body,
            block => Assert.Equal(new[] { "Item" }, Assert.IsType<ItemList>(block).Items),
            block => Assert.Equal("Real.", Assert.IsType<Paragraph>(block).Text));
    }

    [Fact]
    public void Extract_IgnoresUnknownAndUntypedBlocksEvenWithLedeMedia()
    {
        const string Blocks = """
            {"__typename":"MysteryBlock","ledeMedia":{"__typename":"ImageBlock","media":{"credit":"Hidden"}}},
            {"content":[{"text":"Untyped"}]},
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Kept."}]}
            """;
        Assert.IsType<Paragraph>(Assert.Single(Extract(Blocks).Body));
    }

    [Theory]
    [InlineData("Heading1Block", 1)]
    [InlineData("Heading6Block", 6)]
    [InlineData("Heading0Block", 0)]
    [InlineData("Heading7Block", 0)]
    [InlineData("Heading2Blocks", 0)]
    [InlineData("Xeading2Block", 0)]
    [InlineData("Heading2Xlock", 0)]
    public void Extract_RecognizesHeadingOneThroughSix(string type, int level)
    {
        string blocks = $$"""{"__typename":"{{type}}","content":[{"__typename":"TextInline","text":"Title"}]},""" + Fixtures.ParagraphJson("Body.");
        Heading[] expected = level == 0 ? [] : [new Heading(level, "Title")];

        Assert.Equal(expected, Extract(blocks).Body.OfType<Heading>());
    }

    [Fact]
    public void Extract_FlattensNestedInlineContainers()
    {
        const string Block = """{"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Out "},{"__typename":"StyledInline","content":[{"__typename":"TextInline","text":"in"}]}]}""";
        Assert.Equal("Out in", Assert.Single(Extract(Block).Paragraphs).Text);
    }

    [Fact]
    public void Extract_JoinsInlineTextAndSeparatesBlocks()
    {
        const string Blocks = """
            {"__typename":"Heading2Block","content":[{"text":"O"},{"text":"ne"},{"__typename":"StyledInline","content":[{"text":"-two"}]},{"__typename":"LineBreakInline"},{"text":"three"}]},
            {"__typename":"ListBlock","content":[{"__typename":"ListItemBlock","content":[{"__typename":"ParagraphBlock","content":[{"text":"First"}]},{"__typename":"ParagraphBlock","content":[{"text":"Second"}]}]}]},
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Body."}]}
            """;

        Assert.Collection(
            Extract(Blocks).Body,
            block => Assert.Equal(new Heading(2, "One-two three"), block),
            block => Assert.Equal(new[] { "First Second" }, Assert.IsType<ItemList>(block).Items),
            block => Assert.IsType<Paragraph>(block));
    }

    [Fact]
    public void Extract_KeepsFiguresWithOnlyACaptionOrOnlyACredit()
    {
        const string Blocks = """
            {"__typename":"ImageBlock","media":{"credit":"Only credit"}},
            {"__typename":"ImageBlock","media":{"caption":{"text":"Only caption"}}},
            {"__typename":"ParagraphBlock","content":[{"__typename":"TextInline","text":"Body."}]}
            """;

        Assert.Equal(new[] { new Figure(null, "Only credit"), new Figure("Only caption", null) }, Extract(Blocks).Body.OfType<Figure>());
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

    [Fact]
    public void Extract_SkipsBlankLines()
    {
        const string Html = """<script type="application/ld+json">{"@type":"NewsArticle","headline":"H","articleBody":"\nFirst."}</script>""";
        Article? article = new JsonLdExtractor().Extract(HtmlPage.Parse(Html));

        Assert.NotNull(article);
        Assert.Equal("First.", Assert.Single(article.Paragraphs).Text);
    }

    [Fact]
    public void Extract_WhenPageIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("page", () => new JsonLdExtractor().Extract(null!));
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

    [Theory]
    [InlineData("script")]
    [InlineData("style")]
    [InlineData("noscript")]
    [InlineData("template")]
    [InlineData("svg")]
    [InlineData("figure")]
    [InlineData("aside")]
    [InlineData("nav")]
    [InlineData("form")]
    [InlineData("button")]
    [InlineData("header")]
    [InlineData("footer")]
    public void Extract_DropsParagraphsInsideNonContentElements(string element)
    {
        Assert.Equal("Kept.", Assert.Single(Extract($"<article><p>Kept.</p><{element}><p>Dropped.</p></{element}></article>").Paragraphs).Text);
    }

    [Fact]
    public void Extract_DropsNestedRegionElements()
    {
        Assert.Equal("Kept.", Assert.Single(Extract("<article><p>Kept.</p><article><p>Nested.</p></article></article>").Paragraphs).Text);
    }

    [Fact]
    public void Extract_RemovesCommentsInsideParagraphs()
    {
        Assert.Equal("Real text.", Assert.Single(Extract("<article><p>Real <!-- note --> text.</p></article>").Paragraphs).Text);
    }

    [Fact]
    public void Extract_PicksTheParagraphStyleWithTheMostText()
    {
        const string Html = """<article><p class="a">Ten chars.</p><p class="a">Ten chars.</p><p class="b">Fifteen chars!!</p></article>""";
        Assert.Equal(new[] { "Ten chars.", "Ten chars." }, Extract(Html).Paragraphs.Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void Extract_UsesAnEmptyHeadlineWhenThePageHasNone()
    {
        Assert.Equal("", Extract("<p>x</p>").Headline);
    }

    [Fact]
    public void Extract_SkipsEmptyHeadings()
    {
        Assert.IsType<Paragraph>(Assert.Single(Extract("<body><p>x</p><h2> </h2></body>").Body));
    }

    [Fact]
    public void Extract_DoesNotAddEmptyInlines()
    {
        Inline inline = Assert.Single(Assert.Single(Extract("""<p><a href="https://example.com/x">link</a></p>""").Paragraphs).Inlines);
        Assert.Equal(new Inline("link", new Uri("https://example.com/x")), inline);
    }

    [Fact]
    public void Extract_WhenPageIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("page", () => new RenderedHtmlExtractor().Extract(null!));
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

    [Fact]
    public void Extract_WhenPageIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("page", () => new CompositeArticleExtractor([]).Extract(null!));
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

    [Fact]
    public void Format_RendersFiguresWithoutCaptionOrCredit()
    {
        Article article = Fixtures.Article(new Figure(null, null));
        Assert.Equal("[Image] ", new PlainTextFormatter(new FormatOptions(IncludeMetadata: false, IncludeFigures: true)).Format(article));
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

    [Fact]
    public void Format_SkipsMissingMetadata()
    {
        var article = new Article("", null, [], null, null, null, [], null, [new Paragraph("Only.")], ExtractionSource.RenderedHtml);
        Assert.Equal("Only.", new MarkdownFormatter().Format(article));
    }

    [Fact]
    public void Format_JoinsInlinesWithoutAddingSpaces()
    {
        var link = new Uri("https://example.com/f");
        var paragraph = new Paragraph([new Inline("pre"), new Inline(""), new Inline("fix", link), new Inline(" ", link), new Inline("end")]);
        string markdown = new MarkdownFormatter(new FormatOptions(IncludeMetadata: false)).Format(Fixtures.Article(paragraph));

        Assert.Equal("pre[fix](https://example.com/f) end", markdown);
    }

    [Fact]
    public void Format_NumbersOrderedLists()
    {
        Article article = Fixtures.Article(new ItemList(["a", "b"], Ordered: true));
        Assert.Equal("1. a\n2. b", new MarkdownFormatter(new FormatOptions(IncludeMetadata: false)).Format(article));
    }

    [Fact]
    public void Escape_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("text", () => MarkdownFormatter.Escape(null!));
    }

    [Fact]
    public void Format_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("article", () => new MarkdownFormatter().Format(null!));
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

    [Fact]
    public void Summarize_IncludesASentenceThatExactlyFillsTheBudget()
    {
        Article article = Fixtures.ArticleOf("One two three. Four five six.", "Seven eight nine.");
        Assert.Equal("One two three. Four five six.", new LeadSummarizer(maxWords: 6).Summarize(article));
    }

    [Fact]
    public void Summarize_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("article", () => new LeadSummarizer().Summarize(null!));
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

    public static TheoryData<string> StopWords =>
    [
        "about", "above", "after", "again", "against", "all", "also", "and", "any", "are",
        "because", "been", "before", "being", "below", "between", "both", "but", "can", "could",
        "did", "does", "doing", "down", "during", "each", "even", "few", "for", "from",
        "further", "had", "has", "have", "having", "her", "here", "hers", "herself", "him",
        "himself", "his", "how", "into", "its", "itself", "just", "last", "like", "made",
        "make", "many", "may", "more", "most", "mrs", "much", "myself", "new", "nor",
        "not", "now", "off", "once", "one", "only", "other", "our", "ours", "ourselves",
        "out", "over", "own", "said", "same", "say", "says", "she", "should", "since",
        "some", "still", "such", "than", "that", "the", "their", "theirs", "them", "themselves",
        "then", "there", "these", "they", "this", "those", "through", "too", "two", "under",
        "until", "very", "was", "were", "what", "when", "where", "which", "while", "who",
        "whom", "why", "will", "with", "would", "year", "years", "yet", "you", "your",
        "yours", "yourself", "yourselves", "it's", "don't", "didn't", "that's", "he's", "she's", "they're",
    ];

    [Theory]
    [MemberData(nameof(StopWords))]
    public void ContentWords_DropsEveryStopWord(string word)
    {
        Assert.Empty(ExtractiveSummarizer.ContentWords(word));
    }

    [Fact]
    public void ContentWords_DropsTwoLetterWords()
    {
        Assert.Empty(ExtractiveSummarizer.ContentWords("ox is up"));
    }

    [Fact]
    public void Summarize_BoostsWordsFromTheHeadlineAndSummary()
    {
        Article article = Fixtures.ArticleOf(
            "Dockworkers began a harbor strike on Monday.",
            "The city budget vote was delayed by the council.",
            "The council said the city budget vote would be held next week.") with
        { Headline = "Harbor strike", Summary = "Dockworkers walk out" };

        Assert.Equal("Dockworkers began a harbor strike on Monday.", new ExtractiveSummarizer(maxSentences: 1).Summarize(article));
    }

    [Fact]
    public void Summarize_SumsWordWeights()
    {
        Article article = Fixtures.ArticleOf(
            "Farmers planted corn, wheat, barley and oats in the valley.",
            "Heavy rain soaked the valley again, and more rain is coming.",
            "Rain also delayed the harvest of corn, wheat and oats.");

        Assert.Equal("Farmers planted corn, wheat, barley and oats in the valley.", new ExtractiveSummarizer(maxSentences: 1).Summarize(article));
    }

    [Fact]
    public void Summarize_AcceptsSentencesOfExactlySixWords()
    {
        Article article = Fixtures.ArticleOf("Port cranes stood idle all day.", "A bakery on the corner reopened yesterday.");
        Assert.Equal("Port cranes stood idle all day.", new ExtractiveSummarizer(maxSentences: 1).Summarize(article));
    }

    [Fact]
    public void Summarize_RejectsSentencesThatAreHalfDuplicates()
    {
        Article article = Fixtures.ArticleOf(
            "Port cranes sat idle during the strike.",
            "Port cranes sat idle during the strike as wage talks stalled near the docks.",
            "A bakery on the corner reopened yesterday.");

        Assert.Equal("Port cranes sat idle during the strike. A bakery on the corner reopened yesterday.", new ExtractiveSummarizer(maxSentences: 2).Summarize(article));
    }

    [Fact]
    public void Summarize_IgnoresSentencesWithoutContentWords()
    {
        Article article = Fixtures.ArticleOf("Oil exports rose sharply in March.", "They were there with them about this.");
        Assert.Equal("Oil exports rose sharply in March.", new ExtractiveSummarizer().Summarize(article));
    }

    [Fact]
    public void Summarize_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>("article", () => new ExtractiveSummarizer().Summarize(null!));
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
        Assert.Throws<ArgumentNullException>("html", () => ArticleProcessor.Default.Process(null!));
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

    private sealed class RecordingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    private sealed class GatedReader(Task<string> content) : TextReader
    {
        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) => content.WaitAsync(cancellationToken);
    }

    private sealed class GatedStream(byte[] bytes, Task gate) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Read(buffer.Span);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    private static Task<T> StartUnder<T>(SynchronizationContext context, Func<Task<T>> start)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            return start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task ProcessAsync_FromReader_DoesNotResumeOnTheCallersContext()
    {
        var context = new RecordingContext();
        var content = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reader = new GatedReader(content.Task);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<ArticleDigest> pending = StartUnder(context, () => ArticleProcessor.Default.ProcessAsync(reader, cancellationToken));
        content.SetResult(Html);
        ArticleDigest digest = await pending;

        Assert.Equal("Display Headline", digest.Article.Headline);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task ProcessAsync_FromStream_DoesNotResumeOnTheCallersContext()
    {
        var context = new RecordingContext();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new GatedStream(Encoding.UTF8.GetBytes(Html), gate.Task);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<ArticleDigest> pending = StartUnder(context, () => ArticleProcessor.Default.ProcessAsync(stream, cancellationToken));
        gate.SetResult();
        ArticleDigest digest = await pending;

        Assert.Equal("Display Headline", digest.Article.Headline);
        Assert.Equal(0, context.Posts);
    }

    [Fact]
    public async Task ProcessFilesAsync_DoesNotResumeOnTheCallersContext()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(path, Html, cancellationToken);

        try
        {
            var context = new RecordingContext();
            Task<IReadOnlyList<ArticleDigest>> many = StartUnder(context, () => ArticleProcessor.Default.ProcessFilesAsync([path, path], cancellationToken));
            Task<ArticleDigest> one = StartUnder(context, () => ArticleProcessor.Default.ProcessFileAsync(path, cancellationToken));

            Assert.Equal(2, (await many).Count);
            Assert.Equal("Display Headline", (await one).Article.Headline);
            Assert.Equal(0, context.Posts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProcessAsync_DetectsUtf16ByteOrderMarks()
    {
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Html)];
        using var stream = new MemoryStream(bytes);
        ArticleDigest digest = await ArticleProcessor.Default.ProcessAsync(stream, TestContext.Current.CancellationToken);

        Assert.Equal("Display Headline", digest.Article.Headline);
    }

    [Fact]
    public void Process_WhenNothingIsFound_ExplainsWhy()
    {
        ArticleExtractionException exception = Assert.Throws<ArticleExtractionException>(() => ArticleProcessor.Default.Process("<div>empty</div>"));
        Assert.Equal("No article content was found in the HTML.", exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_ValidatesArguments()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>("reader", () => ArticleProcessor.Default.ProcessAsync((TextReader)null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>("stream", () => ArticleProcessor.Default.ProcessAsync((Stream)null!, cancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>("paths", () => ArticleProcessor.Default.ProcessFilesAsync(null!, cancellationToken));
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
        Assert.Throws<ArgumentNullException>("inputs", () => ArticleFiles.Resolve(null!));
    }

    [Fact]
    public void Resolve_WhenInputIsMissing_ExplainsWhy()
    {
        string missing = Path.Combine(_root, "missing.html");
        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() => ArticleFiles.Resolve([missing]));
        Assert.Equal($"No file or directory exists at '{missing}'.", exception.Message);
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

    [Fact]
    public void Render_ProducesTheExactLayout()
    {
        Article article = Fixtures.ArticleOf("one two three") with { ReportedWordCount = 4 };
        string report = DigestReport.Render("/some/dir/story.html", new ArticleDigest(article, "PROSE", "SUMMARY TEXT"));
        string rule = new('=', 80);

        Assert.Equal(string.Join('\n', rule, "story.html", "Source: PreloadedData  Paragraphs: 1  Words: 3/4", rule, "", "SUMMARY", "", "SUMMARY TEXT", "", "ARTICLE", "", "PROSE", ""), report);
    }

    [Fact]
    public void Render_ValidatesArguments()
    {
        var digest = new ArticleDigest(Fixtures.ArticleOf("x"), "", "");
        Assert.Throws<ArgumentNullException>("path", () => DigestReport.Render(null!, digest));
        Assert.Throws<ArgumentNullException>("digest", () => DigestReport.Render("story.html", null!));
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
        Assert.Equal(222, ArticleFiles.Resolve([Path.Combine(AppContext.BaseDirectory, "nytimes")]).Count);
    }
}

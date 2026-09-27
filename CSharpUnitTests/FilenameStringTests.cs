using CSharpClassLibrary;
using Xunit;

namespace CSharpUnitTests;

public class FilenameStringTests
{
    private const string LongName = "hello_there_I_have_a_surprise_final_final_really.pdf";

    private static double Proportional(string s) => s.Sum(c => c == 'W' ? 3.0 : 1.0);

    [Fact]
    public void Constructor_WhenNameIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FilenameString(null!));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_AssignsFullName(string name)
    {
        Assert.Equal(name, new FilenameString(name).FullName);
    }

    [Fact]
    public void ToString_ReturnsFullName()
    {
        Assert.Equal("report.pdf", new FilenameString("report.pdf").ToString());
    }

    [Theory]
    [InlineData("report.pdf", ".pdf")]
    [InlineData("archive.tar.gz", ".tar.gz")]
    [InlineData("ARCHIVE.TAR.GZ", ".TAR.GZ")]
    [InlineData(".tar.gz", ".gz")]
    [InlineData(".hidden.txt", ".txt")]
    [InlineData(".gitignore", "")]
    [InlineData("notes.", "")]
    [InlineData("noextension", "")]
    [InlineData("Meeting notes v2.0 final", "")]
    [InlineData("", "")]
    public void Extension_IsDetectedCorrectly(string name, string expected)
    {
        Assert.Equal(expected, new FilenameString(name).Extension);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("report.pdf", 10)]
    [InlineData("e\u0301", 1)]
    [InlineData("\U0001F600.png", 5)]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466", 1)]
    public void Length_CountsGraphemeClusters(string name, int expected)
    {
        Assert.Equal(expected, new FilenameString(name).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Truncate_WhenMaxLengthIsLessThanOne_Throws(int maxLength)
    {
        var sut = new FilenameString("report.pdf");
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.Truncate(maxLength));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("a", 1)]
    [InlineData("   ", 3)]
    [InlineData("report.pdf", 10)]
    [InlineData("report.pdf", 50)]
    public void Truncate_WhenNameFits_ReturnsItUnchanged(string name, int maxLength)
    {
        Assert.Equal(name, new FilenameString(name).Truncate(maxLength));
    }

    [Theory]
    [InlineData("abcdefghijklmnopqrstuvwxyz", 10, "abcde…wxyz")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", 11, "abcde…vwxyz")]
    [InlineData(LongName, 20, "hello_ther…eally.pdf")]
    [InlineData(".dockerignore", 8, ".doc…ore")]
    [InlineData("long_file_name.", 10, "long_…ame.")]
    [InlineData("Meeting notes v2.0 final", 10, "Meeti…inal")]
    public void Truncate_RemovesCharactersFromTheMiddle(string name, int maxLength, string expected)
    {
        Assert.Equal(expected, new FilenameString(name).Truncate(maxLength));
    }

    [Theory]
    [InlineData("very_long_name.pdf", 8, "ver….pdf")]
    [InlineData("very_long_name.pdf", 6, "v….pdf")]
    [InlineData("very_long_archive.tar.gz", 12, "very….tar.gz")]
    [InlineData(".very_long_config_file.json", 10, ".ver….json")]
    public void Truncate_WhenBudgetIsTight_KeepsExtensionWhole(string name, int maxLength, string expected)
    {
        Assert.Equal(expected, new FilenameString(name).Truncate(maxLength));
    }

    [Theory]
    [InlineData(5, "ve…df")]
    [InlineData(3, "v…f")]
    [InlineData(2, "v…")]
    [InlineData(1, "…")]
    public void Truncate_WhenExtensionCannotFit_FallsBackToPlainMiddleTruncation(int maxLength, string expected)
    {
        Assert.Equal(expected, new FilenameString("very_long_name.pdf").Truncate(maxLength));
    }

    [Fact]
    public void Truncate_ResultIsExactlyMaxLengthWhenTruncated()
    {
        var sut = new FilenameString(LongName);
        for (int maxLength = 1; maxLength <= LongName.Length + 5; maxLength++)
        {
            string result = sut.Truncate(maxLength);
            Assert.Equal(Math.Min(maxLength, LongName.Length), result.Length);
        }
    }

    [Fact]
    public void Truncate_KeepsExtensionForEveryBudgetThatCanHoldIt()
    {
        var sut = new FilenameString("very_long_name.pdf");

        for (int maxLength = 6; maxLength <= 18; maxLength++)
        {
            Assert.EndsWith(".pdf", sut.Truncate(maxLength));
        }
    }

    [Fact]
    public void Truncate_KeepsNamesThatDifferOnlyAtTheEndDistinguishable()
    {
        var really = new FilenameString("hello_there_I_have_a_surprise_final_final_really.pdf");
        var v2 = new FilenameString("hello_there_I_have_a_surprise_final_final_v2.pdf");

        Assert.NotEqual(really.Truncate(20), v2.Truncate(20));
    }

    public static TheoryData<string> Graphemes =>
    [
        "\U0001F600",
        "e\u0301",
        "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466",
        "\U0001F1FA\U0001F1F8",
    ];

    [Theory]
    [MemberData(nameof(Graphemes))]
    public void Truncate_NeverSplitsGraphemeClusters(string grapheme)
    {
        string name = string.Concat(Enumerable.Repeat(grapheme, 12)) + ".png";
        string expected = string.Concat(Enumerable.Repeat(grapheme, 5)) + "…" + ".png";

        Assert.Equal(expected, new FilenameString(name).Truncate(10));
    }

    [Fact]
    public void TruncateByWidth_WithMonospaceMeasure_MatchesCharacterCount()
    {
        var sut = new FilenameString(LongName);
        static double Monospace(string s) => s.Length * 7.0;

        Assert.Equal(sut.Truncate(20), sut.Truncate(140.0, Monospace));
    }

    [Fact]
    public void TruncateByWidth_WithProportionalMeasure_KeepsFewerWideCharacters()
    {
        var sut = new FilenameString("WWWWWWWWaaaaaaaa");

        Assert.Equal("WWW…aa", sut.Truncate(12.0, Proportional));
    }

    [Fact]
    public void TruncateByWidth_WhenKeepingExtensionNarrowsResult_StillFindsLongestFit()
    {
        var sut = new FilenameString("WWWWWWWW.pdf");

        Assert.Equal("W….pdf", sut.Truncate(8.0, Proportional));
    }

    [Fact]
    public void TruncateByWidth_WhenEvenEllipsisDoesNotFit_ReturnsEllipsis()
    {
        var sut = new FilenameString("report.pdf");
        Assert.Equal(FilenameString.Ellipsis, sut.Truncate(0.5, s => s.Length));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void TruncateByWidth_WhenWidthIsInvalid_Throws(double maxWidth)
    {
        var sut = new FilenameString("report.pdf");
        Assert.Throws<ArgumentOutOfRangeException>(() => sut.Truncate(maxWidth, s => s.Length));
    }

    [Fact]
    public void TruncateByWidth_WhenMeasureIsNull_Throws()
    {
        var sut = new FilenameString("report.pdf");
        Assert.Throws<ArgumentNullException>(() => sut.Truncate(10.0, null!));
    }
}

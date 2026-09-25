using CSharpClassLibrary;
using Xunit;

namespace CSharpUnitTests;

public class FilenameStringTests
{
    [Theory]
    [InlineData("ValidName.txt")]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_AssignsFullName(string expected)
    {
        var sut = new FilenameString(expected);
        Assert.Equal(expected, sut.FullName);
    }

    [Fact]
    public void Constructor_AcceptsNull()
    {
        var sut = new FilenameString(null!);
        Assert.Null(sut.FullName);
    }

    [Fact]
    public void EllipsisName_WhenFullNameIsNull_ReturnsEmptyString()
    {
        var sut = new FilenameString(null!);
        Assert.Equal(string.Empty, sut.EllipsisName);
    }

    [Fact]
    public void EllipsisName_WhenLengthIsZero_ReturnsEmptyString()
    {
        var sut = new FilenameString("");
        Assert.Equal(string.Empty, sut.EllipsisName);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void EllipsisName_WhenWhitespace_ReturnsEmptyString(string input)
    {
        var sut = new FilenameString(input);
        Assert.Equal("", sut.EllipsisName);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("12345")]
    [InlineData("12345.txt")]
    [InlineData("1234567890")]
    [InlineData("1234567890.pdf")]
    public void EllipsisName_WhenBaseNameIsTenOrLess_ReturnsFullName(string input)
    {
        var sut = new FilenameString(input);
        Assert.Equal(input, sut.EllipsisName);
    }

    [Theory]
    [InlineData("12345678901", "1234...901")]
    [InlineData("very_long_name", "very...ame")]
    public void EllipsisName_WhenBaseNameIsGreaterThanTen_WithoutExtension_ReturnsMiddleEllipsis(string input, string expected)
    {
        var sut = new FilenameString(input);
        Assert.Equal(expected, sut.EllipsisName);
    }

    [Theory]
    [InlineData("12345678901.txt", "1234...901.txt")]
    [InlineData("very_long_name.pdf", "very...ame.pdf")]
    [InlineData("ScottArthurJenson.pdf", "Scot...son.pdf")]
    [InlineData("ScottArthurJenson final for real (2012).pdf", "Scot...12).pdf")]
    public void EllipsisName_WhenBaseNameIsGreaterThanTen_WithExtension_ReturnsMiddleEllipsisPreservingExtension(string input, string expected)
    {
        var sut = new FilenameString(input);
        Assert.Equal(expected, sut.EllipsisName);
    }

    [Theory]
    [InlineData(".gitignore", ".gitignore")]
    [InlineData(".dockerignore", ".doc...ore")]
    [InlineData(".very_long_hidden_file", ".ver...ile")]
    public void EllipsisName_WithHiddenFileAndNoOtherDots_TreatsAsBaseNameAndTruncates(string input, string expected)
    {
        var sut = new FilenameString(input);
        Assert.Equal(expected, sut.EllipsisName);
    }

    [Theory]
    [InlineData(".hidden.txt")]
    [InlineData(".short.longextension")]
    public void EllipsisName_WithHiddenFileAndExtension_WhenBaseNameIsShort_ReturnsFullName(string input)
    {
        var sut = new FilenameString(input);
        Assert.Equal(input, sut.EllipsisName);
    }

    [Theory]
    [InlineData(".super_long_hidden.txt", ".sup...den.txt")]
    [InlineData(".very_long_config_file.json", ".ver...ile.json")]
    public void EllipsisName_WithHiddenFileAndExtension_WhenBaseNameIsLong_TruncatesBaseName(string input, string expected)
    {
        var sut = new FilenameString(input);
        Assert.Equal(expected, sut.EllipsisName);
    }

    [Theory]
    [InlineData("archive.tar.gz", "archive.tar.gz")]
    [InlineData("very_long_archive.tar.gz", "very...ive.tar.gz")]
    [InlineData("backup_database.tar.bz2", "back...ase.tar.bz2")]
    public void EllipsisName_WithCompoundExtension_PreservesFullCompoundExtension(string input, string expected)
    {
        var sut = new FilenameString(input);
        Assert.Equal(expected, sut.EllipsisName);
    }

    [Theory]
    [InlineData("long_file_name.", "long...ame.")]
    public void EllipsisName_WithTrailingDot_PreservesTrailingDot(string input, string expected)
    {
        var sut = new FilenameString(input);
        Assert.Equal(expected, sut.EllipsisName);
    }
}

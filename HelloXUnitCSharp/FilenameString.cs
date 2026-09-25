using System;
using System.IO;
using Xunit;

namespace HelloXUnitCSharp;

public class FilenameString
{
    public string FullName { get; }
    private string _Ellipsis => "...";
    private static readonly int _EllipsisLength = 10;

    public FilenameString(string fullName)
    {
        FullName = fullName;
    }

    public string EllipsisName => GetEllipsisName();

    private string GetEllipsisName()
    {
        if (string.IsNullOrEmpty(FullName))
        {
            return FullName ?? string.Empty;
        }

        string baseName = Path.GetFileNameWithoutExtension(FullName);

        if (baseName.Length <= _EllipsisLength)
        {
            return FullName;
        }

        return GetScottJensenEllipsis();
    }

    private string GetScottJensenEllipsis()
    {
        string extension = Path.GetExtension(FullName);
        string baseName = Path.GetFileNameWithoutExtension(FullName);

        int charsLeft = _EllipsisLength - _Ellipsis.Length;
        int front = (int)Math.Ceiling(charsLeft / 2.0);
        int back = charsLeft - front;

        string initialPortion = baseName.Substring(0, front);
        string laterPortion = baseName.Substring(baseName.Length - back);

        return initialPortion + _Ellipsis + laterPortion + extension;
    }

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
        public void EllipsisName_WhenWhitespace_ReturnsWhitespace(string input)
        {
            var sut = new FilenameString(input);
            Assert.Equal(input, sut.EllipsisName);
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
        [InlineData("verylongname", "very...ame")]
        public void EllipsisName_WhenBaseNameIsGreaterThanTen_WithoutExtension_ReturnsMiddleEllipsis(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData("12345678901.txt", "1234...901.txt")]
        [InlineData("verylongname.pdf", "very...ame.pdf")]
        [InlineData("ScottArthurJenson.pdf", "Scot...son.pdf")]
        public void EllipsisName_WhenBaseNameIsGreaterThanTen_WithExtension_ReturnsMiddleEllipsisPreservingExtension(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData("archive.tar.gz", "arch...tar.gz")]
        [InlineData("backup.1234567890.zip", "back...890.zip")]
        public void EllipsisName_WithMultipleDots_TreatsLastDotAsExtension(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData(".gitignore", ".gitignore")]
        [InlineData(".dockerignore", ".dockerignore")]
        public void EllipsisName_WithOnlyExtension_ReturnsFullName(string input)
        {
            var sut = new FilenameString(input);
            Assert.Equal(input, sut.EllipsisName);
        }

        [Theory]
        [InlineData(".hidden.longextension", ".hid...ion")]
        public void EllipsisName_WithHiddenFileAndExtension_TruncatesBaseName(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData("longfilename.", "long...ame.")]
        public void EllipsisName_WithTrailingDot_PreservesTrailingDot(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Fact]
        public void PrivateProperty_Ellipsis_ReturnsExactlyThreeDots()
        {
            var sut = new FilenameString("test");
            Assert.Equal("...", sut._Ellipsis);
        }

        [Fact]
        public void PrivateField_EllipsisLength_IsStaticallySetToTen()
        {
            Assert.Equal(10, FilenameString._EllipsisLength);
        }

        [Fact]
        public void GetScottJensenEllipsis_WhenCalledDirectly_CalculatesCorrectly()
        {
            var sut = new FilenameString("12345678901.pdf");
            Assert.Equal("1234...901.pdf", sut.GetScottJensenEllipsis());
        }

        [Fact]
        public void GetScottJensenEllipsis_ThrowsNullReferenceException_WhenFullNameIsNull()
        {
            var sut = new FilenameString(null!);
            Assert.Throws<ArgumentNullException>(() => sut.GetScottJensenEllipsis());
        }
    }
}

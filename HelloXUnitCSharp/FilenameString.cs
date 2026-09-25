using System;
using Xunit;

namespace HelloXUnitCSharp;

public class FilenameString(string fullName)
{
    public string FullName { get; } = fullName;
    private string Ellipsis => "...";
    private static readonly int EllipsisLength = 10;

    public string EllipsisName => GetEllipsisName();

    private string GetEllipsisName()
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            return "";
        }

        ParseNameAndExtension(out string baseName, out string extension);

        if (baseName.Length <= EllipsisLength)
        {
            return FullName;
        }

        return GetScottJensenEllipsis(baseName, extension);
    }

    private void ParseNameAndExtension(out string baseName, out string extension)
    {
        if (string.IsNullOrEmpty(FullName))
        {
            baseName = "";
            extension = "";
            return;
        }

        int firstDot = FullName.IndexOf('.');
        int lastDot = FullName.LastIndexOf('.');

        if (firstDot == 0 && lastDot == 0)
        {
            baseName = FullName;
            extension = "";
            return;
        }

        string[] compoundExtensions = { ".tar.gz", ".tar.bz2", ".tar.xz" };
        foreach (var compExt in compoundExtensions)
        {
            if (FullName.EndsWith(compExt, StringComparison.OrdinalIgnoreCase) && FullName.Length > compExt.Length)
            {
                baseName = FullName[..^compExt.Length];
                extension = FullName[^compExt.Length..];
                return;
            }
        }

        if (lastDot > 0)
        {
            baseName = FullName[..lastDot];
            extension = FullName[lastDot..];
        }
        else
        {
            baseName = FullName;
            extension = "";
        }
    }

    private string GetScottJensenEllipsis(string baseName, string extension)
    {
        if (baseName.Length <= EllipsisLength)
        {
            return baseName + extension;
        }

        int charsLeft = EllipsisLength - Ellipsis.Length;
        
        if (charsLeft <= 0)
        {
            return Ellipsis + extension;
        }

        int front = (int)Math.Ceiling(charsLeft / 2.0);
        int back = charsLeft - front;

        string initialPortion = baseName[..front];
        string laterPortion = baseName[^back..];

        return initialPortion + Ellipsis + laterPortion + extension;
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
        public void EllipsisName_WhenBaseNameIsGreaterThanTen_WithExtension_ReturnsMiddleEllipsisPreservingExtension(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData(".gitignore", ".git...ore")]
        [InlineData(".dockerignore", ".doc...ore")]
        [InlineData(".verylonghiddenfile", ".ver...ile")]
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
        [InlineData(".superlonghidden.txt", ".sup...den.txt")]
        [InlineData(".verylongconfigfile.json", ".ver...ile.json")]
        public void EllipsisName_WithHiddenFileAndExtension_WhenBaseNameIsLong_TruncatesBaseName(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData("archive.tar.gz", "archive.tar.gz")] 
        [InlineData("verylongarchive.tar.gz", "very...ive.tar.gz")]
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

        [Theory]
        [InlineData("file.txt", "file", ".txt")]
        [InlineData(".gitignore", ".gitignore", "")]
        [InlineData(".hidden.txt", ".hidden", ".txt")]
        [InlineData("archive.tar.gz", "archive", ".tar.gz")]
        [InlineData("noextension", "noextension", "")]
        [InlineData("trailingdot.", "trailingdot", ".")]
        [InlineData(".tar.gz", ".tar", ".gz")] 
        public void ParseNameAndExtension_ParsesCorrectly(string input, string expectedBase, string expectedExt)
        {
            var sut = new FilenameString(input);
            sut.ParseNameAndExtension(out string baseName, out string extension);
            
            Assert.Equal(expectedBase, baseName);
            Assert.Equal(expectedExt, extension);
        }

        [Theory]
        [InlineData("verylongbasename", ".txt", "very...ame.txt")]
        [InlineData("12345678901", "", "1234...901")]
        [InlineData("short", ".pdf", "short.pdf")]
        public void GetScottJensenEllipsis_DirectCall_CalculatesCorrectly(string baseName, string ext, string expected)
        {
            var sut = new FilenameString("dummy");
            var result = sut.GetScottJensenEllipsis(baseName, ext);
            
            Assert.Equal(expected, result);
        }

        [Fact]
        public void PrivateProperty_Ellipsis_ReturnsExactlyThreeDots()
        {
            var sut = new FilenameString("test");
            Assert.Equal("...", sut.Ellipsis);
        }

        [Fact]
        public void PrivateField_EllipsisLength_IsStaticallySetToTen()
        {
            Assert.Equal(10, FilenameString.EllipsisLength);
        }
    }
}

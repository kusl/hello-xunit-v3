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

        int lastDotIndex = FullName.LastIndexOf('.');
        string baseName = lastDotIndex > 0 ? FullName[..lastDotIndex] : FullName;

        if (baseName.Length <= EllipsisLength)
        {
            return FullName;
        }

        return GetScottJensenEllipsis();
    }

    private string GetScottJensenEllipsis()
    {
        ArgumentNullException.ThrowIfNull(FullName);

        int lastDotIndex = FullName.LastIndexOf('.');
        string baseName;
        string extension;

        if (lastDotIndex > 0)
        {
            baseName = FullName[..lastDotIndex];
            extension = FullName[lastDotIndex..];
        }
        else
        {
            baseName = FullName;
            extension = "";
        }

        int charsLeft = EllipsisLength - Ellipsis.Length;
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
        [InlineData("archive.tar.gz", "arch...tar.gz")]
        [InlineData("backup.1234567890.zip", "back...890.zip")]
        public void EllipsisName_WithMultipleDots_TreatsLastDotAsExtension(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
        }

        [Theory]
        [InlineData(".gitignore")]
        [InlineData(".dockerignore")]
        [InlineData(".hidden.longextension")]
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
        [InlineData("long_file_name.", "long...ame.")]
        public void EllipsisName_WithTrailingDot_PreservesTrailingDot(string input, string expected)
        {
            var sut = new FilenameString(input);
            Assert.Equal(expected, sut.EllipsisName);
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

        [Fact]
        public void GetScottJensenEllipsis_WhenCalledDirectly_CalculatesCorrectly()
        {
            var sut = new FilenameString("12345678901.pdf");
            Assert.Equal("1234...901.pdf", sut.GetScottJensenEllipsis());
        }

        [Fact]
        public void GetScottJensenEllipsis_ThrowsArgumentNullException_WhenFullNameIsNull()
        {
            var sut = new FilenameString(null!);
            Assert.Throws<ArgumentNullException>(sut.GetScottJensenEllipsis);
        }
    }
}

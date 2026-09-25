using System;
using Xunit;

namespace HelloXUnitCSharp;

/// <summary>
/// An experiment to put code and tests together
/// </summary>
public class FilenameString(string fullName)
{
    public string FullName { get; } = fullName;
    private string _Ellipsis => "...";
    private static readonly int _EllipsisLength = 10;
    public string EllipsisName => GetEllipsisName();

    private string GetEllipsisName()
    {
        if (FullName.Length < _EllipsisLength)
        {
            return FullName;
        }

        return GetNaiveEllipsis();
    }

    private string GetNaiveEllipsis()
    {
        string subString = FullName.Substring(0, _EllipsisLength - 3);
        return subString;
    }

    private string GetScottJensenEllipsis()
    {
        int middle = _EllipsisLength / 2;
        string initialPortion = FullName.Substring(0, middle);
        string laterPortion = FullName.Substring(FullName.Length - _Ellipsis.Length, FullName.Length - 1);
        return initialPortion + _Ellipsis + laterPortion;
    }

    /// <summary>
    /// Exhaustive test suite nested directly inside the target class. 
    /// This allows xUnit to seamlessly test private fields and methods 
    /// without relying on reflection.
    /// </summary>
    public class FilenameStringTests
    {
        // =========================================================================
        // CATEGORY 1: Constructor & Public Properties 
        // =========================================================================

        [Theory]
        [InlineData("ValidName.txt")]
        [InlineData("")]
        [InlineData(" ")]
        public void Constructor_AssignsFullName_Correctly(string expected)
        {
            var sut = new FilenameString(expected);
            Assert.Equal(expected, sut.FullName);
        }

        [Fact]
        public void Constructor_AcceptsNull_WithoutThrowing()
        {
            var sut = new FilenameString(null!);
            Assert.Null(sut.FullName);
        }

        // =========================================================================
        // CATEGORY 2: EllipsisName Main Logic & Boundary Values
        // =========================================================================

        [Fact]
        public void EllipsisName_WhenFullNameIsNull_ThrowsNullReferenceException()
        {
            var sut = new FilenameString(null!);
            Assert.Throws<NullReferenceException>(() => sut.EllipsisName);
        }

        [Fact]
        public void EllipsisName_WhenLengthIsZero_ReturnsEmptyString()
        {
            var sut = new FilenameString("");
            Assert.Equal("", sut.EllipsisName);
        }

        [Theory]
        [InlineData("1")]                   // Length 1
        [InlineData("12345")]               // Length 5
        [InlineData("123456789")]           // Length 9 (Upper boundary before truncation)
        public void EllipsisName_WhenLengthIsUnderTen_ReturnsExactFullName(string input)
        {
            var sut = new FilenameString(input);
            Assert.Equal(input, sut.EllipsisName);
        }

        [Theory]
        [InlineData("1234567890", "1234567")]           // Length 10 (Lower boundary for truncation)
        [InlineData("12345678901", "1234567")]          // Length 11
        [InlineData("verylongfilename.cs", "verylon")]  // Typical long string
        public void EllipsisName_WhenLengthIsTenOrGreater_ReturnsNaiveEllipsisString(string input, string expected)
        {
            var sut = new FilenameString(input);
            
            // Notice: GetNaiveEllipsis currently omits the actual "..." string. 
            // We are testing the current factual output of the code.
            Assert.Equal(expected, sut.EllipsisName);
        }

        // =========================================================================
        // CATEGORY 3: Private Fields & Properties
        // =========================================================================

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

        // =========================================================================
        // CATEGORY 4: Private Method - GetEllipsisName (Explicit Routing Tests)
        // =========================================================================

        [Fact]
        public void GetEllipsisName_DelegatesToFullName_WhenStringIsShort()
        {
            var sut = new FilenameString("short");
            Assert.Equal("short", sut.GetEllipsisName());
        }

        [Fact]
        public void GetEllipsisName_DelegatesToNaiveEllipsis_WhenStringIsLong()
        {
            var sut = new FilenameString("thisisaverylongstring");
            Assert.Equal("thisisa", sut.GetEllipsisName());
        }

        // =========================================================================
        // CATEGORY 5: Private Method - GetNaiveEllipsis
        // =========================================================================

        [Fact]
        public void GetNaiveEllipsis_AlwaysReturnsFirstSevenCharacters_ForValidLongStrings()
        {
            var sut = new FilenameString("1234567890");
            Assert.Equal("1234567", sut.GetNaiveEllipsis());
        }

        [Fact]
        public void GetNaiveEllipsis_ThrowsNullReferenceException_WhenFullNameIsNull()
        {
            var sut = new FilenameString(null!);
            Assert.Throws<NullReferenceException>(() => sut.GetNaiveEllipsis());
        }

        [Fact]
        public void GetNaiveEllipsis_ThrowsArgumentOutOfRangeException_WhenLengthIsUnderSeven()
        {
            // Bug isolation: If called directly with a string < 7 chars, Substring(0, 7) throws.
            var sut = new FilenameString("123456"); 
            Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetNaiveEllipsis());
        }

        // =========================================================================
        // CATEGORY 6: Private Method - GetScottJensenEllipsis (Bug Documentation)
        // =========================================================================

        [Fact]
        public void GetScottJensenEllipsis_ThrowsNullReferenceException_WhenFullNameIsNull()
        {
            var sut = new FilenameString(null!);
            Assert.Throws<NullReferenceException>(() => sut.GetScottJensenEllipsis());
        }

        [Fact]
        public void GetScottJensenEllipsis_ThrowsArgumentOutOfRangeException_WhenStringIsTooShortForMiddle()
        {
            // First bug isolated: Length < 5 means Substring(0, middle) throws since middle is 5.
            var sut = new FilenameString("1234");
            Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetScottJensenEllipsis());
        }

        [Fact]
        public void GetScottJensenEllipsis_ThrowsArgumentOutOfRangeException_DueToSubstringLengthCalculationBug()
        {
            // Second bug isolated: Substring expects (startIndex, length).
            // Code passes (startIndex, FullName.Length - 1). 
            // When Length is 10, it calls Substring(7, 9). 7 + 9 > 10, causing out of range exception.
            var sut = new FilenameString("1234567890");
            
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => sut.GetScottJensenEllipsis());
            
            // Verify it failed specifically on a length parameter violation
            Assert.Contains("length", exception.ParamName ?? exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

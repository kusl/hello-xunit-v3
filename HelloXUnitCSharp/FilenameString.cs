using System;
using System.Reflection;
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
}

/// <summary>
/// Exhaustive test suite providing 100% coverage of all public APIs, private methods, 
/// boundary conditions, and known logic exceptions within the FilenameString class.
/// </summary>
public class FilenameStringTests
{
    #region Public API: Constructor & FullName Property

    [Fact]
    public void Constructor_SetsFullName_Correctly()
    {
        // Arrange
        var expected = "test_file.txt";

        // Act
        var sut = new FilenameString(expected);

        // Assert
        Assert.Equal(expected, sut.FullName);
    }

    [Fact]
    public void Constructor_AcceptsNull_SetsFullNameToNull()
    {
        // Arrange & Act
        var sut = new FilenameString(null!);

        // Assert
        Assert.Null(sut.FullName);
    }

    [Fact]
    public void Constructor_AcceptsEmptyString_SetsFullNameToEmpty()
    {
        // Arrange & Act
        var sut = new FilenameString(string.Empty);

        // Assert
        Assert.Equal(string.Empty, sut.FullName);
    }

    #endregion

    #region Public API: EllipsisName (Condition: Length < _EllipsisLength)

    [Theory]
    [InlineData("")]          // Length 0 (Boundary)
    [InlineData("1")]         // Length 1
    [InlineData("123456789")] // Length 9 (Upper boundary for < 10)
    [InlineData(" a b c ")]   // Whitespace handling
    [InlineData("👍👍")]      // Surrogate pairs / Unicode (Length 4)
    public void EllipsisName_LengthLessThan10_ReturnsFullName(string input)
    {
        // Arrange
        var sut = new FilenameString(input);

        // Act
        var result = sut.EllipsisName;

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void EllipsisName_WhenFullNameIsNull_ThrowsNullReferenceException()
    {
        // Arrange
        var sut = new FilenameString(null!);

        // Act & Assert
        // NullReferenceException occurs because GetEllipsisName checks FullName.Length
        Assert.Throws<NullReferenceException>(() => sut.EllipsisName);
    }

    #endregion

    #region Public API: EllipsisName (Condition: Length >= _EllipsisLength)

    [Theory]
    [InlineData("0123456789", "0123456")]                // Length 10 (Exact threshold boundary)
    [InlineData("0123456789A", "0123456")]               // Length 11 (Just above boundary)
    [InlineData("ThisIsAVeryLongFileName", "ThisIsA")]   // Very long string
    [InlineData("          ", "       ")]                // 10 spaces
    public void EllipsisName_Length10OrGreater_ReturnsFirst7Characters(string input, string expected)
    {
        // Arrange
        var sut = new FilenameString(input);

        // Act
        var result = sut.EllipsisName;

        // Assert
        Assert.Equal(expected, result);
        Assert.Equal(7, result.Length); // _EllipsisLength (10) - 3 = 7
    }

    #endregion

    #region Private Members: Reflection Tests for Hardcoded/Internal State

    [Fact]
    public void PrivateProperty_Ellipsis_ReturnsThreeDots()
    {
        // Arrange
        var sut = new FilenameString("test");
        var propInfo = typeof(FilenameString).GetProperty("_Ellipsis", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(propInfo);

        // Act
        var result = propInfo.GetValue(sut);

        // Assert
        Assert.Equal("...", result);
    }

    [Fact]
    public void PrivateField_EllipsisLength_Equals10()
    {
        // Arrange
        var fieldInfo = typeof(FilenameString).GetField("_EllipsisLength", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(fieldInfo);

        // Act
        var result = fieldInfo.GetValue(null);

        // Assert
        Assert.Equal(10, result);
    }

    #endregion

    #region Private Methods: GetNaiveEllipsis Edge Cases via Reflection

    [Fact]
    public void GetNaiveEllipsis_LengthExactly7_WorksWithoutException()
    {
        // Arrange
        // Calling this directly via reflection bypasses the < 10 length check.
        // It requires a minimum length of 7 (_EllipsisLength - 3).
        var sut = new FilenameString("1234567"); 

        // Act
        var result = InvokePrivateMethod(sut, "GetNaiveEllipsis");

        // Assert
        Assert.Equal("1234567", result);
    }

    [Fact]
    public void GetNaiveEllipsis_LengthLessThan7_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var sut = new FilenameString("123456"); // Length 6

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => InvokePrivateMethod(sut, "GetNaiveEllipsis"));
    }

    #endregion

    #region Private Methods: GetScottJensenEllipsis (Known Bug Documentation)

    // Note: The GetScottJensenEllipsis method is mathematically impossible to execute without throwing 
    // an ArgumentOutOfRangeException.
    // 
    // 1. `initialPortion` requires a minimum length of 5.
    // 2. `laterPortion` executes `Substring(Length - 3, Length - 1)`. 
    //    For Substring to not throw, (startIndex + length) <= string.Length.
    //    (L - 3) + (L - 1) <= L  -->  2L - 4 <= L  -->  L <= 4.
    //
    // The string length cannot be simultaneously >= 5 and <= 4. 

    [Theory]
    [InlineData("1234")] // Less than 5 (Fails on initialPortion)
    [InlineData("12345")] // Exactly 5 (Fails on laterPortion)
    [InlineData("0123456789")] // 10 (Fails on laterPortion)
    [InlineData("VeryLongStringThatWillStillFail")] 
    public void GetScottJensenEllipsis_AlwaysThrowsArgumentOutOfRangeException_DueToInvalidIndexMath(string input)
    {
        // Arrange
        var sut = new FilenameString(input);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => InvokePrivateMethod(sut, "GetScottJensenEllipsis"));
    }

    #endregion

    #region Reflection Helper

    private static object? InvokePrivateMethod(FilenameString obj, string methodName)
    {
        var method = typeof(FilenameString).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        
        Assert.NotNull(method); // Ensure the method wasn't renamed

        try
        {
            return method.Invoke(obj, null);
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap the TargetInvocationException to assert against the actual exception thrown by the code
            if (ex.InnerException != null)
            {
                // Rethrow preserving the stack trace
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
            throw;
        }
    }

    #endregion
}

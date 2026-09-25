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

using System.Globalization;

namespace CSharpClassLibrary;

public sealed class FilenameString
{
    public const string Ellipsis = "\u2026";

    private static readonly string[] CompoundExtensions = [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.zst"];

    private readonly int[] _boundaries;
    private readonly int _extensionLength;

    public FilenameString(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);

        FullName = fullName;
        Extension = GetExtension(fullName);
        _boundaries = GetTextElementBoundaries(fullName);
        _extensionLength = CountTextElements(Extension);
    }

    public string FullName { get; }

    public string Extension { get; }

    public int Length => _boundaries.Length - 1;

    public string Truncate(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        return Length <= maxLength ? FullName : BuildTruncated(maxLength - 1);
    }

    public string Truncate(double maxWidth, Func<string, double> measureWidth)
    {
        if (double.IsNaN(maxWidth) || maxWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "Width must be a non-negative number.");
        }

        ArgumentNullException.ThrowIfNull(measureWidth);

        if (measureWidth(FullName) <= maxWidth)
        {
            return FullName;
        }

        for (int keep = Length - 1; keep > 0; keep--)
        {
            string candidate = BuildTruncated(keep);
            if (measureWidth(candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        return Ellipsis;
    }

    public override string ToString() => FullName;

    private string BuildTruncated(int keep)
    {
        int head = (keep + 1) / 2;
        int tail = keep - head;

        if (_extensionLength > tail && _extensionLength < keep)
        {
            tail = _extensionLength;
            head = keep - tail;
        }

        string front = FullName[.._boundaries[head]];
        string back = FullName[_boundaries[Length - tail]..];
        return front + Ellipsis + back;
    }

    private static string GetExtension(string name)
    {
        foreach (string compound in CompoundExtensions)
        {
            if (name.Length > compound.Length && name.EndsWith(compound, StringComparison.OrdinalIgnoreCase))
            {
                return name[^compound.Length..];
            }
        }

        int lastDot = name.LastIndexOf('.');

        if (lastDot <= 0 || lastDot == name.Length - 1)
        {
            return "";
        }

        string extension = name[lastDot..];

        foreach (char c in extension)
        {
            if (char.IsWhiteSpace(c))
            {
                return "";
            }
        }

        return extension;
    }

    private static int[] GetTextElementBoundaries(string text)
    {
        var boundaries = new List<int> { 0 };
        int index = 0;
        while (index < text.Length)
        {
            index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
            boundaries.Add(index);
        }

        return [.. boundaries];
    }

    private static int CountTextElements(string text)
    {
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
            count++;
        }

        return count;
    }
}

using System.Globalization;

namespace CSharpClassLibrary;

/// <summary>
/// A file name that can be shortened for display the way the macOS Finder does it:
/// characters are removed from the <em>middle</em> and replaced with a single ellipsis
/// character, so both the start of the name and its end (which usually holds the
/// distinguishing part, such as "final_v2", and the extension) stay visible.
/// </summary>
/// <remarks>
/// <para>
/// Finder does not use a fixed character count. It truncates to whatever width the label
/// or column has available, measured in points in the current font (AppKit's
/// middle-truncation line break mode). <see cref="Truncate(double, Func{string, double})"/>
/// models that with a caller-supplied width function; <see cref="Truncate(int)"/> is the
/// same algorithm with every user-perceived character (grapheme cluster) counted as width 1.
/// </para>
/// <para>
/// Truncation never splits a grapheme cluster, so emoji, flags and accented letters built
/// from combining marks are either kept whole or removed whole.
/// </para>
/// <para>
/// On top of plain middle truncation, the extension is kept whole whenever the budget
/// allows at least one leading character plus the extension, so "report.pdf" never
/// turns into "rep…df" when "re….pdf" would fit.
/// </para>
/// </remarks>
public sealed class FilenameString
{
    /// <summary>The horizontal ellipsis character (U+2026) that macOS uses, not three periods.</summary>
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

    /// <summary>The complete, untruncated file name.</summary>
    public string FullName { get; }

    /// <summary>
    /// The extension including its leading dot (for example ".pdf" or ".tar.gz"), or an
    /// empty string when the name has none. A leading dot (".gitignore"), a trailing dot
    /// ("notes.") and a "suffix" containing whitespace ("Minutes v2.0 final") are not
    /// treated as extensions.
    /// </summary>
    public string Extension { get; }

    /// <summary>The number of user-perceived characters (grapheme clusters) in the name.</summary>
    public int Length => _boundaries.Length - 1;

    /// <summary>
    /// Shortens the name to at most <paramref name="maxLength"/> user-perceived characters,
    /// including the ellipsis, by removing characters from the middle.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is less than 1.</exception>
    public string Truncate(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);
        return Length <= maxLength ? FullName : BuildTruncated(maxLength - 1);
    }

    /// <summary>
    /// Shortens the name so that <paramref name="measureWidth"/> of the result is at most
    /// <paramref name="maxWidth"/>, keeping as many characters as possible. This is how Finder
    /// behaves: the available width comes from the column or icon label, and the width
    /// function would measure text in the font being drawn.
    /// </summary>
    /// <param name="maxWidth">The available width, in whatever unit <paramref name="measureWidth"/> returns.</param>
    /// <param name="measureWidth">Measures the rendered width of a string.</param>
    /// <returns>
    /// The full name if it fits; otherwise the longest middle-truncated form that fits. If not
    /// even the ellipsis on its own fits, the ellipsis is returned, since there is nothing shorter
    /// that still signals a name is there.
    /// </returns>
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

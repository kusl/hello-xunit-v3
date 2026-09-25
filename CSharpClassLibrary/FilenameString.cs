namespace HelloXUnitCSharp;

public class FilenameString(string fullName)
{
    public string FullName { get; } = fullName;
    public static string Ellipsis => "...";
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
}

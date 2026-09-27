using System.Text;
using CSharpClassLibrary;

// The ellipsis is U+2026; make sure it survives on consoles that don't default to UTF-8.
Console.OutputEncoding = Encoding.UTF8;

string[] names =
[
    "hello_there_I_have_a_surprise_final_final_really.pdf",
    "hello_there_I_have_a_surprise_final_final_v2.pdf",
    "Quarterly Report 2026 (draft).docx",
    "very_long_archive.tar.gz",
];

// Stand-ins for different Finder column widths, in characters.
int[] widths = [40, 24, 16, 10];

foreach (string name in names)
{
    var filename = new FilenameString(name);
    Console.WriteLine(filename.FullName);

    foreach (int width in widths)
    {
        string middle = filename.Truncate(width);
        string end = TruncateEnd(name, width);
        Console.WriteLine($"  {width,2}  middle: {middle,-40}  end: {end}");
    }

    Console.WriteLine();
}

// End truncation, for comparison only. Both "final_final_…" names above collapse to the
// same string this way, which is exactly the problem middle truncation solves.
// (Char-based, which is fine for these ASCII sample names.)
static string TruncateEnd(string name, int maxLength) =>
    name.Length <= maxLength ? name : name[..(maxLength - 1)] + FilenameString.Ellipsis;

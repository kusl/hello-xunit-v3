using System.Text;
using CSharpClassLibrary;

Console.OutputEncoding = Encoding.UTF8;

string[] names =
[
    "hello_there_I_have_a_surprise_final_final_really.pdf",
    "hello_there_I_have_a_surprise_final_final_v2.pdf",
    "Quarterly Report 2026 (draft).docx",
    "very_long_archive.tar.gz",
];

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

static string TruncateEnd(string name, int maxLength) =>
    name.Length <= maxLength ? name : name[..(maxLength - 1)] + FilenameString.Ellipsis;


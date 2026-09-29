using System.Text;
using CSharpClassLibrary;

Console.OutputEncoding = Encoding.UTF8;

string[] inputs = args.Length > 0 ? args : [Path.Combine(AppContext.BaseDirectory, "nytimes")];

try
{
    IReadOnlyList<string> paths = ArticleFiles.Resolve(inputs);
    IReadOnlyList<ArticleDigest> digests = await ArticleProcessor.Default.ProcessFilesAsync(paths);

    foreach ((string path, ArticleDigest digest) in paths.Zip(digests))
    {
        Console.WriteLine(DigestReport.Render(path, digest));
    }

    return 0;
}
catch (Exception exception) when (exception is FileNotFoundException or ArticleExtractionException)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}

using System.Reflection;
using System.Text;
using CSharpClassLibrary;

Console.OutputEncoding = Encoding.UTF8;

string[] inputs = args.Length > 0 ? args : [DefaultSamplesDirectory()];

try
{
    IReadOnlyList<string> paths = ArticleFiles.Resolve(inputs);
    IReadOnlyList<ArticleDigest> digests = await ArticleProcessor.Default.ProcessFilesAsync(paths);

    foreach ((string path, ArticleDigest digest) in paths.Zip(digests))
    {
        string report = DigestReport.Render(path, digest);
        Console.WriteLine(report);

        if (!string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".txt"), report);
        }
    }

    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArticleExtractionException)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}

static string DefaultSamplesDirectory()
{
    string? projectDirectory = typeof(Program).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "ProjectDirectory")?.Value;

    string source = string.IsNullOrEmpty(projectDirectory) ? "" : Path.Combine(projectDirectory, "nytimes");
    return Directory.Exists(source) ? source : Path.Combine(AppContext.BaseDirectory, "nytimes");
}

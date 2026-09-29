# HelloXUnit

Sandbox for xUnit v3 on .NET 10 with Microsoft.Testing.Platform, central package management, and warnings as errors.

`FilenameString` middle-truncates file names like the macOS Finder, so names that differ only at the end stay distinguishable:

| Width 24 | Result |
|---|---|
| middle | `hello_there_…inal_v2.pdf` |
| end | `hello_there_I_have_a_su…` |

It keeps the extension when it fits and never splits grapheme clusters.

`ArticleProcessor` turns a saved news article page into clean prose and an extractive summary. It tries, in order:

1. `window.__preloadedData` JSON (full NYT body, even behind the paywall spinner)
2. JSON-LD `articleBody`
3. Rendered `<p>` elements

## Projects

- `CSharpClassLibrary`: `FilenameString`, `ArticleExtraction.cs`
- `CSharpConsoleApp`: prints a summary and the full prose for each HTML file under `nytimes/` or the given paths
- `CSharpUnitTests`: tests

## Usage

```bash
dotnet format
dotnet test
dotnet run --project CSharpConsoleApp
dotnet run --project CSharpConsoleApp -- path/to/page.html path/to/dir
bash export.sh
```

`export.sh` writes all exported sources to `docs/llm/dump.txt`. Verify it with:

```bash
sed '$d' docs/llm/dump.txt | sha256sum
```

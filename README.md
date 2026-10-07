# HelloXUnit

Sandbox for xUnit v3 on .NET 10 with Microsoft.Testing.Platform, central package management, warnings as errors, and Stryker.NET mutation testing.

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
- `CSharpConsoleApp`: prints a summary and the full prose for each HTML file under `CSharpConsoleApp/nytimes/` or the given paths, and writes the same report to a `.txt` file beside each HTML file
- `CSharpUnitTests`: tests, plus `stryker-config.json`

Sample HTML under `CSharpConsoleApp/nytimes/` is not tracked in exports but is required by the sample tests.

## Usage

```bash
dotnet format
dotnet test
dotnet run --project CSharpConsoleApp
dotnet run --project CSharpConsoleApp -- path/to/page.html path/to/dir
bash mutate.sh
bash export.sh
```

`dotnet test` runs every test, every time. Nothing is filtered or skipped.

## Mutation testing

`bash mutate.sh` restores the local `dotnet-stryker` tool from `.config/dotnet-tools.json` and runs it from `CSharpUnitTests/`. Extra arguments pass through, e.g. `bash mutate.sh -m "**/FilenameString.cs"`.

Settings in `CSharpUnitTests/stryker-config.json`:

| Setting | Value | Why |
|---|---|---|
| `test-runner` | `mtp` | required for xUnit v3 |
| `mutation-level` | `Complete` | every mutator |
| `coverage-analysis` | `off` | every test runs against every mutant |
| `disable-bail` | `true` | every test runs to completion, exposing tests that kill nothing |
| `disable-mix-mutants` | `true` | one mutant per run |
| `concurrency` | `1` | Stryker 5.0.0 under-reports kills under `mtp` with concurrency above 1 ([#3832](https://github.com/stryker-mutator/stryker-net/issues/3832)) |
| `thresholds.break` | `0` | raise once a baseline score exists |

Only `CSharpClassLibrary` is mutated. Reports land in `CSharpUnitTests/StrykerOutput/<timestamp>/reports/`.

## Export

`export.sh` writes all exported sources to `docs/llm/dump.txt`. Verify it with:

```bash
sed '$d' docs/llm/dump.txt | sha256sum
```

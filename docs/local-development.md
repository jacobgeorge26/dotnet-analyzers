# Local development

## Prerequisites

- **.NET 10 SDK** or newer
- **Rider** (recommended) or Visual Studio

## Getting started

Clone the repository, open `Dotnet.Analyzers.sln`, and run the tests:

```sh
dotnet test Dotnet.Analyzers.sln
```

## Adding a new rule

1. **Add the analyzer.** Create a `DiagnosticAnalyzer` class in `Dotnet.Analyzers/`
   Give it the next free `NET` ID as its `DiagnosticId`.
2. **Add the tests.** Create a matching `<Analyzer>Tests` class in `Dotnet.Analyzers.Tests/`.
   Cover both cases that should trigger and cases that shouldn't.
3. **Track the release.** Add the rule to `Dotnet.Analyzers/AnalyzerReleases.Unshipped.md`;
   the build warns (`RS2000`) if it's missing.
4. **Document it.** Add the rule to the table in the [README](https://github.com/jacobgeorge26/dotnet-analyzers#rules).
5. **Release as a minor version bump**; see [release](release.md#versioning).

## Referencing local build in another repo

### Option A: Direct project reference

Use this for the fastest iteration loop.
In the other repo's `Directory.Build.props`, replace the `Dotnet.Analyzers` package reference
with a project reference to a local clone of this repo:

```xml
<ProjectReference Include="/path/to/dotnet-analyzers/Dotnet.Analyzers/Dotnet.Analyzers.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

`OutputItemType="Analyzer"` loads the DLL as an analyzer, not a library reference.
Rider can take a few seconds after a build to pick up analyzer changes.

### Option B: Local NuGet source

Use this when verifying a change as a built package.

1. In this repo, pack with a unique pre-release version, so NuGet doesn't reuse a cached package from an earlier pack:
   ```sh
   # Unix
   dotnet pack Dotnet.Analyzers/Dotnet.Analyzers.csproj -c Release -o artifacts -p:Version=0.0.0-local.$(date +%s)
   ```
   ```powershell
   # Windows
   dotnet pack Dotnet.Analyzers/Dotnet.Analyzers.csproj -c Release -o artifacts -p:Version=0.0.0-local.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
   ```
2. In the other repo, run:
   ```sh
   dotnet nuget add source /path/to/dotnet-analyzers/artifacts --name local
   ```
   If the source already exists: `dotnet nuget update source local --source /path/to/dotnet-analyzers/artifacts`
3. Set the package reference to the exact version that was packed; pre-releases don't match `1.0.*`.

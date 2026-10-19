# Troubleshooting

## "Dotnet.Analyzers requires the .NET 10 SDK or newer"

The package fails the build on older SDKs, since their compilers can't load the analyzers.
Update to .NET 10 SDK or newer, and check the SDK version in the repo's `global.json`.

## `CS9057`: the analyzer references a newer version of the compiler

The user's compiler is older than the analyzer's `Microsoft.CodeAnalysis.CSharp` reference, so no rules run.
The SDK check above should catch this first; if not, e.g. on a .NET 10 preview SDK, update to the released .NET 10 SDK or newer.

## A new patch isn't picked up locally

NuGet skips restore when nothing has changed, and caches feed results for 30 minutes. Force a fresh resolve:

```sh
dotnet restore --force-evaluate
```

## Rules don't match the source, after a local pack

A stale cached copy of the package may be the cause. Clear it with:

```sh
# Windows
rm -r $env:USERPROFILE/.nuget/packages/dotnet.analyzers/
# Unix
rm -rf ~/.nuget/packages/dotnet.analyzers/
```

## Rider doesn't show a rule, or shows an old message

Rider reloads analyzers after a build, sometimes with a delay.
Build the solution in Rider, wait a few seconds, and reopen the file.

## A fixed issue is still reported until a rebuild

The rules run live in the IDE, so a fix in the flagged file should clear within a few seconds.
If it doesn't, the error is likely left over from the last build:

- **Rider:** the Problems window's *Build* tab keeps errors until the next build.
  Check the *Current File* or *Solution-Wide Analysis* tab instead.
- **Visual Studio:** errors from a build stay in the Error List until the next build, whatever the filter.
  Set the filter to *Build + IntelliSense* to also see live results.

If the error is gone from the editor but not the list, it's stale.
If it's still in the editor, check Roslyn analyzers are enabled in Rider:
Settings | Editor | Inspection Settings | Roslyn Analyzers.
Without them, Rider only reports analyzer diagnostics from builds.

A fix in another file isn't picked up until the flagged file is edited or reopened, since the IDE only re-analyzes changed files.

## A rule conflicts with an IDE suggestion or compiler warning

Some rules conflict with, or duplicate, an IDE suggestion / compiler warning.
See [suggested overrides](installation.md#suggested-overrides) for suggested `.editorconfig` settings to add.

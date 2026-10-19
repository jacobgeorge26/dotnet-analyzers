# Installing the package

## Prerequisites

- .NET 10 SDK or newer

## Installing package

TODO

## Disabling a rule

To disable a rule in one project, add it to `NoWarn` in that project's `.csproj`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);NET0001</NoWarn>
</PropertyGroup>
```

To change a rule's severity for specific files, set it in the repo's `.editorconfig`.
The section header picks the files, relative to the `.editorconfig`; e.g. this disables `NET0001` in tests only:

```ini
[tests/**.cs]
dotnet_diagnostic.NET0001.severity = none
```

Use `[*.cs]` for the whole repo, and `warning` or `suggestion` instead of `none` to keep the rule visible without failing the build.

To try an unreleased version of the analyzers, see [local development](local-development.md#trying-a-change-in-another-repo).

## Suggested overrides

Some IDE suggestions and compiler warnings conflict with or duplicate these rules.
Add these to the repo's `.editorconfig` to resolve in favour of the installed dotnet analyzers.

### NET0001: `Do not use Environment.GetEnvironmentVariable(s)`

Build scripts, such as a Nuke `build/` project, conflict with rule `NET0001: Do not use Environment.GetEnvironmentVariable(s)`.
They read CI environment variables, with no `IOptions<T>` setup to use instead.
Use the following override for build projects:

```ini
[build/**.cs]
dotnet_diagnostic.NET0001.severity = none
```

### NET0002: `Do not use Console.Write/WriteLine`

Build scripts, such as a Nuke `build/` project, conflict with rule `NET0002: Do not use Console.Write/WriteLine`.
They write to the console with no logging setup to use instead.
Use the following override for build projects:

```ini
[build/**.cs]
dotnet_diagnostic.NET0002.severity = none
```

###  NET0004: `BanBaseForwardedParameterUse`

The compiler's `CS9107: Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor` warning duplicates rule `NET0004: BanBaseForwardedParameterUse`.
To avoid the same use being reported twice, use the following override for all projects:

```ini
[*.cs]
dotnet_diagnostic.CS9107.severity = none
```

# dotnet-analyzers

Roslyn analyzers for .NET services
Supports .NET 10 SDK and newer.

- [Installation](docs/installation.md)
- [Local development](docs/local-development.md)
- [Release](docs/release.md)
- [Troubleshooting](docs/troubleshooting.md)

## Rules

| ID        | Severity | Rule                                                                              |
|-----------|----------|-----------------------------------------------------------------------------------|
| `NET0001` | Error    | Do not use `Environment.GetEnvironmentVariable(s)`                                |
| `NET0002` | Error    | Do not use `Console.Write`/`WriteLine`                                            |
| `NET0003` | Error    | Do not use static `Serilog.Log` in instance members                               |
| `NET0004` | Error    | Do not use a primary constructor parameter after passing it to a base constructor |

To disable a rule or change its severity, see [disabling a rule](docs/installation.md#disabling-a-rule).

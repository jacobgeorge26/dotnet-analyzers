using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dotnet.Analyzers.Tests;

public sealed class BanStaticLogInInstanceCodeAnalyzerTests
{
    // Serilog stub types, so test code compiles without referencing the real package
    private const string SerilogStubs = """
        namespace Serilog
        {
            public interface ILogger
            {
                void Information(string message);
                void Warning(string message);
                void Error(string message);
            }

            public static class Log
            {
                public static void Information(string message) { }
                public static void Warning(string message) { }
                public static void Error(string message) { }
                public static void Debug(string message) { }
                public static void Fatal(string message) { }
                public static void Verbose(string message) { }
                public static ILogger ForContext<T>() => null;
                public static void CloseAndFlush() { }
                public static System.Threading.Tasks.ValueTask CloseAndFlushAsync() => default;
            }
        }
        """;

    [Theory]
    [InlineData("""Serilog.Log.Information("hello")""", "Log.Information")]
    [InlineData("""Serilog.Log.Warning("hello")""", "Log.Warning")]
    [InlineData("""Serilog.Log.Error("hello")""", "Log.Error")]
    [InlineData("""Serilog.Log.Debug("hello")""", "Log.Debug")]
    [InlineData("""Serilog.Log.Fatal("hello")""", "Log.Fatal")]
    [InlineData("""Serilog.Log.Verbose("hello")""", "Log.Verbose")]
    public async Task Reports_static_Log_calls_in_instance_method(string call, string expectedName)
    {
        var source = $$"""
            class MyService
            {
                void DoWork()
                {
                    {{call}};
                }
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        AssertDiagnostic(diagnostic, call, expectedName);
    }

    [Fact]
    public async Task Reports_call_through_using_directive()
    {
        const string source = """
            using Serilog;

            class MyService
            {
                void DoWork() => Log.Warning("oops");
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        AssertDiagnostic(diagnostic, """Log.Warning("oops")""", "Log.Warning");
    }

    [Fact]
    public async Task Reports_call_in_constructor()
    {
        const string source = """
            class MyService
            {
                public MyService() => Serilog.Log.Error("bad");
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        AssertDiagnostic(diagnostic, """Serilog.Log.Error("bad")""", "Log.Error");
    }

    [Fact]
    public async Task Reports_method_group_in_instance_method()
    {
        const string source = """
            using System.Collections.Generic;

            class MyService
            {
                void DoWork(List<string> items) => items.ForEach(Serilog.Log.Information);
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        AssertDiagnostic(diagnostic, "Serilog.Log.Information", "Log.Information");
    }

    [Fact]
    public async Task Reports_each_call()
    {
        const string source = """
            class MyService
            {
                void DoWork()
                {
                    Serilog.Log.Information("a");
                    Serilog.Log.Warning("b");
                }
            }
            """;

        Assert.Equal(2, (await AnalyzeAsync(source)).Length);
    }

    [Fact]
    public async Task Ignores_call_in_static_method()
    {
        const string source = """
            class Startup
            {
                static void Configure() => Serilog.Log.Information("boot");
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task Ignores_call_in_static_constructor()
    {
        const string source = """
            class MyService
            {
                static MyService() => Serilog.Log.Information("init");
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task Ignores_call_in_static_property()
    {
        const string source = """
            class MyService
            {
                static string Name
                {
                    get
                    {
                        Serilog.Log.Debug("reading");
                        return "test";
                    }
                }
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task Ignores_ForContext_logger()
    {
        const string source = """
            class MyService
            {
                private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<MyService>();

                void DoWork() => Log.Information("hello");
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    // Lifecycle, not logging; ILogger has no equivalent
    [Fact]
    public async Task Ignores_CloseAndFlush_in_instance_method()
    {
        const string source = """
            class Host : System.IDisposable
            {
                public void Dispose()
                {
                    Serilog.Log.CloseAndFlush();
                    _ = Serilog.Log.CloseAndFlushAsync();
                }
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task Ignores_code_without_Serilog()
    {
        const string source = """
            static class Log
            {
                public static void Information(string message) { }
            }

            class MyService
            {
                void DoWork() => Log.Information("hello");
            }
            """;

        Assert.Empty(await AnalyzeAsync([source]));
    }

    private static void AssertDiagnostic(Diagnostic diagnostic, string expectedSource, string expectedName)
    {
        Assert.Equal(BanStaticLogInInstanceCodeAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{expectedName}'", diagnostic.GetMessage());
        Assert.Equal(expectedSource, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) => AnalyzeAsync([source, SerilogStubs]);

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string[] sources)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var compilation = CSharpCompilation.Create(
            "Test",
            sources.Select(source => CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Fail on broken test source rather than silently analyzing it
        Assert.Empty(compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        return await compilation
            .WithAnalyzers([new BanStaticLogInInstanceCodeAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
    }

    // The test runtime's own assemblies, so snippets compile against the real BCL types
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dotnet.Analyzers.Tests;

public sealed class BanConsoleWriteAnalyzerTests
{
    [Theory]
    [InlineData("""Console.WriteLine("hello")""", "Console.WriteLine")]
    [InlineData("""Console.Write("hello")""", "Console.Write")]
    [InlineData("Console.WriteLine()", "Console.WriteLine")]
    [InlineData("""Console.WriteLine("{0}", 1)""", "Console.WriteLine")]
    [InlineData("""System.Console.WriteLine("hello")""", "Console.WriteLine")]
    public async Task Reports_Console_Write_calls(string call, string expectedName)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(WrapInMethod($"{call};")));

        AssertDiagnostic(diagnostic, call, expectedName);
    }

    [Fact]
    public async Task Reports_method_group()
    {
        const string source = """
            using System;
            using System.Collections.Generic;

            class C
            {
                void M(List<string> items) => items.ForEach(Console.WriteLine);
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        AssertDiagnostic(diagnostic, "Console.WriteLine", "Console.WriteLine");
    }

    [Fact]
    public async Task Reports_call_through_static_using()
    {
        const string source = """
            using static System.Console;

            class C
            {
                void M() => WriteLine("hello");
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        AssertDiagnostic(diagnostic, """WriteLine("hello")""", "Console.WriteLine");
    }

    [Fact]
    public async Task Reports_each_call()
    {
        var diagnostics = await AnalyzeAsync(WrapInMethod("""
            Console.Write("a");
            Console.WriteLine("b");
            """));

        Assert.Equal(2, diagnostics.Length);
    }

    [Theory]
    [InlineData("Console.ReadLine();")]
    [InlineData("""Console.Error.WriteLine("hello");""")]
    [InlineData("_ = nameof(Console.WriteLine);")]
    public async Task Ignores_other_Console_usages(string statement)
    {
        Assert.Empty(await AnalyzeAsync(WrapInMethod(statement)));
    }

    [Fact]
    public async Task Ignores_logger_calls()
    {
        const string source = """
            static class Log
            {
                public static void Information(string message) { }
            }

            class C
            {
                void M() => Log.Information("hello");
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public async Task Ignores_unrelated_type_named_Console()
    {
        const string source = """
            namespace App
            {
                static class Console
                {
                    public static void WriteLine(string message) { }
                }

                class C
                {
                    void M() => Console.WriteLine("hello");
                }
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
    }

    private static void AssertDiagnostic(Diagnostic diagnostic, string expectedSource, string expectedName)
    {
        Assert.Equal(BanConsoleWriteAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{expectedName}'", diagnostic.GetMessage());
        Assert.Equal(expectedSource, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    private static string WrapInMethod(string statements) => $$"""
        using System;

        class C
        {
            void M()
            {
                {{statements}}
            }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var compilation = CSharpCompilation.Create(
            "Test",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Fail on broken test source rather than silently analyzing it
        Assert.Empty(compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        return await compilation
            .WithAnalyzers([new BanConsoleWriteAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
    }

    // The test runtime's own assemblies, so snippets compile against the real System.Console
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];
}

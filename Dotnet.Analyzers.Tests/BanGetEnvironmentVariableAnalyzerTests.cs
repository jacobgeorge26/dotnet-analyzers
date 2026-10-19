using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dotnet.Analyzers.Tests;

public sealed class BanGetEnvironmentVariableAnalyzerTests
{
    [Theory]
    [InlineData("""Environment.GetEnvironmentVariable("FOO")""", "Environment.GetEnvironmentVariable")]
    [InlineData("""Environment.GetEnvironmentVariable("FOO", EnvironmentVariableTarget.Process)""", "Environment.GetEnvironmentVariable")]
    [InlineData("Environment.GetEnvironmentVariables()", "Environment.GetEnvironmentVariables")]
    [InlineData("""System.Environment.GetEnvironmentVariable("FOO")""", "Environment.GetEnvironmentVariable")]
    public async Task Reports_GetEnvironmentVariable_calls(string call, string expectedName)
    {
        var diagnostics = await AnalyzeAsync(WrapInMethod($"_ = {call};"));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(BanGetEnvironmentVariableAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{expectedName}'", diagnostic.GetMessage());
        Assert.Equal(call, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Reports_each_call()
    {
        var diagnostics = await AnalyzeAsync(WrapInMethod(
            """_ = Environment.GetEnvironmentVariable("A") + Environment.GetEnvironmentVariable("B");"""));

        Assert.Equal(2, diagnostics.Length);
    }

    [Fact]
    public async Task Reports_method_group_invocation_through_static_using()
    {
        const string source = """
            using static System.Environment;

            class C
            {
                object M() => GetEnvironmentVariable("FOO");
            }
            """;

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Equal(BanGetEnvironmentVariableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    [Theory]
    [InlineData("""Environment.SetEnvironmentVariable("FOO", "bar");""")]
    [InlineData("_ = Environment.ProcessorCount;")]
    [InlineData("""_ = Environment.ExpandEnvironmentVariables("%FOO%");""")]
    public async Task Ignores_other_Environment_members(string statement)
    {
        Assert.Empty(await AnalyzeAsync(WrapInMethod(statement)));
    }

    [Fact]
    public async Task Ignores_unrelated_type_named_Environment()
    {
        const string source = """
            namespace App
            {
                static class Environment
                {
                    public static string GetEnvironmentVariable(string name) => name;
                }

                class C
                {
                    object M() => Environment.GetEnvironmentVariable("FOO");
                }
            }
            """;

        Assert.Empty(await AnalyzeAsync(source));
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
            .WithAnalyzers([new BanGetEnvironmentVariableAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
    }

    // The test runtime's own assemblies, so snippets compile against the real System.Environment
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];
}

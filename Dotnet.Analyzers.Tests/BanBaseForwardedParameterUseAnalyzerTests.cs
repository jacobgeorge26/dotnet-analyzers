using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dotnet.Analyzers.Tests;

// Expected diagnostics are marked in the source with [|...|]
public sealed class BanBaseForwardedParameterUseAnalyzerTests
{
    [Fact]
    public async Task Reports_forwarded_parameter_used_in_member()
    {
        await VerifyAsync("""
            class Base(string value);
            class C(string value) : Base(value)
            {
                public string Value => [|value|];
            }
            """);
    }

    // CS9107 misses this one: the parameter isn't captured, it's copied into a field
    [Fact]
    public async Task Reports_forwarded_parameter_used_in_field_initializer()
    {
        await VerifyAsync("""
            class Base(string value);
            class C(string value) : Base(value)
            {
                private readonly string _value = [|value|];
            }
            """);
    }

    [Fact]
    public async Task Reports_forwarded_parameter_captured_by_initializer_lambda()
    {
        await VerifyAsync("""
            using System;

            class Base(string value);
            class C(string value) : Base(value)
            {
                private readonly Lazy<int> _length = new(() => [|value|].Length);
            }
            """);
    }

    [Fact]
    public async Task Reports_parameter_nested_in_base_argument()
    {
        await VerifyAsync("""
            class Wrapper(string value);
            class Base(Wrapper wrapper);
            class C(string value) : Base(new Wrapper(value))
            {
                public string Value => [|value|];
            }
            """);
    }

    [Fact]
    public async Task Reports_each_use()
    {
        await VerifyAsync("""
            interface I;
            class Base(string a, int b);
            class C(string a, int b) : Base(a, b), I
            {
                public string A => [|a|];
                public int Twice => [|b|] + [|b|];
            }
            """);
    }

    [Fact]
    public async Task Reports_use_in_other_partial_declaration()
    {
        await VerifyAsync(
            """
            class Base(string value);
            partial class C(string value) : Base(value);
            """,
            """
            partial class C
            {
                public string Value => [|value|];
            }
            """);
    }

    [Fact]
    public async Task Ignores_forwarded_parameter_not_used()
    {
        await VerifyAsync("""
            class Base(string value)
            {
                protected string Value => value;
            }
            class C(string value) : Base(value)
            {
                public int Length => Value.Length;
            }
            """);
    }

    [Fact]
    public async Task Ignores_forwarded_parameter_used_twice_in_base_arguments()
    {
        await VerifyAsync("""
            class Base(string value, int length);
            class C(string value) : Base(value, value.Length);
            """);
    }

    [Fact]
    public async Task Ignores_parameter_not_forwarded()
    {
        await VerifyAsync("""
            class Base(string value);
            class C(string value, int number) : Base(value)
            {
                public int Number => number;
            }
            """);
    }

    [Fact]
    public async Task Ignores_method_parameter_with_same_name()
    {
        await VerifyAsync("""
            class Base(string value);
            class C(string value) : Base(value)
            {
                public string Echo(string value) => value;
            }
            """);
    }

    [Fact]
    public async Task Ignores_lambda_parameter_in_base_argument()
    {
        await VerifyAsync("""
            using System;

            class Base(Func<int, int> func);
            class C() : Base(x => x + 1);
            """);
    }

    [Fact]
    public async Task Ignores_explicit_constructor_passing_to_base()
    {
        await VerifyAsync("""
            class Base
            {
                protected Base(string value) { }
            }
            class C : Base
            {
                private readonly string _value;

                public C(string value) : base(value)
                {
                    _value = value;
                }
            }
            """);
    }

    [Fact]
    public async Task Ignores_primary_constructor_without_base()
    {
        await VerifyAsync("""
            class C(string value)
            {
                public string Value => value;
            }
            """);
    }

    // Forwarding positional parameters to a base record is idiomatic
    [Fact]
    public async Task Ignores_records()
    {
        await VerifyAsync("""
            record Base(string Value);
            record C(string Value, int Number) : Base(Value)
            {
                public int Length => Value.Length;
            }
            """);
    }

    // nameof doesn't read the value
    [Fact]
    public async Task Ignores_nameof_use()
    {
        await VerifyAsync("""
            class Base(string value);
            class C(string value) : Base(value)
            {
                public string Name => nameof(value);
            }
            """);
    }

    [Fact]
    public async Task Ignores_nameof_in_base_argument()
    {
        await VerifyAsync("""
            class Base(string name);
            class C(string value) : Base(nameof(value))
            {
                public string Value => value;
            }
            """);
    }

    private static async Task VerifyAsync(params string[] markups)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var expected = new List<(string Path, TextSpan Span)>();
        var trees = markups.Select((markup, index) =>
        {
            var path = $"Test{index}.cs";
            var (source, spans) = ParseMarkup(markup);
            expected.AddRange(spans.Select(span => (path, span)));
            return CSharpSyntaxTree.ParseText(source, path: path, cancellationToken: cancellationToken);
        }).ToList();

        var compilation = CSharpCompilation.Create(
            "Test",
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Fail on broken test source rather than silently analyzing it
        Assert.Empty(compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers([new BanBaseForwardedParameterUseAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        Assert.Equal(
            expected.OrderBy(e => e.Path).ThenBy(e => e.Span.Start),
            diagnostics.Select(d => (d.Location.SourceTree!.FilePath, d.Location.SourceSpan)).OrderBy(e => e.FilePath).ThenBy(e => e.SourceSpan.Start));

        foreach (var diagnostic in diagnostics)
        {
            var parameterName = diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan);

            Assert.Equal(BanBaseForwardedParameterUseAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains($"'{parameterName}'", diagnostic.GetMessage());
        }
    }

    // Strips [|...|] markers, returning the clean source and the marked spans
    private static (string Source, ImmutableArray<TextSpan> Spans) ParseMarkup(string markup)
    {
        var source = new System.Text.StringBuilder();
        var spans = ImmutableArray.CreateBuilder<TextSpan>();
        var start = -1;

        for (var i = 0; i < markup.Length; i++)
        {
            if (markup.AsSpan(i).StartsWith("[|"))
            {
                start = source.Length;
                i++;
            }
            else if (markup.AsSpan(i).StartsWith("|]"))
            {
                spans.Add(TextSpan.FromBounds(start, source.Length));
                i++;
            }
            else
            {
                source.Append(markup[i]);
            }
        }

        return (source.ToString(), spans.ToImmutable());
    }

    // The test runtime's own assemblies, so snippets compile against the real BCL types
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];
}

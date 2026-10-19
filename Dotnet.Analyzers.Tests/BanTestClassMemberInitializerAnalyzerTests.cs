using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Dotnet.Analyzers.Tests;

// Expected diagnostics are marked in the source with [|...|]
public sealed class BanTestClassMemberInitializerAnalyzerTests
{
    // Test framework stub types, so test code compiles without referencing the real packages
    private const string FrameworkStubs = """
        namespace NUnit.Framework
        {
            public class TestFixtureAttribute : System.Attribute { }
            public class TestAttribute : System.Attribute { }
            public class SetUpAttribute : System.Attribute { }
        }
        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public class TheoryAttribute : FactAttribute { }
        }
        namespace Microsoft.VisualStudio.TestTools.UnitTesting
        {
            public class TestClassAttribute : System.Attribute { }
            public class TestMethodAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public async Task Reports_NUnit_field_initializer()
    {
        const string code = """
            using System.Collections.Generic;
            using NUnit.Framework;
            [TestFixture]
            class C
            {
                private List<int> [|_items|] = new();
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    // readonly stops reassignment, not mutation: items added by one test are still there in the next
    [Fact]
    public async Task Reports_readonly_field_of_mutable_type()
    {
        const string code = """
            using System.Collections.Generic;
            using NUnit.Framework;
            class C
            {
                private readonly List<int> [|_items|] = new();
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_non_readonly_field_of_immutable_type()
    {
        const string code = """
            using NUnit.Framework;
            class C
            {
                private int [|_count|] = 0;
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_xunit_field_initializer()
    {
        const string code = """
            using Xunit;
            class C
            {
                private string [|_name|] = "a";
                [Fact] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_class_with_derived_test_attribute()
    {
        const string code = """
            using Xunit;
            class C
            {
                private int [|_count|] = 1;
                [Theory] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_MSTest_field_initializer()
    {
        const string code = """
            using Microsoft.VisualStudio.TestTools.UnitTesting;
            [TestClass]
            class C
            {
                private object [|_state|] = new();
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_auto_property_initializer()
    {
        const string code = """
            using System.Collections.Generic;
            using NUnit.Framework;
            class C
            {
                public List<int> [|Items|] { get; set; } = new();
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_static_field_initializer()
    {
        const string code = """
            using Xunit;
            class C
            {
                private static int [|_calls|] = 0;
                [Fact] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_class_whose_base_has_setup()
    {
        const string code = """
            using NUnit.Framework;
            abstract class TestBase
            {
                [SetUp] public void SetUp() { }
            }
            class C : TestBase
            {
                private object [|_state|] = new();
            }
            """;

        await VerifyAsync(code);
    }

    // The base has no test markers itself, but its members are shared with the fixtures' tests
    [Fact]
    public async Task Reports_unmarked_base_of_test_classes_once()
    {
        const string code = """
            using System.Collections.Generic;
            using NUnit.Framework;
            abstract class TestBase
            {
                protected List<int> [|Items|] = new();
            }
            class ATests : TestBase
            {
                [Test] public void T() { }
            }
            class BTests : TestBase
            {
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_unmarked_generic_base_of_test_class()
    {
        const string code = """
            using System.Collections.Generic;
            using NUnit.Framework;
            abstract class TestBase<T>
            {
                protected List<T> [|Items|] = new();
            }
            class Tests : TestBase<int>
            {
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Reports_only_initialized_declarator()
    {
        const string code = """
            using NUnit.Framework;
            class C
            {
                private int [|_a|] = 1, _b;
                [Test] public void T() { _b = 2; }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_const()
    {
        const string code = """
            using NUnit.Framework;
            class C
            {
                private const string Name = "a";
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_readonly_members_of_immutable_types()
    {
        const string code = """
            using System;
            using NUnit.Framework;
            enum Colour { Red }
            class C
            {
                private readonly int _count = 1;
                private readonly string _name = "a";
                private static readonly Colour Colour = Colour.Red;
                private readonly TimeSpan _timeout = TimeSpan.FromSeconds(1);
                private readonly int? _limit = null;
                public string Name { get; } = "a";
                public int Count { get; init; } = 1;
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_member_assigned_in_setup()
    {
        const string code = """
            using System.Collections.Generic;
            using NUnit.Framework;
            class C
            {
                private List<int> _items = null!;
                [SetUp] public void SetUp() { _items = new(); }
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_null_or_default_initializer()
    {
        const string code = """
            using NUnit.Framework;
            class C
            {
                private object? _a = null;
                private object _b = default!;
                private object _e = null!;
                private readonly object _f = null!;
                private static object _g = null!;
                public object H { get; set; } = null!;
                private int _c = default;
                private int _d = default(int);
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_member_assigned_in_constructor()
    {
        const string code = """
            using System.Collections.Generic;
            using Xunit;
            class C
            {
                private readonly List<int> _items;
                public C() { _items = new(); }
                [Fact] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_primary_constructor_parameter_capture()
    {
        const string code = """
            using Xunit;
            class Fixture { public object Create() => new(); }
            class C(Fixture fixture)
            {
                private readonly object _client = fixture.Create();
                [Fact] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_non_test_class()
    {
        const string code = """
            using System.Collections.Generic;
            class C
            {
                private List<int> _items = new();
                public void M() { }
            }
            """;

        await VerifyAsync(code);
    }

    [Fact]
    public async Task Ignores_user_defined_test_attribute()
    {
        const string code = """
            class TestAttribute : System.Attribute { }
            class C
            {
                private object _state = new();
                [Test] public void T() { }
            }
            """;

        await VerifyAsync(code);
    }

    private static async Task VerifyAsync(string markup)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (source, expected) = ParseMarkup(markup);

        var compilation = CSharpCompilation.Create(
            "Test",
            [
                CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken),
                CSharpSyntaxTree.ParseText(FrameworkStubs, cancellationToken: cancellationToken),
            ],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Fail on broken test source rather than silently analyzing it
        Assert.Empty(compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var diagnostics = await compilation
            .WithAnalyzers([new BanTestClassMemberInitializerAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        Assert.Equal(expected, diagnostics.Select(d => d.Location.SourceSpan).Order());

        foreach (var diagnostic in diagnostics)
        {
            var memberName = diagnostic.Location.SourceTree!.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan);

            Assert.Equal(BanTestClassMemberInitializerAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains($"'{memberName}'", diagnostic.GetMessage());
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

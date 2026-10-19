using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dotnet.Analyzers;

/// <summary>
/// Flags fields and auto-properties in test classes that are initialized at their declaration.
/// Applies to every test framework: an initialized value can outlive a single test, so one test's changes leak into the next.
/// Values should be assigned in a [SetUp] method or constructor instead, so each test starts from a known state.
/// Constants and read-only members of immutable types are allowed, since no test can change them.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanTestClassMemberInitializerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NET0005";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not initialize test class members at their declaration",
        messageFormat: "'{0}' is initialized at its declaration in a test class",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A value initialized at its declaration can outlive a single test, so a change made by one test leaks into the next. Assign it in a [SetUp] method or constructor instead.");

    // Attributes that mark a method as a test, setup or teardown; subclasses also match (e.g. [Theory], [DataTestMethod])
    private static readonly ImmutableHashSet<string> TestMethodAttributes = ImmutableHashSet.Create(
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "NUnit.Framework.TheoryAttribute",
        "NUnit.Framework.SetUpAttribute",
        "NUnit.Framework.TearDownAttribute",
        "NUnit.Framework.OneTimeSetUpAttribute",
        "NUnit.Framework.OneTimeTearDownAttribute",
        "Xunit.FactAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute",
        "TUnit.Core.TestAttribute",
        "TUnit.Core.BeforeAttribute",
        "TUnit.Core.AfterAttribute");

    private static readonly ImmutableHashSet<string> TestClassAttributes = ImmutableHashSet.Create(
        "NUnit.Framework.TestFixtureAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute");

    // Immutable types beyond primitives, string and enums; a read-only member of one can't be changed by a test
    private static readonly ImmutableHashSet<string> ImmutableTypes = ImmutableHashSet.Create(
        "System.Guid",
        "System.TimeSpan",
        "System.DateTimeOffset",
        "System.DateOnly",
        "System.TimeOnly");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            // A base class shares its members with the tests, even without test markers of its own
            var testBaseClasses = new Lazy<ImmutableHashSet<INamedTypeSymbol>>(
                () => FindTestBaseClasses(compilationContext.Compilation));

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeClass(ctx, testBaseClasses),
                SyntaxKind.ClassDeclaration);
        });
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context, Lazy<ImmutableHashSet<INamedTypeSymbol>> testBaseClasses)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken) is not { } classSymbol
            || !(IsTestClass(classSymbol) || testBaseClasses.Value.Contains(classSymbol)))
            return;

        foreach (var member in classDeclaration.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.ConstKeyword):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        if (variable.Initializer is { } initializer
                            && context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol fieldSymbol
                            && !IsAllowed(context, fieldSymbol.IsReadOnly, fieldSymbol.Type, initializer.Value))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), fieldSymbol.Name));
                        }
                    }
                    break;

                case PropertyDeclarationSyntax { Initializer: { } initializer } property
                    when context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is { } propertySymbol:
                    // Get-only and init-only auto-properties can't be reassigned after construction
                    var isReadOnly = propertySymbol.SetMethod is null or { IsInitOnly: true };
                    if (!IsAllowed(context, isReadOnly, propertySymbol.Type, initializer.Value))
                        context.ReportDiagnostic(Diagnostic.Create(Rule, property.Identifier.GetLocation(), propertySymbol.Name));
                    break;
            }
        }
    }

    private static bool IsAllowed(SyntaxNodeAnalysisContext context, bool isReadOnly, ITypeSymbol type, ExpressionSyntax value)
    {
        if (isReadOnly && IsImmutable(type))
            return true;

        // `= null!` placates nullable analysis for a member assigned in setup; it holds no state
        if (value is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed)
            value = suppressed.Operand;

        if (value.IsKind(SyntaxKind.NullLiteralExpression)
            || value.IsKind(SyntaxKind.DefaultLiteralExpression)
            || value.IsKind(SyntaxKind.DefaultExpression))
            return true;

        // Capturing a primary constructor parameter is constructor assignment, which is allowed
        return value.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol
                is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } });
    }

    private static bool IsImmutable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type.TypeKind == TypeKind.Enum
               || type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String
               || type.SpecialType == SpecialType.System_DateTime
               || ImmutableTypes.Contains(type.ToDisplayString());
    }

    // A class is a test class if it, or a base class, has a test class attribute or a test/setup method
    private static bool IsTestClass(INamedTypeSymbol classSymbol)
    {
        for (var type = classSymbol; type is not null; type = type.BaseType)
        {
            if (HasAttribute(type, TestClassAttributes))
                return true;

            if (type.GetMembers().OfType<IMethodSymbol>().Any(method => HasAttribute(method, TestMethodAttributes)))
                return true;
        }

        return false;
    }

    private static ImmutableHashSet<INamedTypeSymbol> FindTestBaseClasses(Compilation compilation)
    {
        var bases = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var type in GetTypes(compilation.Assembly.GlobalNamespace))
        {
            if (type.TypeKind != TypeKind.Class || !IsTestClass(type))
                continue;

            // Only source bases; generic ones are declared as their original definition
            for (var baseType = type.BaseType;
                 baseType is not null && SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, compilation.Assembly);
                 baseType = baseType.BaseType)
            {
                bases.Add(baseType.OriginalDefinition);
            }
        }

        return bases.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> GetTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol nested)
            {
                if (nested is INamedTypeSymbol type)
                    yield return type;

                foreach (var inner in GetTypes(nested))
                    yield return inner;
            }
        }
    }

    private static bool HasAttribute(ISymbol symbol, ImmutableHashSet<string> attributeNames) =>
        symbol.GetAttributes().Any(attribute => IsOrDerivesFrom(attribute.AttributeClass, attributeNames));

    private static bool IsOrDerivesFrom(INamedTypeSymbol? type, ImmutableHashSet<string> names)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (names.Contains(type.ToDisplayString()))
                return true;
        }

        return false;
    }
}

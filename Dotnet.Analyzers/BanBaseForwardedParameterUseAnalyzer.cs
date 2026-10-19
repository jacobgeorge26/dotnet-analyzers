using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dotnet.Analyzers;

/// <summary>
/// Flags uses of a primary constructor parameter that is also passed to the base constructor.
/// The derived class should use the base member instead.
/// Covers more than CS9107, which skips field initializers and parameters nested in a base argument.
/// Records are skipped; forwarding positional parameters to a base record is idiomatic.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanBaseForwardedParameterUseAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NET0004";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not use a primary constructor parameter after passing it to a base constructor",
        messageFormat: "Primary constructor parameter '{0}' is passed to the base constructor; use the base class member instead",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A primary constructor parameter passed to the base class stays in scope for the whole derived class. Using it there keeps a second copy alongside the base class member, so only the base class member should be used.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolStartAction(StartClass, SymbolKind.NamedType);
    }

    // Partial declarations share the parameters, so collect from all of them and report once at the end
    private static void StartClass(SymbolStartAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        var parameterNames = type.InstanceConstructors
            .Where(constructor => constructor.DeclaringSyntaxReferences.Any(reference =>
                GetBaseArguments(reference.GetSyntax(context.CancellationToken)) is not null))
            .SelectMany(constructor => constructor.Parameters)
            .Select(parameter => parameter.Name)
            .ToImmutableHashSet();

        if (parameterNames.IsEmpty)
            return;

        var forwarded = new ConcurrentDictionary<IParameterSymbol, byte>(SymbolEqualityComparer.Default);
        var uses = new ConcurrentBag<(IdentifierNameSyntax Identifier, IParameterSymbol Parameter)>();

        context.RegisterSyntaxNodeAction(
            nodeContext => CollectDeclaration(nodeContext, type, parameterNames, forwarded, uses),
            SyntaxKind.ClassDeclaration);

        context.RegisterSymbolEndAction(endContext =>
        {
            foreach (var (identifier, parameter) in uses)
            {
                if (forwarded.ContainsKey(parameter))
                    endContext.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), parameter.Name));
            }
        });
    }

    private static void CollectDeclaration(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol type,
        ImmutableHashSet<string> parameterNames,
        ConcurrentDictionary<IParameterSymbol, byte> forwarded,
        ConcurrentBag<(IdentifierNameSyntax Identifier, IParameterSymbol Parameter)> uses)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        // Nested classes are analysed as their own symbol
        if (!SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetDeclaredSymbol(classDeclaration, context.CancellationToken), type))
            return;

        var baseArguments = GetBaseArguments(classDeclaration);
        if (baseArguments is not null)
        {
            foreach (var identifier in baseArguments.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (GetPrimaryConstructorParameter(context, type, parameterNames, identifier) is { } parameter)
                    forwarded.TryAdd(parameter, 0);
            }
        }

        // Everything outside the base argument list counts as a use: members, initializers, lambdas
        var identifiers = classDeclaration
            .DescendantNodes(node => node != baseArguments)
            .OfType<IdentifierNameSyntax>();

        foreach (var identifier in identifiers)
        {
            if (GetPrimaryConstructorParameter(context, type, parameterNames, identifier) is { } parameter)
                uses.Add((identifier, parameter));
        }
    }

    // The base type with an argument list is always first in the base list
    private static ArgumentListSyntax? GetBaseArguments(SyntaxNode node) =>
        node is ClassDeclarationSyntax { ParameterList: not null } classDeclaration
        && classDeclaration.BaseList?.Types.FirstOrDefault() is PrimaryConstructorBaseTypeSyntax baseType
            ? baseType.ArgumentList
            : null;

    private static IParameterSymbol? GetPrimaryConstructorParameter(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol type,
        ImmutableHashSet<string> parameterNames,
        IdentifierNameSyntax identifier)
    {
        // Cheap name check first, so most identifiers skip the semantic lookup
        if (!parameterNames.Contains(identifier.Identifier.ValueText) || IsInNameOf(context, identifier))
            return null;

        if (context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not IParameterSymbol parameter)
            return null;

        // Lambda and method parameters aren't primary constructor parameters
        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
            || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, type)
            || !constructor.DeclaringSyntaxReferences.Any(r => r.GetSyntax(context.CancellationToken) is ClassDeclarationSyntax))
            return null;

        return parameter;
    }

    // nameof doesn't read the value, so it's neither forwarding nor a use
    private static bool IsInNameOf(SyntaxNodeAnalysisContext context, IdentifierNameSyntax identifier) =>
        identifier.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            && context.SemanticModel.GetOperation(invocation, context.CancellationToken) is INameOfOperation);
}

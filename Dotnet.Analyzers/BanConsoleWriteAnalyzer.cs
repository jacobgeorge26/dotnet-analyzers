using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dotnet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanConsoleWriteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NET0002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not use Console.Write/WriteLine",
        messageFormat: "'{0}' uses System.Console; use a structured logger instead",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "System.Console.Write/WriteLine output is unstructured and bypasses log levels and sinks. Use a structured logger instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var consoleType = compilationContext.Compilation.GetTypeByMetadataName("System.Console");
            if (consoleType is null)
                return;

            compilationContext.RegisterOperationAction(
                ctx => AnalyzeOperation(ctx, consoleType),
                OperationKind.Invocation,
                OperationKind.MethodReference);
        });
    }

    private static void AnalyzeOperation(OperationAnalysisContext context, INamedTypeSymbol consoleType)
    {
        // Method groups count too, not just calls
        var method = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null,
        };

        if (method is null || !SymbolEqualityComparer.Default.Equals(method.ContainingType, consoleType))
            return;

        if (method.Name is not ("Write" or "WriteLine"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            context.Operation.Syntax.GetLocation(),
            $"Console.{method.Name}"));
    }
}

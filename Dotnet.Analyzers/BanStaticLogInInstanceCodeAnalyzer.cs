using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dotnet.Analyzers;

/// <summary>
/// Flags calls to the static <c>Serilog.Log</c> facade (e.g. <c>Log.Information(...)</c>) inside instance members.
/// Use <c>Log.ForContext&lt;T&gt;()</c> stored in a field/property instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanStaticLogInInstanceCodeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NET0003";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not use static Serilog.Log in instance members",
        messageFormat: "'{0}' calls static Serilog.Log from an instance member; use a 'Log.ForContext<T>()' instance instead",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Static Serilog.Log calls lose type context. Create an instance of Log.ForContext<T>() instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var serilogLogType = compilationContext.Compilation.GetTypeByMetadataName("Serilog.Log");
            if (serilogLogType is null)
                return;

            compilationContext.RegisterOperationAction(
                ctx => AnalyzeOperation(ctx, serilogLogType),
                OperationKind.Invocation,
                OperationKind.MethodReference);
        });
    }

    private static void AnalyzeOperation(OperationAnalysisContext context, INamedTypeSymbol serilogLogType)
    {
        // Method groups count too, not just calls
        var method = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null,
        };

        if (method is null || !SymbolEqualityComparer.Default.Equals(method.ContainingType, serilogLogType))
            return;

        // ForContext creates the instance logger; CloseAndFlush manages the global one and has no ILogger equivalent
        if (method.Name is "ForContext" or "CloseAndFlush" or "CloseAndFlushAsync")
            return;

        // Only flag if we're inside an instance member
        var containingSymbol = context.ContainingSymbol;
        switch (containingSymbol)
        {
            case IMethodSymbol { IsStatic: true }:
            case IPropertySymbol { IsStatic: true }:
            case IFieldSymbol { IsStatic: true }:
                return;
            default:
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    context.Operation.Syntax.GetLocation(),
                    $"Log.{method.Name}"));
                break;
        }
    }
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Dotnet.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BanGetEnvironmentVariableAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NET0001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not use Environment.GetEnvironmentVariable(s)",
        messageFormat: "'{0}' should not be used at runtime; use IOptions<T> / IConfiguration instead",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Environment.GetEnvironmentVariable and GetEnvironmentVariables bypass the configuration system. Bind settings to an IOptions<T> section instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var envType = compilationContext.Compilation.GetTypeByMetadataName("System.Environment");
            if (envType is null)
                return;

            compilationContext.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, envType),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol envType)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, envType))
            return;

        if (method.Name is not ("GetEnvironmentVariable" or "GetEnvironmentVariables"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            $"Environment.{method.Name}"));
    }
}

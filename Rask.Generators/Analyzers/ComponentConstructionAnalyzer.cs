using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ComponentConstructionAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskCoreAssembly = "Rask.Core";

    private static readonly DiagnosticDescriptor Rask014 = new(
        "RASK014",
        "Components must be created via factory methods",
        "Do not instantiate '{0}' with 'new'; use the generated {1}Components.{0}(...) factory instead",
        "Usage",
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask014);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            var component = start.Compilation.GetTypeByMetadataName(ComponentFullName);
            if (component is null)
            {
                return;
            }

            start.RegisterOperationAction(ctx => Analyze(ctx, component), OperationKind.ObjectCreation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol componentType)
    {
        var op = (IObjectCreationOperation)context.Operation;
        if (op.Type is not INamedTypeSymbol type)
        {
            return;
        }

        if (!InheritsFrom(type, componentType))
        {
            return;
        }

        var location = GetNewKeywordLocation(op.Syntax) ?? op.Syntax.GetLocation();
        var namespacePrefix = GetNamespacePrefix(type);
        context.ReportDiagnostic(Diagnostic.Create(Rask014, location, type.Name, namespacePrefix));
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol target)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, target))
            {
                return true;
            }
        }

        return false;
    }

    private static Location? GetNewKeywordLocation(SyntaxNode syntax) => syntax switch
    {
        ObjectCreationExpressionSyntax oc => oc.NewKeyword.GetLocation(),
        ImplicitObjectCreationExpressionSyntax ioc => ioc.NewKeyword.GetLocation(),
        _ => null
    };

    private static string GetNamespacePrefix(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
        {
            return string.Empty;
        }

        return ns.ToDisplayString() + ".";
    }
}

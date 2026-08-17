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

    // Rask.Html is the other half of the framework's own markup: the HTML/SVG element family, split out
    // of Rask.Core.Components. The rule is "construct components through the generated surface, not
    // 'new'", and it exempts the framework because the framework is what BUILDS that surface — a tag
    // component assembling its own children predates its own factory. Splitting the family into a second
    // assembly did not change which code that is, so the exemption follows it.
    private const string RaskHtmlAssembly = "Rask.Html";

    private static readonly DiagnosticDescriptor Rask014 = new(
        "RASK014",
        "Components must be built through a chain",
        "Do not instantiate '{0}' with 'new'; name it and chain onto it instead — '{0}.Prop(value)', or '{0}' alone when it needs nothing",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A chain is how a component gets its identity, its children and its injected services: the "
                     + "first step routes through GetOrCreate, which is what lets the runtime reconcile the same "
                     + "instance across renders. 'new' skips all of it and produces an instance the runtime cannot "
                     + "match to anything, so it re-mounts every frame and never hits the render cache. It also skips "
                     + "what the chain enforces — a component whose required properties are steps cannot be "
                     + "incomplete, and 'new' can make one that is. In a test file that deliberately constructs "
                     + "components, opt out per file with '#pragma warning disable RASK014'.",
        helpLinkUri: DiagnosticHelp.Link("RASK014"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask014);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal)
                || string.Equals(start.Compilation.AssemblyName, RaskHtmlAssembly, StringComparison.Ordinal))
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

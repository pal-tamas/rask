using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK104 — a static field or static auto-property holds a component its initializer built from a chain
///     entry (<c>private static readonly Component Empty = Div;</c>).
///     <para>
///         An entry built during a render is a positional slot of the component being rendered, handed back
///         on that component's next render. A static initializer runs once, during whichever render first
///         touches the type, so the static ends up holding ONE page's slot and sharing it with every later
///         page — and the next entry of that type to land on its position redraws it, attributes and all
///         (#1201). Which page that is depends on request order, so the bug shows in one process and not the
///         next.
///     </para>
/// </summary>
/// <remarks>
///     A static METHOD or an expression-bodied property (<c>static Component Empty =&gt; Div;</c>) builds a
///     fresh entry on every call, in the render that calls it, and is exactly what the fix turns this into —
///     so neither is reported. Neither is an entry inside a lambda the initializer stores, which runs later.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticChainEntryAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask104 = new(
        "RASK104",
        "A static member keeps a component built from a chain entry",
        "'{0}' is static and keeps '{1}', built once by whichever render touched it first — that render's slot, then shared by every page. Build it per call instead: 'static {2} {0} => …'.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "An entry built during a render is a positional slot of the component being rendered. A "
                     + "static initializer runs once, during the first render that touches the type, so the "
                     + "static keeps that one page's slot and shares it with every later page; the next entry "
                     + "built at that position redraws it with its own attributes. Turn the field into an "
                     + "expression-bodied property (or a static method) so each render builds its own.",
        helpLinkUri: DiagnosticHelp.Link("RASK104"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask104);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(ComponentFullName) is not { } component)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(ctx => AnalyzeField(ctx, component), SyntaxKind.FieldDeclaration);
            start.RegisterSyntaxNodeAction(ctx => AnalyzeProperty(ctx, component), SyntaxKind.PropertyDeclaration);
        });
    }

    private static void AnalyzeField(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        var field = (FieldDeclarationSyntax)context.Node;
        if (!field.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return;
        }

        foreach (var declarator in field.Declaration.Variables)
        {
            if (declarator.Initializer?.Value is { } value
                && context.SemanticModel.GetDeclaredSymbol(declarator, context.CancellationToken) is IFieldSymbol symbol)
            {
                Check(context, component, symbol.Name, symbol.Type, value);
            }
        }
    }

    // Only an auto-property's `= …` initializer runs once; an expression body (`=> …`) runs per call.
    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context, INamedTypeSymbol component)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (!property.Modifiers.Any(SyntaxKind.StaticKeyword)
            || property.Initializer?.Value is not { } value
            || context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is not { } symbol)
        {
            return;
        }

        Check(context, component, symbol.Name, symbol.Type, value);
    }

    private static void Check(
        SyntaxNodeAnalysisContext context, INamedTypeSymbol component, string member, ITypeSymbol type,
        ExpressionSyntax value)
    {
        if (!HoldsComponent(type, component) || FirstEntry(context, value) is not { } entry)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rask104, value.GetLocation(), member, entry.Identifier.ValueText,
            type.ToMinimalDisplayString(context.SemanticModel, value.SpanStart)));
    }

    // The first chain the initializer builds when it RUNS — so not one inside a lambda it merely stores.
    private static SimpleNameSyntax? FirstEntry(SyntaxNodeAnalysisContext context, ExpressionSyntax value)
    {
        foreach (var node in value.DescendantNodesAndSelf(n => n is not AnonymousFunctionExpressionSyntax)
                     .OfType<ExpressionSyntax>())
        {
            if (BuilderEntry.TryReadChain(node, context.SemanticModel, context.CancellationToken, out var entry, out _, out _))
            {
                return entry;
            }
        }

        return null;
    }

    // A component, or a collection of them (`Component[]`, `IReadOnlyList<Div>`): each element is a slot too.
    private static bool HoldsComponent(ITypeSymbol type, INamedTypeSymbol component) => type switch
    {
        IArrayTypeSymbol array => HoldsComponent(array.ElementType, component),
        INamedTypeSymbol named when BuilderEntry.DerivesFromComponent(named, component) => true,
        INamedTypeSymbol { IsGenericType: true } generic => generic.TypeArguments.Any(t => HoldsComponent(t, component)),
        _ => false,
    };
}

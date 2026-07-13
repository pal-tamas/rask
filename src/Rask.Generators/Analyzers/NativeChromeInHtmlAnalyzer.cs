using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK032 — flags a native chrome component (a <c>Rask.Native.Components.NativeComponent</c> subclass such
///     as <c>NativeHeaderBar</c> / <c>NativeTabBar</c>) nested inside the HTML tree.
///     <para>
///         Native components describe real platform bars, not HTML. They are composed at a native page's layout
///         level — as siblings of <c>NativeWebView</c> in <c>Render()</c> — never inside an HTML element
///         (<c>Div()[NativeHeaderBar()]</c>) or inside <c>NativeWebView</c>'s HTML content. Placed in HTML they
///         serialize to nothing; this catches the mistake at compile time by flagging a native component passed
///         to any element-children indexer.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NativeChromeInHtmlAnalyzer : DiagnosticAnalyzer
{
    private const string NativeComponentFullName = "Rask.Native.Components.NativeComponent";

    private static readonly DiagnosticDescriptor Rask032 = new(
        "RASK032",
        "Native components cannot appear in the HTML tree",
        "'{0}' is a native chrome component and cannot appear inside the HTML tree; compose it at the native layout level (a sibling of NativeWebView), not as an element child",
        "Usage",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK032"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask032);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // Rask.Core defines the spine and Rask.Native the concrete family — both legitimately reference these
            // types outside an HTML context. Mirrors the RASK014 / RASK019 assembly skip.
            var asm = start.Compilation.AssemblyName;
            if (string.Equals(asm, "Rask.Core", StringComparison.Ordinal)
                || string.Equals(asm, "Rask.Native", StringComparison.Ordinal))
            {
                return;
            }

            var nativeComponent = start.Compilation.GetTypeByMetadataName(NativeComponentFullName);
            if (nativeComponent is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeElementChildren(ctx, nativeComponent),
                SyntaxKind.ElementAccessExpression);
        });
    }

    // A native component passed to an element-children indexer — `Element()[ ... native ... ]` (or
    // `NativeWebView()[ ... native ... ]`). Composing bars at the layout level (a collection `[bar, webview]`)
    // is NOT an element-access, so it is correctly untouched; only nesting a bar inside HTML content is flagged.
    private static void AnalyzeElementChildren(SyntaxNodeAnalysisContext context, INamedTypeSymbol nativeComponent)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;
        foreach (var arg in node.ArgumentList.Arguments)
        {
            var type = ModelExtensions.GetTypeInfo(context.SemanticModel, arg.Expression, context.CancellationToken).Type;
            if (type is not null && DerivesFrom(type, nativeComponent))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask032, arg.Expression.GetLocation(), type.Name));
            }
        }
    }

    private static bool DerivesFrom(ITypeSymbol type, INamedTypeSymbol target)
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
}

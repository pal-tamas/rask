using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Guards the boundary between the HTML tree and the native view tree, in both directions.
///     <para>
///         <b>RASK032</b> — a native component (a <c>Rask.Native.Components.NativeComponent</c> subclass such
///         as <c>NativeHeaderBar</c> / <c>NativeTabBar</c>) nested inside the HTML tree. Native components
///         describe real platform views, not HTML. Bars are composed at a native page's layout level — as
///         siblings of <c>NativeWebView</c> in <c>Render()</c> — never inside an HTML element
///         (<c>Div[NativeHeaderBar]</c>) or inside <c>NativeWebView</c>'s HTML content. Placed in HTML they
///         serialize to nothing.
///     </para>
///     <para>
///         <b>RASK048</b> — the mirror image: an HTML element nested inside a <c>NativeScreen</c> subtree. A
///         pure-native screen paints platform views with no WebView anywhere, so there is nothing that could
///         render a <c>Div</c> there and it silently disappears.
///     </para>
/// </summary>
/// <remarks>
///     Both rules read the children indexer and classify by the RECEIVER, which is what lets one analyzer
///     serve both directions — and what keeps RASK032 off the legitimate case. Asking only "is this child
///     native?" was right while <c>NativeWebView</c> was the sole container; a <c>NativeScreen</c>'s children
///     are legitimately native, so that question alone would reject every pure-native screen.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NativeChromeInHtmlAnalyzer : DiagnosticAnalyzer
{
    private const string NativeComponentFullName = "Rask.Native.Components.NativeComponent";
    private const string NativeViewComponentFullName = "Rask.Native.Components.NativeViewComponent";
    private const string ElementFullName = "Rask.Core.Element";

    private static readonly DiagnosticDescriptor Rask032 = new(
        "RASK032",
        "Native components cannot appear in the HTML tree",
        "'{0}' is a native chrome component and cannot appear inside the HTML tree; compose it at the native layout level (a sibling of NativeWebView), or inside a NativeScreen — not as an element child",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Native chrome components describe real platform bars for the Rask.Native host, not HTML. They "
                     + "are composed at the native page's layout level, as siblings of a NativeWebView — nested inside "
                     + "the HTML tree one would serialize to nothing, so it is caught at build time rather than as an "
                     + "invisible bar on a device.",
        helpLinkUri: DiagnosticHelp.Link("RASK032"));

    private static readonly DiagnosticDescriptor Rask048 = new(
        "RASK048",
        "HTML cannot appear inside a native screen",
        "'{0}' is an HTML element and cannot appear inside a NativeScreen; a pure-native screen renders platform views with no WebView to host markup — use the native components (NativeLabel, NativeStack, …), or put the HTML in a NativeWebView instead",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A NativeScreen paints real platform views and has no WebView behind it, so an HTML element "
                     + "composed inside one has nothing to render it and disappears. Use the native view family "
                     + "for native screens, and keep markup inside a NativeWebView — an app may compose both, on "
                     + "different routes.",
        helpLinkUri: DiagnosticHelp.Link("RASK048"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask032, Rask048);

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

            var nativeView = start.Compilation.GetTypeByMetadataName(NativeViewComponentFullName);
            var element = start.Compilation.GetTypeByMetadataName(ElementFullName);

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeElementChildren(ctx, nativeComponent, nativeView, element),
                SyntaxKind.ElementAccessExpression);
        });
    }

    // The children indexer — `Element[ … ]`, `NativeWebView[ … ]`, `NativeScreen[ … ]`. Composing bars at the
    // layout level (a collection `[bar, webview]`) is NOT an element-access, so it is correctly untouched.
    private static void AnalyzeElementChildren(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol nativeComponent,
        INamedTypeSymbol? nativeView,
        INamedTypeSymbol? element)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;

        // A native view container's children must be native; anything else's must not be. NativeWebView is a
        // NativeComponent but NOT a NativeViewComponent, so its children fall under the HTML rule — which is
        // exactly right: it hosts markup.
        var receiverIsNativeView =
            nativeView is not null && DerivesFrom(Resolve(context, node.Expression), nativeView);

        foreach (var arg in node.ArgumentList.Arguments)
        {
            var type = Resolve(context, arg.Expression);
            if (type is null)
            {
                continue;
            }

            if (receiverIsNativeView)
            {
                if (element is not null && DerivesFrom(type, element))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rask048, arg.Expression.GetLocation(), type.Name));
                }

                continue;
            }

            if (DerivesFrom(type, nativeComponent))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask032, arg.Expression.GetLocation(), type.Name));
            }
        }
    }

    // A chain hands back Build<T>, not T, so the type has to be unwrapped first — without this every native
    // component written as a chain (which is all of them now) walked straight past the tests above.
    private static ITypeSymbol? Resolve(SyntaxNodeAnalysisContext context, ExpressionSyntax expression) =>
        BuilderEntry.ChainedComponent(
            ModelExtensions.GetTypeInfo(context.SemanticModel, expression, context.CancellationToken).Type);

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol target)
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

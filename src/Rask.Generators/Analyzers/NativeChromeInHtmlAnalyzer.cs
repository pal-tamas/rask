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
///         <b>RASK032</b> — a native component (a <c>Rask.Native.Components.NativeComponent</c> subclass such as
///         <c>NativeHeaderBar</c> or <c>NativeLabel</c>) nested inside HTML. Native components describe real
///         platform views, not markup; inside an element they serialize to nothing, so a bar written there is
///         simply invisible on the device.
///     </para>
///     <para>
///         <b>RASK046</b> — the mirror image: an HTML element nested inside a <c>NativeScreen</c> subtree. A
///         pure-native screen paints platform views with no WebView anywhere, so there is nothing that could
///         render a <c>Div</c> there and it silently disappears.
///     </para>
/// </summary>
/// <remarks>
///     Both rules read the children indexer — <c>Parent[child, child]</c> — and classify by the RECEIVER, which
///     is what lets one analyzer serve both directions: children of an HTML element (or of a
///     <c>NativeWebView</c>) must not be native, and children of a native view container must not be HTML.
///     Composing bars as siblings of the content root is a collection, not an indexer, so it is untouched.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NativeChromeInHtmlAnalyzer : DiagnosticAnalyzer
{
    private const string NativeComponentFullName = "Rask.Native.Components.NativeComponent";
    private const string NativeViewComponentFullName = "Rask.Native.Components.NativeViewComponent";
    private const string ElementFullName = "Rask.Core.Element";
    private const string BuildFullName = "Rask.Core.Build`1";

    private static readonly DiagnosticDescriptor Rask032 = new(
        "RASK032",
        "Native components cannot appear in the HTML tree",
        "'{0}' is a native component and cannot appear inside the HTML tree; compose it at the native layout level (a sibling of NativeWebView), or inside a NativeScreen — not as an element child",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Native components describe real platform views for the Rask.Native host, not HTML. Bars are "
                     + "composed at the native page's layout level, as siblings of a NativeWebView, and native "
                     + "views inside a NativeScreen — nested in the HTML tree one would serialize to nothing, so "
                     + "it is caught at build time rather than as an invisible view on a device.",
        helpLinkUri: DiagnosticHelp.Link("RASK032"));

    private static readonly DiagnosticDescriptor Rask046 = new(
        "RASK046",
        "HTML cannot appear inside a native screen",
        "'{0}' is an HTML element and cannot appear inside a NativeScreen; a pure-native screen renders platform views with no WebView to host markup — use the native components (NativeLabel, NativeStack, …), or put the HTML in a NativeWebView instead",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A NativeScreen paints real platform views and has no WebView behind it, so an HTML element "
                     + "composed inside one has nothing to render it and disappears. Use the native view family "
                     + "for native screens, and keep markup inside a NativeWebView — an app may compose both, on "
                     + "different routes.",
        helpLinkUri: DiagnosticHelp.Link("RASK046"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask032, Rask046);

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
            var build = start.Compilation.GetTypeByMetadataName(BuildFullName);

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeChildren(ctx, nativeComponent, nativeView, element, build),
                SyntaxKind.ElementAccessExpression);
        });
    }

    private static void AnalyzeChildren(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol nativeComponent,
        INamedTypeSymbol? nativeView,
        INamedTypeSymbol? element,
        INamedTypeSymbol? build)
    {
        var node = (ElementAccessExpressionSyntax)context.Node;
        var receiver = Resolve(context, node.Expression, build);

        // A native view container's children must be native; anything else's must not be. NativeWebView is a
        // NativeComponent but NOT a NativeViewComponent, so its children fall under the HTML rule — which is
        // exactly right: it hosts markup.
        var receiverIsNativeView = nativeView is not null && DerivesFrom(receiver, nativeView);

        foreach (var arg in node.ArgumentList.Arguments)
        {
            var type = Resolve(context, arg.Expression, build);
            if (type is null)
            {
                continue;
            }

            if (receiverIsNativeView)
            {
                if (element is not null && DerivesFrom(type, element))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rask046, arg.Expression.GetLocation(), type.Name));
                }

                continue;
            }

            if (DerivesFrom(type, nativeComponent))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask032, arg.Expression.GetLocation(), type.Name));
            }
        }
    }

    /// <summary>
    ///     The component type an expression contributes, seeing through the builder chain: the chain syntax
    ///     yields <c>Build&lt;T&gt;</c> rather than <c>T</c>, and without unwrapping it these rules would only
    ///     ever fire on the older factory-call syntax — green on exactly the code most people write.
    /// </summary>
    private static ITypeSymbol? Resolve(
        SyntaxNodeAnalysisContext context, ExpressionSyntax expression, INamedTypeSymbol? build)
    {
        var type = ModelExtensions.GetTypeInfo(context.SemanticModel, expression, context.CancellationToken).Type;
        if (build is not null
            && type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, build))
        {
            return named.TypeArguments[0];
        }

        return type;
    }

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

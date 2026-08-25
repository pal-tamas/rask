using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Guards <c>NativeWebView</c>'s two modes. It either hosts markup — its children are the page — or it
///     takes a <c>Url</c> and shows an app you host. A WebView holds one document, so the two cannot both be
///     true of it.
///     <para>
///         <b>RASK049</b> — one <c>NativeWebView</c> does both. The children would be built and then thrown
///         away, which is the failure mode the chain's mode system exists to prevent elsewhere: accepted at
///         the call site, silently dropped at render.
///     </para>
///     <para>
///         <b>RASK050</b> — one component uses both modes, typically by switching on a condition. In URL mode
///         the WebView shows a document this session did not render and holds no HTML diff baseline for, so
///         the markup arm has nothing to paint against.
///     </para>
/// </summary>
/// <remarks>
///     RASK050 is scoped to the declaring <b>type</b>, deliberately, and not to the compilation. A
///     compilation is not an app: a test project, a component library, or any assembly with more than one app
///     root legitimately contains both modes in different types, and a compilation-wide rule reports every
///     one of them — this repo's own native test assembly was the proof. The mistake worth catching is one
///     component that could render either, and a per-type scope sees exactly that.
///     <para>
///         It reports on the <em>markup</em> usage: the <c>Url</c> is the deliberate choice, and the markup is
///         the half that has quietly stopped working.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NativeWebViewModeAnalyzer : DiagnosticAnalyzer
{
    private const string NativeWebViewFullName = "Rask.Native.Components.NativeWebView";
    private const string UrlStep = "Url";

    private static readonly DiagnosticDescriptor Rask049 = new(
        "RASK049",
        "NativeWebView sets a Url and takes children",
        "This NativeWebView sets a Url and also takes children. It shows one document: either the page at "
        + "the Url, or these children. Drop one — put the children on a NativeWebView with no Url, or remove "
        + "them and let the hosted app render the page.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A NativeWebView carrying a Url loads that address, so children composed into it would "
                     + "be built and then discarded — accepted where you wrote them, gone by the time the "
                     + "frame is pushed.",
        helpLinkUri: DiagnosticHelp.Link("RASK049"));

    private static readonly DiagnosticDescriptor Rask050 = new(
        "RASK050",
        "One component uses both of NativeWebView's modes",
        "This NativeWebView hosts markup, but '{0}' also composes one with a Url. A component renders one "
        + "kind of page — pick a mode, or split the two into components of their own.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "In URL mode the WebView shows a document the session did not render and keeps no HTML "
                     + "diff baseline for, so a markup arm in the same component has nothing to paint "
                     + "against. Native screens are unaffected and may be composed alongside either mode.",
        helpLinkUri: DiagnosticHelp.Link("RASK050"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask049, Rask050);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // Rask.Native declares the component itself; its own sources reference both shapes. Mirrors the
            // RASK032 / RASK048 assembly skip.
            if (string.Equals(start.Compilation.AssemblyName, "Rask.Native", StringComparison.Ordinal))
            {
                return;
            }

            var webView = start.Compilation.GetTypeByMetadataName(NativeWebViewFullName);
            if (webView is null)
            {
                return;
            }

            // One action per type declaration, so the two modes are compared within a component and nowhere
            // wider. Everything the rule needs is inside the type, so no cross-action state is required.
            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeType(ctx, webView),
                SyntaxKind.ClassDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.StructDeclaration);
        });
    }

    private static void AnalyzeType(SyntaxNodeAnalysisContext context, INamedTypeSymbol webView)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;

        var markupSites = new List<ElementAccessExpressionSyntax>();
        var urlSites = new List<InvocationExpressionSyntax>();

        foreach (var node in declaration.DescendantNodes())
        {
            // A nested type is analysed by its own action; skipping its nodes here keeps each report on the
            // type that actually wrote the markup.
            if (node != declaration && node is TypeDeclarationSyntax)
            {
                continue;
            }

            switch (node)
            {
                case ElementAccessExpressionSyntax access when IsWebViewChildren(context, access, webView):
                    if (TakesUrlStep(access.Expression))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rask049, access.GetLocation()));
                    }
                    else
                    {
                        markupSites.Add(access);
                    }

                    break;

                case InvocationExpressionSyntax invocation when IsWebViewUrlStep(context, invocation, webView):
                    urlSites.Add(invocation);
                    break;
            }
        }

        // Only a component that does BOTH is wrong. Either alone is a perfectly good app.
        if (urlSites.Count == 0 || markupSites.Count == 0)
        {
            return;
        }

        var owner = declaration.Identifier.ValueText;
        foreach (var site in markupSites)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask050, site.GetLocation(), owner));
        }
    }

    // `NativeWebView[ … ]` with at least one child. `NativeWebView[]` composes nothing and is not markup mode.
    private static bool IsWebViewChildren(
        SyntaxNodeAnalysisContext context, ElementAccessExpressionSyntax access, INamedTypeSymbol webView) =>
        access.ArgumentList.Arguments.Count > 0 && ResolvesToWebView(context, access.Expression, webView);

    private static bool IsWebViewUrlStep(
        SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, INamedTypeSymbol webView) =>
        invocation.Expression is MemberAccessExpressionSyntax member
        && string.Equals(member.Name.Identifier.ValueText, UrlStep, StringComparison.Ordinal)
        && ResolvesToWebView(context, member.Expression, webView);

    // A chain hands back Build<T>, not T, so the type has to be unwrapped — without this every chain (which
    // is all of them now) walks straight past.
    private static bool ResolvesToWebView(
        SyntaxNodeAnalysisContext context, ExpressionSyntax expression, INamedTypeSymbol webView)
    {
        var resolved = BuilderEntry.ChainedComponent(
            ModelExtensions.GetTypeInfo(context.SemanticModel, expression, context.CancellationToken).Type);

        return SymbolEqualityComparer.Default.Equals(resolved, webView);
    }

    // Walk the chain the indexer is applied to, looking for a `.Url(…)` step. Syntactic on purpose: the step
    // is an extension method whose receiver is Build<NativeWebView>, and the name is what distinguishes it.
    private static bool TakesUrlStep(ExpressionSyntax expression)
    {
        var current = expression;
        while (true)
        {
            switch (current)
            {
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }:
                    if (string.Equals(member.Name.Identifier.ValueText, UrlStep, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    current = member.Expression;
                    continue;
                case MemberAccessExpressionSyntax access:
                    current = access.Expression;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized.Expression;
                    continue;
                default:
                    return false;
            }
        }
    }
}

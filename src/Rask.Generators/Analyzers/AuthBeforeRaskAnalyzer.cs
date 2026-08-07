using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK024 — warn when app.UseRask<App>() is wired before app.UseAuthentication(). Rask seeds the
// live session from HttpContext.User during the initial GET render and the WebSocket upgrade; if the
// authentication middleware runs after UseRask, the principal is empty at that point and every
// [Authorize] page challenges. The fix is to call UseAuthentication() (and UseAuthorization()) before
// UseRask(). Fires only when both calls are present and the earliest UseAuthentication is positioned
// after UseRask in source — an app with no UseAuthentication at all is left alone. Suppressible.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuthBeforeRaskAnalyzer : DiagnosticAnalyzer
{
    private const string RaskServerAssembly = "Rask.Server";
    private const string UseRaskMethod = "UseRask";
    private const string UseAuthenticationMethod = "UseAuthentication";
    private const string AspNetBuilderNamespace = "Microsoft.AspNetCore.Builder";

    private static readonly DiagnosticDescriptor Rask024 = new(
        "RASK024",
        "UseAuthentication must precede UseRask",
        "UseAuthentication() is called after UseRask<{0}>() — move it before UseRask so HttpContext.User is "
        + "populated on the GET render and the WebSocket upgrade; otherwise every [Authorize] page challenges",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Rask seeds the live session from HttpContext.User during the initial GET and the WS upgrade. If the "
        + "authentication middleware runs after UseRask, the principal is empty at that point and authorized "
        + "routes reject the user. Call app.UseAuthentication() (and UseAuthorization()) before app.UseRask().",
        DiagnosticHelp.Link("RASK024"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask024);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // A per-tree pass: the ordering check needs to see every UseRask / UseAuthentication call in
        // the file at once (Program.cs is one tree), which RegisterSyntaxNodeAction can't do statelessly.
        context.RegisterSemanticModelAction(Analyze);
    }

    private static void Analyze(SemanticModelAnalysisContext context)
    {
        var model = context.SemanticModel;
        var root = model.SyntaxTree.GetRoot(context.CancellationToken);

        var authPositions = new List<int>();
        var raskCalls = new List<(InvocationExpressionSyntax Inv, string AppType)>();

        foreach (var node in root.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax inv)
            {
                continue;
            }

            var name = MethodName(inv);
            if (!string.Equals(name, UseRaskMethod, StringComparison.Ordinal)
                && !string.Equals(name, UseAuthenticationMethod, StringComparison.Ordinal))
            {
                continue; // Cheap syntactic filter before the semantic lookup.
            }

            if (model.GetSymbolInfo(inv, context.CancellationToken).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (string.Equals(method.Name, UseAuthenticationMethod, StringComparison.Ordinal)
                && string.Equals(method.ContainingNamespace?.ToDisplayString(), AspNetBuilderNamespace,
                    StringComparison.Ordinal))
            {
                authPositions.Add(inv.SpanStart);
            }
            else if (string.Equals(method.Name, UseRaskMethod, StringComparison.Ordinal)
                     && string.Equals(method.ContainingAssembly?.Name, RaskServerAssembly, StringComparison.Ordinal))
            {
                var appType = method.TypeArguments.Length == 1 ? method.TypeArguments[0].Name : "TApp";
                raskCalls.Add((inv, appType));
            }
        }

        if (raskCalls.Count == 0 || authPositions.Count == 0)
        {
            return;
        }

        foreach (var (inv, appType) in raskCalls)
        {
            // Safe when any UseAuthentication call precedes this UseRask in source order. Only flag when
            // every UseAuthentication is positioned after it (the documented misordering footgun).
            if (authPositions.All(p => p > inv.SpanStart))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask024, NameLocation(inv), appType));
            }
        }
    }

    private static string? MethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => null
    };

    private static Location NameLocation(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.GetLocation(),
        _ => inv.Expression.GetLocation()
    };
}

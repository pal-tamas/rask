using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK023 — warn when an Img factory call omits Alt. Every informative image needs a text
// alternative (WCAG 1.1.1): without `alt`, screen readers fall back to announcing the file name or
// nothing at all. The fix is to pass a meaningful `Alt:`; for a purely decorative image, pass the
// empty string (`Alt: ""`) so assistive tech skips it. The warning fires only when no Alt argument
// is supplied at all — `Alt: ""` counts as supplied and stays quiet. Suppressible like any analyzer.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImgMissingAltAnalyzer : DiagnosticAnalyzer
{
    private const string ImgFullName = "Rask.Core.Components.Img";
    private const string RaskCoreAssembly = "Rask.Core";
    private const string GeneratedClassName = "Generated";
    private const string AltParameter = "Alt";

    private static readonly DiagnosticDescriptor Rask023 = new(
        "RASK023",
        "Img is missing Alt text",
        "Img is created without Alt — set Alt: to a text alternative, or Alt: \"\" for a decorative "
        + "image, so screen readers don't announce the file name (WCAG 1.1.1)",
        "Usage",
        DiagnosticSeverity.Warning,
        true,
        "Every informative image needs a text alternative. Pass a meaningful Alt:, or the empty "
        + "string (Alt: \"\") for a purely decorative image so assistive technology skips it.",
        DiagnosticHelp.Link("RASK023"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask023);

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

            var img = start.Compilation.GetTypeByMetadataName(ImgFullName);
            if (img is null)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(
                ctx => Analyze(ctx, img),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol img)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Is this the generated Img factory? A static `Generated.Img(...)` returning Rask.Core.Components.Img.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method
            || !method.IsStatic
            || !string.Equals(method.ContainingType?.Name, GeneratedClassName, StringComparison.Ordinal)
            || !string.Equals(method.Name, "Img", StringComparison.Ordinal)
            || !SymbolEqualityComparer.Default.Equals(method.ReturnType, img))
        {
            return;
        }

        if (!SuppliesAlt(invocation, method))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask023, NameLocation(invocation)));
        }
    }

    // Alt may be passed by name (Alt: "...") or positionally. Positional arguments fill parameters
    // left-to-right and (per C# rules) precede any named argument, so Alt is supplied positionally
    // when its parameter index is within the leading positional-argument count.
    private static bool SuppliesAlt(InvocationExpressionSyntax invocation, IMethodSymbol method)
    {
        var altIndex = -1;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (string.Equals(method.Parameters[i].Name, AltParameter, StringComparison.Ordinal))
            {
                altIndex = i;
                break;
            }
        }

        if (altIndex < 0)
        {
            return true; // No Alt parameter (shouldn't happen) — don't flag.
        }

        var positionalCount = 0;
        var sawNamed = false;
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon is { } name)
            {
                sawNamed = true;
                if (string.Equals(name.Name.Identifier.ValueText, AltParameter, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (!sawNamed)
            {
                positionalCount++;
            }
        }

        return altIndex < positionalCount;
    }

    private static Location NameLocation(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.GetLocation(),
        _ => inv.Expression.GetLocation()
    };
}

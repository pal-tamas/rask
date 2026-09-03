using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Reports a Rask component that carries ASP.NET's <c>[Route]</c> where it meant Rask's own.
/// </summary>
/// <remarks>
///     The two attributes share a name and differ only by namespace, so in a server project that already
///     has <c>using Microsoft.AspNetCore.Mvc;</c> the wrong one is one completion away — and Blazor's is
///     the same trap for anyone arriving from there. Nothing else catches it. MVC reads that attribute
///     only while scanning controllers and a component is never scanned; Blazor's is read by a renderer
///     this framework does not run; and <c>RoutesGenerator</c> matches on the full name, so it sees
///     nothing to register. The build is green, the page simply does not exist, and the first sign of it
///     is a 404 — which is why this is an Error rather than a warning you could scroll past.
///     <para>
///         A page carrying Rask's attribute as well is left alone: it registers correctly, so the
///         ASP.NET one is inert rather than harmful and failing the build would break working code.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AspNetRouteAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskRouteAttribute = "Rask.Core.Routing.RouteAttribute";
    private const string MvcRouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string BlazorRouteAttribute = "Microsoft.AspNetCore.Components.RouteAttribute";

    private static readonly DiagnosticDescriptor Rask067 = new(
        "RASK067",
        "ASP.NET route attribute on a Rask component",
        "'{0}' carries '{1}', which Rask's router never reads, so this page is never registered and its "
        + "URL 404s — apply Rask's own [Route] from Rask.Core.Routing instead",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Rask's route attribute and ASP.NET's share the short name 'Route' and differ only by "
                     + "namespace, so a file that already imports Microsoft.AspNetCore.Mvc (or "
                     + "Microsoft.AspNetCore.Components) can bind the wrong one without any warning. The "
                     + "consequence is silent: MVC only reads its attribute while scanning controllers, a "
                     + "component is never scanned, and Rask's route generator matches on the full name — so "
                     + "the build succeeds and the page is simply absent from the route table. Apply "
                     + "Rask.Core.Routing.RouteAttribute instead; the quick-fix rewrites the attribute in "
                     + "place, qualifying it where the short name would be ambiguous.",
        helpLinkUri: DiagnosticHelp.Link("RASK067"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask067);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!IsComponent(type))
        {
            return;
        }

        var attributes = type.GetAttributes();

        // Both attributes present: the page registers through Rask's, so the ASP.NET one changes
        // nothing. Reporting here would fail a build that is producing the right route table.
        if (attributes.Any(a => DerivesFrom(a.AttributeClass, RaskRouteAttribute)))
        {
            return;
        }

        foreach (var attribute in attributes)
        {
            if (ForeignRouteAttribute(attribute.AttributeClass) is not { } offending)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask067,
                attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? type.Locations[0],
                type.Name,
                offending));
        }
    }

    private static bool IsComponent(INamedTypeSymbol type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == ComponentFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static string? ForeignRouteAttribute(ITypeSymbol? attribute)
    {
        if (DerivesFrom(attribute, MvcRouteAttribute))
        {
            return MvcRouteAttribute;
        }

        return DerivesFrom(attribute, BlazorRouteAttribute) ? BlazorRouteAttribute : null;
    }

    // Matched through the base chain, not just by name: MVC's attribute is unsealed, so an alias
    // deriving from it is just as invisible to Rask's router as the original. Blazor's is sealed and
    // can only ever match itself, which the same walk covers for free.
    private static bool DerivesFrom(ITypeSymbol? attribute, string fullName)
    {
        for (var t = attribute; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == fullName)
            {
                return true;
            }
        }

        return false;
    }
}

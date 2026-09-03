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
///         A page that already registers is left alone, because failing a build that is producing the
///         right route table is the worse outcome. That means BOTH ways a page can register: Rask's own
///         <c>[Route]</c>, and <c>[NotFound]</c>, which <c>RoutesGenerator</c> registers with no
///         <c>[Route]</c> at all. Missing the second one would not merely be a spurious error — the
///         obvious fix for it produces <c>[NotFound]</c> beside a <c>[Route]</c>, which is RASK013 and
///         drops the catch-all from the registry altogether.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AspNetRouteAttributeAnalyzer : DiagnosticAnalyzer
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskRouteAttribute = "Rask.Core.Routing.RouteAttribute";
    private const string RaskNotFoundAttribute = "Rask.Core.Routing.NotFoundAttribute";
    private const string MvcRouteAttribute = "Microsoft.AspNetCore.Mvc.RouteAttribute";
    private const string BlazorRouteAttribute = "Microsoft.AspNetCore.Components.RouteAttribute";

    private static readonly DiagnosticDescriptor Rask067 = new(
        "RASK067",
        "ASP.NET route attribute on a Rask component",
        "'{0}' carries {1}, which Rask's router never reads, so this page is never registered and its "
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

        // Already registered — through Rask's [Route], or as the [NotFound] catch-all, which needs no
        // template. Either way the route table is correct and the ASP.NET attribute is inert.
        if (attributes.Any(a => DerivesFrom(a.AttributeClass, RaskRouteAttribute)
                                || DerivesFrom(a.AttributeClass, RaskNotFoundAttribute)))
        {
            return;
        }

        foreach (var attribute in attributes)
        {
            if (!IsForeignRoute(attribute.AttributeClass))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask067,
                attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? type.Locations[0],
                type.Name,
                Describe(attribute.AttributeClass)));
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

    private static bool IsForeignRoute(ITypeSymbol? attribute) =>
        DerivesFrom(attribute, MvcRouteAttribute) || DerivesFrom(attribute, BlazorRouteAttribute);

    // Names the attribute the developer actually WROTE, and only then what it derives from. Reporting
    // the base alone put a symbol in the message that appears nowhere in the file being squiggled.
    private static string Describe(ITypeSymbol? attribute)
    {
        var written = attribute?.ToDisplayString() ?? MvcRouteAttribute;
        var root = DerivesFrom(attribute, MvcRouteAttribute) ? MvcRouteAttribute : BlazorRouteAttribute;

        return written == root ? $"'{written}'" : $"'{written}', which derives from '{root}'";
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

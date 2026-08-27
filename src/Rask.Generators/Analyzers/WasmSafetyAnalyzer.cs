using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

/// <summary>
///     Reports a routed page that cannot run in the browser, because something it injects only
///     exists on the server.
/// </summary>
/// <remarks>
///     Informational, not a fault. Such a page is completely correct — it simply stays server-live
///     rather than moving into WebAssembly, which for most apps is the right answer and never needs
///     changing. The diagnostic exists so that "why did this page not move?" has an answer at the
///     call site rather than only in a runtime log.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WasmSafetyAnalyzer : DiagnosticAnalyzer
{
    private const string RouteAttribute = "Rask.Core.Routing.RouteAttribute";
    private const string ComponentFullName = "Rask.Core.Component";
    private const string ServerAssembly = "Rask.Server";

    // The two shapes that carry a database into a page. Named rather than discovered because the
    // question is "can this type exist in a browser", which no reference graph here can answer: the
    // analyzer compiles against the server half and has no view of what the browser half references.
    private const string DbContext = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbContextFactory = "Microsoft.EntityFrameworkCore.IDbContextFactory`1";

    private static readonly DiagnosticDescriptor Rask054 = new(
        "RASK054",
        "Page cannot run in the browser",
        "'{0}' injects '{1}', which only exists on the server, so this page stays server-live and will not move into WebAssembly",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        true,
        description: "How far a page climbs is decided per page: one that can run in the browser moves there "
                     + "once the bundle is available, and one that cannot stays live over the WebSocket. A page "
                     + "reaching a database, or anything else that only exists in the server process, is in the "
                     + "second group. That is not a fault — it is the correct outcome, and for most apps it is "
                     + "every data page. This is Info rather than a warning for exactly that reason. Reach the "
                     + "same data through a query or a CQRS message and the page becomes eligible, because those "
                     + "already cross the wire. The list of server-only types is deliberately short and not "
                     + "exhaustive; it names what this framework hands people, not everything that could fail.",
        helpLinkUri: DiagnosticHelp.Link("RASK054"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask054);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only a routed page is worth reporting. A shared component injecting a DbContext is a
        // property of whatever page uses it, and saying so here would point at a file whose author
        // cannot see which page it affects.
        if (type.IsAbstract || !IsComponent(type) || !HasRoute(type))
        {
            return;
        }

        foreach (var parameter in type.InstanceConstructors
                     .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                     .SelectMany(c => c.Parameters))
        {
            if (ServerOnlyName(parameter.Type) is not { } offending)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rask054,
                parameter.Locations.FirstOrDefault() ?? type.Locations[0],
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

    private static bool HasRoute(ISymbol type) =>
        type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == RouteAttribute);

    private static string? ServerOnlyName(ITypeSymbol type)
    {
        // Anything from the server host assembly is server-only by definition — it is the assembly a
        // browser build does not reference.
        if (type.ContainingAssembly?.Name == ServerAssembly)
        {
            return type.ToDisplayString();
        }

        if (type is INamedTypeSymbol { IsGenericType: true } generic
            && generic.ConstructedFrom.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore.IDbContextFactory<", System.StringComparison.Ordinal))
        {
            return generic.ToDisplayString();
        }

        if (type.OriginalDefinition.ToDisplayString() == DbContextFactory)
        {
            return type.ToDisplayString();
        }

        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == DbContext)
            {
                return type.ToDisplayString();
            }
        }

        return null;
    }
}

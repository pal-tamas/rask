using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

// RASK033 — prefer the generated type-safe route URL over a hardcoded path for INTERNAL navigation.
// Rask emits a `Routes.<Page>()` RouteUrl factory for every page's primary [Route] (see RoutesGenerator).
// Passing the raw path string to internal navigation — `Navigator.NavigateTo("/todos")` or a `RouteUrl`
// slot like `NavLink(Href: "/todos")` / `BsNavItem.Href("/todos")` (string → RouteUrl implicit conversion)
// — bypasses that safety: a renamed or removed [Route] leaves a dead link that still compiles.
//
// Only INTERNAL paths that map to a generated PARAMETERLESS route factory are flagged. External URLs
// (`RouteUrl.External(...)`, `https://…`), parameterised routes (`/users/42` → `Routes.UserDetailPage("42")`),
// and secondary [Route] templates that have no generated formatter (`/todos/new`) are deliberately left
// alone — the factory produces a page's FIRST template only, so those literals never match a map entry.
// A Warning by default; suppress a deliberate string with `RouteUrl.External(...)` or `#pragma`.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InternalRouteStringAnalyzer : DiagnosticAnalyzer
{
    private const string RouteAttrFullName = "Rask.Core.Routing.RouteAttribute";
    private const string ParentRouteAttrFullName = "Rask.Core.Routing.ParentRouteAttribute";
    private const string NavigatorFullName = "Rask.Core.Routing.Navigator";
    private const string RouteUrlFullName = "Rask.Core.Routing.RouteUrl";
    private const string RaskCoreAssembly = "Rask.Core";

    private static readonly DiagnosticDescriptor Rask033 = new(
        "RASK033",
        "Prefer the generated route URL over a hardcoded path",
        "Navigate with the generated 'Routes.{0}()' instead of the string \"{1}\" — a renamed or removed "
        + "[Route] then becomes a compile error, not a silent dead link",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Rask generates a type-safe RouteUrl factory ('Routes.<Page>()') for every page's primary [Route]. "
        + "Passing the raw path string to internal navigation (Navigator.NavigateTo, or a RouteUrl Href/To "
        + "slot) bypasses that safety: a renamed or removed route leaves a dead link that still compiles. "
        + "Only internal paths that map to a parameterless generated factory are flagged; external URLs "
        + "(RouteUrl.External) and parameterised/secondary routes are left alone.",
        DiagnosticHelp.Link("RASK033"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask033);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            // Rask.Core defines Navigator/RouteUrl and navigates with strings internally; never flag it.
            if (string.Equals(start.Compilation.AssemblyName, RaskCoreAssembly, StringComparison.Ordinal))
            {
                return;
            }

            var navigator = start.Compilation.GetTypeByMetadataName(NavigatorFullName);
            var routeUrl = start.Compilation.GetTypeByMetadataName(RouteUrlFullName);
            var routeAttr = start.Compilation.GetTypeByMetadataName(RouteAttrFullName);
            var parentAttr = start.Compilation.GetTypeByMetadataName(ParentRouteAttrFullName);
            if (navigator is null || routeUrl is null || routeAttr is null)
            {
                return;
            }

            // Map each navigable page's parameterless static URL → its Routes.<TypeName>() method name.
            var map = BuildRouteMap(start.Compilation.Assembly.GlobalNamespace, routeAttr, parentAttr);
            if (map.Count == 0)
            {
                return;
            }

            start.RegisterOperationAction(ctx => AnalyzeInvocation(ctx, navigator, map), OperationKind.Invocation);
            start.RegisterOperationAction(ctx => AnalyzeConversion(ctx, routeUrl, map), OperationKind.Conversion);
        });
    }

    // --- navigation sinks -----------------------------------------------------------------------

    private static void AnalyzeInvocation(
        OperationAnalysisContext context, INamedTypeSymbol navigator, IReadOnlyDictionary<string, string> map)
    {
        var inv = (IInvocationOperation)context.Operation;
        var method = inv.TargetMethod;
        if (method.Name != "NavigateTo"
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType, navigator))
        {
            return;
        }

        // NavigateTo(string path, …) / NavigateTo(string path, query, …) — flag the path argument. The
        // NavigateTo(RouteUrl, …) overload has no string param and is covered by AnalyzeConversion instead.
        foreach (var arg in inv.Arguments)
        {
            if (arg.Parameter?.Type.SpecialType == SpecialType.System_String)
            {
                Report(context, arg.Value, map);
                return;
            }
        }
    }

    private static void AnalyzeConversion(
        OperationAnalysisContext context, INamedTypeSymbol routeUrl, IReadOnlyDictionary<string, string> map)
    {
        var conv = (IConversionOperation)context.Operation;
        // The implicit string → RouteUrl conversion behind every RouteUrl slot (Href:/To:/RouteUrl params).
        if (conv.Type is not null && SymbolEqualityComparer.Default.Equals(conv.Type, routeUrl))
        {
            Report(context, conv, map);
        }
    }

    private static void Report(
        OperationAnalysisContext context, IOperation value, IReadOnlyDictionary<string, string> map)
    {
        // Unwrap a wrapping conversion (string literal → RouteUrl) to reach the constant string.
        var operand = value is IConversionOperation c ? c.Operand : value;
        if (operand.ConstantValue is not { HasValue: true, Value: string path }
            || path.Length == 0
            || path.IndexOf("://", StringComparison.Ordinal) >= 0)
        {
            return;
        }

        if (map.TryGetValue(Normalize(path), out var typeName))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rask033, operand.Syntax.GetLocation(), typeName, path));
        }
    }

    // --- route map ------------------------------------------------------------------------------

    private readonly record struct RoutePage(string TypeName, string FirstTemplate, string? ParentFqn);

    private static Dictionary<string, string> BuildRouteMap(
        INamespaceSymbol root, INamedTypeSymbol routeAttr, INamedTypeSymbol? parentAttr)
    {
        var pages = new Dictionary<string, RoutePage>(StringComparer.Ordinal);
        CollectPages(root, routeAttr, parentAttr, pages);

        // A type referenced via [ParentRoute] is a layout, not a nav target — never suggest Routes.<Layout>().
        var layouts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages.Values)
        {
            if (page.ParentFqn is { } p)
            {
                layouts.Add(p);
            }
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in pages)
        {
            if (layouts.Contains(entry.Key) || !TryResolveStaticPath(entry.Value, pages, out var path))
            {
                continue;
            }

            // First writer wins; a genuine same-URL collision is already RASK010's job to flag.
            if (!map.ContainsKey(path))
            {
                map[path] = entry.Value.TypeName;
            }
        }

        return map;
    }

    private static void CollectPages(INamespaceOrTypeSymbol container, INamedTypeSymbol routeAttr,
        INamedTypeSymbol? parentAttr, Dictionary<string, RoutePage> pages)
    {
        foreach (var member in container.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    CollectPages(ns, routeAttr, parentAttr, pages);
                    break;
                case INamedTypeSymbol type:
                    TryAddPage(type, routeAttr, parentAttr, pages);
                    CollectPages(type, routeAttr, parentAttr, pages); // nested types
                    break;
            }
        }
    }

    private static void TryAddPage(INamedTypeSymbol type, INamedTypeSymbol routeAttr,
        INamedTypeSymbol? parentAttr, Dictionary<string, RoutePage> pages)
    {
        string? firstTemplate = null;
        string? parentFqn = null;
        foreach (var attr in type.GetAttributes())
        {
            var cls = attr.AttributeClass;
            if (cls is null)
            {
                continue;
            }

            if (firstTemplate is null && SymbolEqualityComparer.Default.Equals(cls, routeAttr))
            {
                // FIRST [Route] only — the generated URL factory formats a page's first template.
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string t)
                {
                    firstTemplate = t;
                }
            }
            else if (parentAttr is not null && SymbolEqualityComparer.Default.Equals(cls, parentAttr))
            {
                if (attr.ConstructorArguments.Length > 0
                    && attr.ConstructorArguments[0].Value is INamedTypeSymbol p)
                {
                    parentFqn = p.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        if (firstTemplate is null)
        {
            return; // no [Route] (e.g. [NotFound]) → no generated formatter
        }

        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        pages[fqn] = new RoutePage(type.Name, firstTemplate, parentFqn);
    }

    // Composes a page's first template with its [ParentRoute] chain (mirrors RoutesGenerator's
    // TryResolveFullTemplate). Returns false for any route with a path parameter ('{…}') — those need a
    // Routes.<Page>(arg) call the analyzer can't reconstruct from a literal — or an unresolvable parent.
    private static bool TryResolveStaticPath(
        RoutePage page, IReadOnlyDictionary<string, RoutePage> pages, out string path)
    {
        path = "";
        var parts = new List<string> { page.FirstTemplate };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parentFqn = page.ParentFqn;
        while (parentFqn is not null)
        {
            if (!seen.Add(parentFqn) || !pages.TryGetValue(parentFqn, out var parent))
            {
                return false; // cycle, or a parent outside this assembly — don't guess
            }

            parts.Insert(0, parent.FirstTemplate);
            parentFqn = parent.ParentFqn;
        }

        var full = string.Join("/", parts);
        if (full.IndexOf('{') >= 0)
        {
            return false; // parameterised route — not a parameterless factory
        }

        path = Normalize(full);
        return true;
    }

    // Canonicalises a route path the way the generated formatter emits it: split on '/', drop empty
    // segments, re-join under a single leading slash; an all-empty template is the root "/".
    private static string Normalize(string raw)
    {
        var sb = new StringBuilder();
        foreach (var segment in raw.Split('/'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            sb.Append('/').Append(segment);
        }

        return sb.Length == 0 ? "/" : sb.ToString();
    }
}

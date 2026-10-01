using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class RoutesGenerator
{
    private static readonly DiagnosticDescriptor Rask003 = new(
        "RASK003",
        "Malformed route template",
        "Route template '{0}' on '{1}' is malformed: {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "The template is parsed at build time so a broken route fails here rather than at the first "
                     + "request. Typed routes support literal segments and single-parameter segments with an optional "
                     + "':constraint' and trailing '?'; they do not support catch-alls or a segment that mixes literal "
                     + "text with a parameter.",
        helpLinkUri: DiagnosticHelp.Link("RASK003"));

    private static readonly DiagnosticDescriptor Rask004 = new(
        "RASK004",
        "Route segment has no matching property",
        "Route segment '{{{0}}}' on '{1}' has no matching public settable property — add a public "
        + "settable property named '{0}' to '{1}', or remove '{{{0}}}' from the route template",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Route parameters bind by name onto public settable properties. A segment with nothing to bind to "
                     + "would silently discard part of the URL, so it is a build error rather than a value that quietly "
                     + "never arrives.",
        helpLinkUri: DiagnosticHelp.Link("RASK004"));

    private static readonly DiagnosticDescriptor Rask005 = new(
        "RASK005",
        "Property type does not match route constraint",
        "Property '{0}.{1}' has type '{2}', incompatible with route constraint '{3}' — change the property "
        + "type to one the '{3}' constraint accepts, or adjust the constraint in the route template",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "The constraint in the template and the property's CLR type are two statements of the same thing, "
                     + "and the router trusts the constraint. If they disagree, a URL the router accepted would fail to "
                     + "bind at request time.",
        helpLinkUri: DiagnosticHelp.Link("RASK005"));

    private static readonly DiagnosticDescriptor Rask006 = new(
        "RASK006",
        "[QueryParam] applied to a path-segment property",
        "Property '{0}.{1}' has [QueryParam] but is also bound by path segment '{{{2}}}' — a value can't "
        + "come from both; remove [QueryParam] to bind it from the path, or rename the property or segment "
        + "so they don't collide",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A value comes from the path or from the query string, never both. Marking a path-bound property "
                     + "[QueryParam] describes a binding that cannot happen.",
        helpLinkUri: DiagnosticHelp.Link("RASK006"));

    private static readonly DiagnosticDescriptor Rask007 = new(
        "RASK007",
        "[ParentRoute] cycle",
        "[ParentRoute] forms a cycle starting at '{0}' — break the cycle so the [ParentRoute] chain ends "
        + "at a page with no parent",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "[ParentRoute] composes a page's template onto its parent's, so the chain has to terminate at a "
                     + "page with no parent. A cycle has no root to compose from and would not terminate.",
        helpLinkUri: DiagnosticHelp.Link("RASK007"));

    private static readonly DiagnosticDescriptor Rask008 = new(
        "RASK008",
        "[RouteParam] without matching path segment",
        "Property '{0}.{1}' has [RouteParam] but no path segment matches '{2}' — add a '{{{2}}}' segment "
        + "to the route template, or remove [RouteParam] from the property",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "[RouteParam] says 'bind me from the path', so a property carrying it with no matching segment — "
                     + "in this template or an ancestor's, via [ParentRoute] — would never be set. Names are matched "
                     + "exactly.",
        helpLinkUri: DiagnosticHelp.Link("RASK008"));

    private static readonly DiagnosticDescriptor Rask009 = new(
        "RASK009",
        "[RouteParam] on a non-routed class",
        "Property '{0}.{1}' has [RouteParam] but '{0}' is not a valid route target ({2}) — add [Route(\"/…\")] to "
        + "'{0}', or remove [RouteParam]",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Route binding only runs for pages the router can reach. On a class with no [Route] and "
                     + "no parent chain to one, the attribute describes binding that never happens, so the property "
                     + "silently keeps its default.",
        helpLinkUri: DiagnosticHelp.Link("RASK009"));

    private static readonly DiagnosticDescriptor Rask010 = new(
        "RASK010",
        "[QueryParam] on a non-routed class",
        "Property '{0}.{1}' has [QueryParam] but '{0}' is not a valid route target ({2}) — add [Route(\"/…\")] to "
        + "'{0}', or remove [QueryParam]",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "As RASK009, for [QueryParam]: query binding is part of routing a page, so it does nothing on a "
                     + "class the router never instantiates.",
        helpLinkUri: DiagnosticHelp.Link("RASK010"));

    private static readonly DiagnosticDescriptor Rask011 = new(
        "RASK011",
        "Route/query param type must implement IParsable<T>",
        "Property '{0}.{1}' of type '{2}' must be 'string' or implement 'System.IParsable<{2}>' to be bound "
        + "by [RouteParam]/[QueryParam] — use a parsable type (int, Guid, DateOnly, an enum, your own "
        + "IParsable<T>), or accept it as 'string' and convert inside the page",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A URL segment is text, so binding it needs a way to parse it. 'string' is taken verbatim; "
                     + "anything else must implement System.IParsable<T> — which every built-in numeric, Guid, "
                     + "DateOnly/DateTime, bool and enum already does.",
        helpLinkUri: DiagnosticHelp.Link("RASK011"));

    private static readonly DiagnosticDescriptor Rask012 = new(
        "RASK012",
        "Multiple [NotFound] components",
        "Multiple [NotFound] components found in this assembly; only one is allowed ('{0}' is a duplicate) "
        + "— remove [NotFound] from all but one component",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "[NotFound] marks the single catch-all page for an assembly. With two, which one answers an "
                     + "unmatched URL would depend on registration order.",
        helpLinkUri: DiagnosticHelp.Link("RASK012"));

    private static readonly DiagnosticDescriptor Rask031 = new(
        "RASK031",
        "Duplicate route template",
        "Route template '{0}' matches the same URL as another page ('{1}') — which one renders is "
        + "arbitrary; give this page a distinct route",
        DiagnosticHelp.Category,
        // Warning, not Error: a route collision is a real bug, but promoting it to Error would hard-break
        // apps that compile today the moment they upgrade (and the app still runs, just picks arbitrarily).
        DiagnosticSeverity.Warning,
        true,
        description: "Templates are compared the way the runtime router matches them, not as strings: literals match "
                     + "case-insensitively, surrounding slashes are trimmed, and parameter names and ':constraints' are "
                     + "ignored. So '/Products' collides with '/products', and '/item/{id:int}' with '/item/{slug}'. "
                     + "Only pages without a [ParentRoute] are compared — the check under-reports rather than risk a "
                     + "false positive on a composed path.",
        helpLinkUri: DiagnosticHelp.Link("RASK031"));

    private static readonly DiagnosticDescriptor Rask013 = new(
        "RASK013",
        "[NotFound] cannot be combined with [Route]",
        "Class '{0}' has both [NotFound] and [Route]; remove [Route] (NotFound is the catch-all)",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "[NotFound] IS the fallback — it answers whatever no other route matched. Giving it a [Route] as "
                     + "well asks it to be both a specific path and the catch-all for every other one.",
        helpLinkUri: DiagnosticHelp.Link("RASK013"));

    private static readonly DiagnosticDescriptor Rask097 = new(
        "RASK097",
        "Route helper name collides",
        "The route helper for '{0}' cannot be generated as '{1}': {2} — rename the page or move it to another folder",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Every page gets one method on the project's single Routes class. Pages that share a type name "
                     + "are grouped under nested classes named after the folders that tell them apart "
                     + "(Routes.Admin.HomePage), so a folder name cannot also be the name of a page at the same level.",
        helpLinkUri: DiagnosticHelp.Link("RASK097"));

    private static void EmitOrphanDiagnostics(SourceProductionContext spc, ImmutableArray<OrphanCandidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var c in candidates)
        {
            foreach (var p in c.Props)
            {
                var descriptor = p.IsRouteParam ? Rask009 : Rask010;
                spc.ReportDiagnostic(Diagnostic.Create(descriptor, p.Location.ToLocation(), c.ClassFqn, p.Name,
                    c.Reason));
            }
        }
    }

    private static List<Candidate> DropAmbiguous(SourceProductionContext spc, ImmutableArray<Candidate> candidates)
    {
        // RASK013: a class with both [NotFound] and [Route] is ambiguous — drop those
        // candidates from registry emission so neither catch-all nor typed route gets
        // registered for a misconfigured type.
        var filtered = new List<Candidate>(candidates.Length);
        foreach (var c in candidates)
        {
            if (c.IsNotFound && c.HasRouteAttr)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask013, c.RouteAttrLocation.ToLocation(),
                    c.FullyQualifiedName));
                continue;
            }

            filtered.Add(c);
        }

        return filtered;
    }

    private static List<Candidate> DropDuplicateNotFound(SourceProductionContext spc, List<Candidate> filtered)
    {
        // RASK012: only one [NotFound] per assembly. Report on every duplicate after the
        // first (sorted by FQN for stable diagnostics).
        var notFoundCandidates = filtered.Where(c => c.IsNotFound)
            .OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();
        if (notFoundCandidates.Count > 1)
        {
            foreach (var dup in notFoundCandidates.Skip(1))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask012, dup.RouteAttrLocation.ToLocation(),
                    dup.FullyQualifiedName));
            }

            // Keep only the first NotFound in downstream emission so duplicates don't
            // create competing catch-all registrations.
            var keepFqn = notFoundCandidates[0].FullyQualifiedName;
            filtered = filtered
                .Where(c => !c.IsNotFound || string.Equals(c.FullyQualifiedName, keepFqn, StringComparison.Ordinal))
                .ToList();
        }

        return filtered;
    }

    private static void ReportRouteCollisions(SourceProductionContext spc, List<Candidate> filtered)
    {
        // RASK031: two different top-level pages must not resolve to the same route — both would match
        // the same URL and the winner would be arbitrary. Group by the NORMALIZED pattern the runtime
        // router actually matches on (see NormalizeTemplate — case-insensitive literals, trimmed slashes,
        // parameter name/constraint ignored), not the verbatim [Route] string, so /Products vs /products,
        // /x vs x/, and /{id:int} vs /{id:guid} are all caught. Restricted to pages WITHOUT a
        // [ParentRoute], whose full path IS the template; parent-composed paths aren't resolved here, so
        // this deliberately under-reports rather than risk a false positive on a nested route.
        var collisions = new Dictionary<string, List<(Candidate Page, string Template)>>(StringComparer.Ordinal);
        var seenFqns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var c in filtered)
        {
            if (c.IsNotFound || c.ParentTypeFqn is not null)
            {
                continue;
            }

            foreach (var template in c.Templates)
            {
                var key = NormalizeTemplate(template);
                if (!seenFqns.TryGetValue(key, out var fqns))
                {
                    fqns = new HashSet<string>(StringComparer.Ordinal);
                    seenFqns[key] = fqns;
                    collisions[key] = new List<(Candidate, string)>();
                }

                // A partial class re-declares the same FQN — only distinct pages count as a collision.
                if (fqns.Add(c.FullyQualifiedName))
                {
                    collisions[key].Add((c, template));
                }
            }
        }

        foreach (var pages in collisions.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Value))
        {
            if (pages.Count < 2)
            {
                continue;
            }

            // Report on every colliding page after the first (ordered by fully-qualified name for a
            // stable canonical page), naming this page's own template and the page it collides with.
            var ordered = pages.OrderBy(x => x.Page.FullyQualifiedName, StringComparer.Ordinal).ToList();
            var firstFqn = ordered[0].Page.FullyQualifiedName;
            foreach (var dup in ordered.Skip(1))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask031, dup.Page.RouteAttrLocation.ToLocation(),
                    dup.Template, firstFqn));
            }
        }
    }

    // A nested class can share neither a name with a helper beside it (CS0102) nor with the class that
    // holds it (CS0542). Either is reported once, and the page that cannot be emitted is dropped, so the
    // author reads what to rename instead of a compile error inside generated code.
    private static void RejectCollisions(SourceProductionContext spc, RoutesNode node, string fqn)
    {
        foreach (var page in node.Pages.ToList())
        {
            string? reason = null;
            if (string.Equals(page.TypeName, node.Name, StringComparison.Ordinal))
            {
                reason = "a member cannot share the name of the class that holds it";
            }
            else if (node.Children.ContainsKey(page.TypeName))
            {
                reason = $"'{page.TypeName}' is also the nested class holding the pages from the '{page.TypeName}' "
                         + "folder that share a type name with another page";
            }

            if (reason is not null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask097, page.RouteAttrLocation.ToLocation(),
                    page.FullyQualifiedName, $"{fqn.Replace("global::", string.Empty)}.{page.TypeName}()", reason));
                node.Pages.Remove(page);
            }
        }

        foreach (var child in node.Children.Values.ToList())
        {
            if (string.Equals(child.Name, node.Name, StringComparison.Ordinal))
            {
                foreach (var page in child.AllPages())
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask097, page.RouteAttrLocation.ToLocation(),
                        page.FullyQualifiedName, $"{fqn.Replace("global::", string.Empty)}.{child.Name}",
                        "a nested class cannot share the name of the class that holds it"));
                }

                node.Children.Remove(child.Name);
                continue;
            }

            RejectCollisions(spc, child, $"{fqn}.{child.Name}");
        }
    }

    // RASK011 for every [RouteParam] / [QueryParam] whose type cannot be parsed from a URL.
    private static bool ReportUnbindable(SourceProductionContext spc, Candidate c)
    {
        var unbindable = false;
        foreach (var prop in c.Properties.Where(static p => (p.HasRouteParam || p.HasQueryParam) && !p.IsParsable))
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask011, prop.Location.ToLocation(), c.FullyQualifiedName,
                prop.Name, prop.TypeFqn));
            unbindable = true;
        }

        return unbindable;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class RoutesGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RouteAttrFullName = "Rask.Core.Routing.RouteAttribute";
    private const string NotFoundAttrFullName = "Rask.Core.Routing.NotFoundAttribute";
    private const string ParentRouteAttrFullName = "Rask.Core.Routing.ParentRouteAttribute";
    private const string QueryParamAttrFullName = "Rask.Core.Routing.QueryParamAttribute";
    private const string RouteParamAttrFullName = "Rask.Core.Routing.RouteParamAttribute";
    private const string SkipFactoryAttrFullName = "Rask.Core.SkipFactoryAttribute";
    private const string RouteUrlFullName = "global::Rask.Core.Routing.RouteUrl";
    private const string NotFoundTemplate = "{**__rask_notfound}";

    // The routable base class. A page declares its template by overriding Page.Route with a compile-time
    // constant; this generator reads that constant out of the override's syntax, which is why RASK036
    // exists (a non-constant override has nothing to read and would silently never register).

    private const string FormatterFullName = "global::Rask.Core.Routing.RouteValueFormatter";

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
        "Property '{0}.{1}' has [RouteParam] but '{0}' is not a valid route target ({2}) — derive '{0}' from "
        + "Page (a concrete subclass), or remove [RouteParam]",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Route binding only runs for pages the router can reach. On a class that is not a Page and has "
                     + "no Parent chain to one, the attribute describes binding that never happens, so the property "
                     + "silently keeps its default.",
        helpLinkUri: DiagnosticHelp.Link("RASK009"));

    private static readonly DiagnosticDescriptor Rask010 = new(
        "RASK010",
        "[QueryParam] on a non-routed class",
        "Property '{0}.{1}' has [QueryParam] but '{0}' is not a valid route target ({2}) — derive '{0}' from "
        + "Page (a concrete subclass), or remove [QueryParam]",
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

    // RASK047 ("Page.Route must be a compile-time constant") is retired along with the Page base class:
    // a route is declared by [Route], whose argument is an attribute argument and therefore constant by
    // construction. The id stays retired, not reused.

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                // A routable class is either attributed ([NotFound]) or derives from Page — and deriving
                // needs a base list, so the cheap syntax filter accepts both and GetCandidate rejects the
                // rest via the semantic model.
                static (node, _) => node is ClassDeclarationSyntax c
                                    && (c.AttributeLists.Count > 0 || c.BaseList is not null)
                                    && !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        // The per-page `SomePage.Url(...)` / `SomePage.Go(...)` helpers are C# 14 static extension members.
        // Generated code is compiled with the CONSUMER's language version, and below C# 14 an extension
        // block does not fail with a clean "feature unavailable" message — it fails as a parse-error
        // cascade (CS1001/CS1513/CS1519) pointing inside generated source, which is unactionable. So the
        // emission is gated here and the legacy Routes.X(...) factories carry those consumers.
        var supportsExtensionMembers = context.ParseOptionsProvider.Select(static (options, _) =>
            options is CSharpParseOptions cs && cs.LanguageVersion >= LanguageVersion.CSharp14);

        var grouped = candidates.Collect().Combine(supportsExtensionMembers);
        context.RegisterSourceOutput(grouped, static (spc, pair) => Emit(spc, pair.Left, pair.Right));

        var orphanCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c
                                    && c.Members.OfType<PropertyDeclarationSyntax>()
                                        .Any(p => p.AttributeLists.Count > 0),
                static (ctx, _) => GetOrphanCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(orphanCandidates.Collect(),
            static (spc, list) => EmitOrphanDiagnostics(spc, list));

    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.IsAbstract || symbol.IsGenericType)
        {
            return null;
        }

        if (!InheritsFromComponent(symbol))
        {
            return null;
        }

        var templates = new List<string>();
        Location? firstRouteAttrLocation = null;
        Location? notFoundAttrLocation = null;
        string? parentTypeFqn = null;
        var hasNotFound = false;

        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == RouteAttrFullName)
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string t)
                {
                    templates.Add(t);
                }

                firstRouteAttrLocation ??= attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            }
            else if (name == NotFoundAttrFullName)
            {
                hasNotFound = true;
                notFoundAttrLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            }
            else if (name == ParentRouteAttrFullName)
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is INamedTypeSymbol p)
                {
                    parentTypeFqn = p.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        var properties = GetPageProperties(symbol);

        if (hasNotFound)
        {
            return new Candidate(
                ns,
                symbol.Name,
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                new EquatableArray<string>(new List<string> { NotFoundTemplate }),
                parentTypeFqn,
                new EquatableArray<RoutePropInfo>(properties),
                new LocationInfo(notFoundAttrLocation),
                true,
                templates.Count > 0);
        }

        if (templates.Count == 0)
        {
            return null;
        }

        return new Candidate(
            ns,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<string>(templates),
            parentTypeFqn,
            new EquatableArray<RoutePropInfo>(properties),
            new LocationInfo(firstRouteAttrLocation),
            IsNotFound: false,
            HasRouteAttr: true,
            IsPubliclyVisible: IsPubliclyVisible(symbol));
    }

    /// <summary>
    ///     Whether the type is visible outside its assembly, walking containing types so a public nested type
    ///     inside an internal one reads as internal. The generated navigation container's accessibility has to
    ///     match: a static extension member takes the receiver type in its signature, so a public container
    ///     over an internal page is CS0051.
    /// </summary>
    private static bool IsPubliclyVisible(INamedTypeSymbol symbol)
    {
        for (ISymbol? s = symbol; s is not null and not INamespaceSymbol; s = s.ContainingType)
        {
            if (s.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    // Normalize a route template to the shape the runtime router matches on (mirrors
    // Rask.Core.Routing.RoutePattern, which can't be referenced from this netstandard2.0 generator):
    // trim surrounding slashes, lowercase literal segments (literals match OrdinalIgnoreCase), and
    // collapse each parameter to a positional placeholder — the router ignores the parameter's name and
    // its `:constraint`, and distinguishes only required vs optional vs catch-all. Two templates that
    // normalize equal match the same set of URLs.
    private static string NormalizeTemplate(string template)
    {
        var raw = template.Trim('/');
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var parts = raw.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length >= 2 && p[0] == '{' && p[p.Length - 1] == '}')
            {
                var inner = p.Substring(1, p.Length - 2);
                if (inner.StartsWith("**", StringComparison.Ordinal)
                    || (inner.Length > 0 && inner[0] == '*'))
                {
                    parts[i] = "{**}"; // catch-all — name ignored
                }
                else
                {
                    parts[i] = inner.Length > 0 && inner[inner.Length - 1] == '?' ? "{?}" : "{}";
                }
            }
            else
            {
                parts[i] = p.ToLowerInvariant(); // literal — matched case-insensitively
            }
        }

        return string.Join("/", parts);
    }

    private static bool InheritsFromComponent(INamedTypeSymbol symbol)
    {
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == ComponentFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static List<RoutePropInfo> GetPageProperties(INamedTypeSymbol symbol)
    {
        var result = new List<RoutePropInfo>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
            {
                continue;
            }

            if (prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared)
            {
                continue;
            }

            if (prop.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (prop.SetMethod is null)
            {
                continue;
            }

            if (prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            string? queryParamName = null;
            var hasQueryParam = false;
            string? routeParamName = null;
            var hasRouteParam = false;
            foreach (var attr in prop.GetAttributes())
            {
                var attrName = attr.AttributeClass?.ToDisplayString();
                if (attrName == QueryParamAttrFullName)
                {
                    hasQueryParam = true;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string qn)
                    {
                        queryParamName = qn;
                    }
                }
                else if (attrName == RouteParamAttrFullName)
                {
                    hasRouteParam = true;
                    if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string rn)
                    {
                        routeParamName = rn;
                    }
                }
            }

            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                             || (prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType ==
                                 SpecialType.System_Nullable_T);

            var underlyingTypeName = GetUnderlyingTypeName(prop.Type);

            var typeFqn = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                          | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

            var isParsable = IsBindableType(prop.Type);

            var underlyingSymbol = GetUnderlying(prop.Type);
            var underlyingTypeFqn = underlyingSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Register every parsable type that is NOT a compiler primitive with TypedParserRegistry so
            // a full-AOT (no MakeGenericMethod) build can bind it. SpecialType.None deliberately covers
            // more than user types — Guid, the date/time types, Int128/UInt128/Half and System.Version
            // are all non-special IParsable structs. Testing SpecialType (not the namespace) is what
            // keeps a System-namespace type like Version, which is NOT in the registry's primitive
            // seed, from silently falling through the gap. Re-registering a type the registry already
            // seeds is an idempotent no-op, and registrations are deduped by FQN at emit time.
            var needsAotRegistration = isParsable && underlyingSymbol.SpecialType == SpecialType.None;

            var loc = prop.Locations.FirstOrDefault();

            result.Add(new RoutePropInfo(
                prop.Name,
                typeFqn,
                underlyingTypeName,
                isNullable,
                hasQueryParam,
                queryParamName,
                hasRouteParam,
                routeParamName,
                isParsable,
                underlyingTypeFqn,
                needsAotRegistration,
                new LocationInfo(loc)));
        }

        return result;
    }

    private static OrphanCandidate? GetOrphanCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        var classAttrs = symbol.GetAttributes();
        if (classAttrs.Any(a => a.AttributeClass?.ToDisplayString() == SkipFactoryAttrFullName))
        {
            return null;
        }

        var inheritsComponent = InheritsFromComponent(symbol);

        // A class is a route target if it derives from Page (the current spelling) or carries [Route]
        // (the legacy one). Deriving is enough on its own — the Route override is what supplies the
        // template, and a missing/non-constant one is RASK047's business, not this analyzer's.
        var isRouteTarget = classAttrs.Any(a => a.AttributeClass?.ToDisplayString() == RouteAttrFullName);

        string? reason = null;
        if (!inheritsComponent)
        {
            reason = "class does not inherit from Component";
        }
        else if (symbol.IsAbstract)
        {
            reason = "class is abstract";
        }
        else if (!isRouteTarget)
        {
            reason = "class does not derive from Page";
        }

        if (reason is null)
        {
            return null;
        }

        var props = new List<OrphanProp>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
            {
                continue;
            }

            if (prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared)
            {
                continue;
            }

            foreach (var attr in prop.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (name != RouteParamAttrFullName && name != QueryParamAttrFullName)
                {
                    continue;
                }

                var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                          ?? prop.Locations.FirstOrDefault();
                props.Add(new OrphanProp(prop.Name, name == RouteParamAttrFullName, new LocationInfo(loc)));
            }
        }

        if (props.Count == 0)
        {
            return null;
        }

        return new OrphanCandidate(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            reason,
            new EquatableArray<OrphanProp>(props));
    }

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

    // Unwraps Nullable<T> to T (leaves every other type unchanged) — the single source of truth for
    // "what type actually gets parsed", shared by the display-name, bindability and AOT-registration
    // paths so they can never disagree about the underlying type.
    private static ITypeSymbol GetUnderlying(ITypeSymbol type)
    {
        if (type.IsValueType && type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    private static string GetUnderlyingTypeName(ITypeSymbol type) =>
        GetUnderlying(type).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

    private static bool IsBindableType(ITypeSymbol type)
    {
        var underlying = GetUnderlying(type);

        if (underlying.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        foreach (var iface in underlying.AllInterfaces)
        {
            var def = iface.OriginalDefinition;
            if (def.MetadataName != "IParsable`1")
            {
                continue;
            }

            if (def.ContainingNamespace?.ToDisplayString() != "System")
            {
                continue;
            }

            if (iface.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], underlying))
            {
                return true;
            }
        }

        return false;
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<Candidate> candidates,
        bool supportsExtensionMembers)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

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
                .Where(c => !c.IsNotFound || c.FullyQualifiedName == keepFqn)
                .ToList();
        }

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

        foreach (var entry in collisions.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (entry.Value.Count < 2)
            {
                continue;
            }

            // Report on every colliding page after the first (ordered by fully-qualified name for a
            // stable canonical page), naming this page's own template and the page it collides with.
            var ordered = entry.Value.OrderBy(x => x.Page.FullyQualifiedName, StringComparer.Ordinal).ToList();
            var firstFqn = ordered[0].Page.FullyQualifiedName;
            foreach (var dup in ordered.Skip(1))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask031, dup.Page.RouteAttrLocation.ToLocation(),
                    dup.Template, firstFqn));
            }
        }

        if (filtered.Count == 0)
        {
            return;
        }

        var byFqn = filtered
            .GroupBy(c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var byNamespace = filtered
            .GroupBy(c => c.Namespace, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in byNamespace)
        {
            // NotFound pages don't get Routes.X() factories — nobody navigates to NotFound
            // by name. Skip emitting a Routes partial entirely when the namespace has only
            // NotFound candidates.
            var routedInGroup = group.Where(c => !c.IsNotFound).ToList();
            if (routedInGroup.Count == 0)
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            // The route helpers carry a <summary> and no <param>, which is warning-clean on its own — but
            // the pragma keeps it that way if a <param> is ever added here, since CS1573 then fires for
            // every parameter left undocumented and would break every consumer's build, not ours.
            sb.AppendLine("#pragma warning disable CS1573 // parameter has no matching param tag");
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(group.Key);
            if (hasNs)
            {
                sb.Append("namespace ").Append(group.Key).AppendLine(";");
                sb.AppendLine();
            }

            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     Type-safe URLs for this assembly's <c>[Route]</c> pages — one method per page, taking that");
            sb.AppendLine("///     page's route parameters. Build links with these rather than with path strings.");
            sb.AppendLine("/// </summary>");
            sb.AppendLine("public static partial class Routes");
            sb.AppendLine("{");

            // Collected separately and appended as a second partial below, because the extension blocks
            // must not interleave with the factory methods they forward to.
            var extSb = supportsExtensionMembers ? new StringBuilder() : null;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in routedInGroup.OrderBy(c => c.TypeName, StringComparer.Ordinal))
            {
                if (!seen.Add(c.TypeName))
                {
                    continue;
                }

                EmitRouteFactory(spc, sb, extSb, c, byFqn);
                sb.AppendLine();
            }

            sb.AppendLine("}");

            if (extSb is { Length: > 0 })
            {
                sb.AppendLine();
                sb.Append(extSb);
            }

            var hint = hasNs ? $"{group.Key}.Routes.g.cs" : "Routes.g.cs";
            spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        // Deduplicate by fully-qualified type name before emitting the registry. A `partial`
        // routed page whose declarations carry attributes on more than one part (e.g. [Route] on
        // one and [Obsolete]/a source-gen attribute on another) yields one Candidate per attributed
        // declaration — all with the same FQN. Emitting them all produced duplicate
        // RouteRegistration entries (competing Route nodes for the same page) and duplicate
        // [DynamicDependency] attributes. byFqn already keeps the first Candidate per FQN (its
        // Templates reflect every [Route] on the merged symbol), so the registry uses that.
        EmitRegistryInitializer(spc, byFqn.Values.ToList());
    }

    private static void EmitRegistryInitializer(SourceProductionContext spc, IReadOnlyList<Candidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskRoutesRegistry");
        sb.AppendLine("{");
        // Per-page [DynamicDependency] tells the trimmer to keep public ctors and properties on
        // every routed page type. Pages are instantiated via ActivatorUtilities.CreateInstance
        // (needs ctors) and bound via reflection over [RouteParam]/[QueryParam] properties
        // (needs property accessors). Custom attributes on the type — [Route], [Authorize],
        // [AllowAnonymous] — are preserved by the trimmer whenever the type metadata is kept,
        // so the auth guard and template resolver work transparently.
        foreach (var c in candidates.OrderBy(x => x.FullyQualifiedName, StringComparer.Ordinal))
        {
            sb.Append("    [global::System.Diagnostics.CodeAnalysis.DynamicDependency(")
                .Append(
                    "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | " +
                    "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties")
                .Append(", typeof(")
                .Append(c.FullyQualifiedName)
                .AppendLine("))]");
        }

        // Init() only bootstraps; RefreshAll() holds the whole body so the hot-reload coordinator
        // can re-invoke it after a metadata update ([ModuleInitializer] never runs twice). It must
        // stay idempotent and replace-semantics — see RaskHotReload.RefreshTargetTypeNames, which
        // lists this class by name.
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init() => RefreshAll();");
        sb.AppendLine();
        sb.AppendLine("    internal static void RefreshAll()");
        sb.AppendLine("    {");
        // Replace, not Add: Add appends, and every assembly with routed pages calls this. Keying
        // the set on this class lets a refresh swap just this assembly's routes — picking up
        // added, edited and deleted [Route] templates — without duplicating them or dropping
        // another assembly's contribution.
        sb.AppendLine(
            "        global::Rask.Core.Routing.RouteRegistry.Replace(typeof(__RaskRoutesRegistry), new global::Rask.Core.Routing.RouteRegistration[]");
        sb.AppendLine("        {");
        foreach (var c in candidates.OrderBy(x => x.FullyQualifiedName, StringComparer.Ordinal))
        {
            // One RouteRegistration per [Route] attribute. RouteRegistry.BuildTree groups
            // by parent — duplicates of the same PageType under the same parent surface as
            // distinct Route nodes the router can match independently.
            foreach (var template in c.Templates)
            {
                sb.Append("            new(typeof(")
                    .Append(c.FullyQualifiedName)
                    .Append("), \"")
                    .Append(EscapeForCSharpStringLiteral(template))
                    .Append("\", ");
                if (c.ParentTypeFqn is null)
                {
                    sb.Append("null");
                }
                else
                {
                    sb.Append("typeof(").Append(c.ParentTypeFqn).Append(')');
                }

                sb.AppendLine("),");
            }
        }

        sb.AppendLine("        });");

        // Register every non-primitive IParsable<T> route/query param type with the reflection-free
        // parser registry so a full-AOT publish (no MakeGenericMethod) can still bind it. Compiler
        // primitives are always seeded by the framework, so they are skipped; deduped by FQN.
        var aotRegisteredTypes = candidates
            .SelectMany(c => c.Properties)
            .Where(p => (p.HasRouteParam || p.HasQueryParam) && p.NeedsAotRegistration)
            .Select(p => p.UnderlyingTypeFqn)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(fqn => fqn, StringComparer.Ordinal)
            .ToList();

        foreach (var fqn in aotRegisteredTypes)
        {
            sb.Append("        global::Rask.Core.Forms.RaskBinding.RegisterParsable<")
                .Append(fqn)
                .AppendLine(">();");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("__RaskRoutesRegistry.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitRouteFactory(SourceProductionContext spc, StringBuilder sb, StringBuilder? extSb,
        Candidate c, Dictionary<string, Candidate> byFqn)
    {
        var unbindable = false;
        foreach (var prop in c.Properties)
        {
            if (!(prop.HasRouteParam || prop.HasQueryParam))
            {
                continue;
            }

            if (prop.IsParsable)
            {
                continue;
            }

            spc.ReportDiagnostic(Diagnostic.Create(Rask011, prop.Location.ToLocation(), c.FullyQualifiedName,
                prop.Name, prop.TypeFqn));
            unbindable = true;
        }

        if (unbindable)
        {
            EmitStub(sb, c);
            return;
        }

        // Multi-route: validate EVERY declared template (so RASK004/005/006 fire on any
        // misconfigured template) and aggregate the set of matched RouteParam property names
        // across all of them. A RouteParam that appears in at least one template's segments
        // is considered bound — RASK008 only fires for properties that no template references.
        List<TemplatePart>? firstParts = null;
        List<ResolvedPathParam>? firstResolved = null;
        var matchedAcrossTemplates = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < c.Templates.Count; i++)
        {
            if (!TryResolveFullTemplate(spc, c, byFqn, i, out var fullTemplate))
            {
                EmitStub(sb, c);
                return;
            }

            if (!TryParseTemplate(fullTemplate, out var parts, out var error))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask003, c.RouteAttrLocation.ToLocation(), fullTemplate,
                    c.FullyQualifiedName, error));
                EmitStub(sb, c);
                return;
            }

            var pathParams = parts.OfType<ParamPart>().ToList();
            var resolved = new List<ResolvedPathParam>();

            foreach (var p in pathParams)
            {
                var prop = c.Properties.FirstOrDefault(x =>
                    x.HasRouteParam &&
                    string.Equals(x.RouteParamName ?? x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                if (prop.Name is null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask004, c.RouteAttrLocation.ToLocation(), p.Name,
                        c.FullyQualifiedName));
                    EmitStub(sb, c);
                    return;
                }

                if (prop.HasQueryParam)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask006, prop.Location.ToLocation(), c.FullyQualifiedName,
                        prop.Name, p.Name));
                    EmitStub(sb, c);
                    return;
                }

                if (!IsTypeCompatible(prop.UnderlyingTypeName, p.Constraint))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask005, prop.Location.ToLocation(), c.FullyQualifiedName,
                        prop.Name, prop.TypeFqn, p.Constraint ?? "(none)"));
                    EmitStub(sb, c);
                    return;
                }

                matchedAcrossTemplates.Add(prop.Name);
                resolved.Add(new ResolvedPathParam(p, prop));
            }

            if (i == 0)
            {
                firstParts = parts;
                firstResolved = resolved;
            }
        }

        // RASK008: orphan [RouteParam] = a property no template segment binds.
        foreach (var prop in c.Properties)
        {
            if (prop.HasRouteParam && !matchedAcrossTemplates.Contains(prop.Name))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask008, prop.Location.ToLocation(), c.FullyQualifiedName,
                    prop.Name, prop.RouteParamName ?? prop.Name));
                EmitStub(sb, c);
                return;
            }
        }

        var queryProps = c.Properties.Where(p => p.HasQueryParam).ToList();

        // URL formatter is built from the first template only — see TryResolveFullTemplate's
        // index-0 comment for the rationale.
        EmitFactoryBody(sb, extSb, c, firstParts!, firstResolved!, queryProps);
    }

    // The doc on a generated route helper. `Routes.UserPage(42)` is what a link is SUPPOSED to be written
    // as instead of "/users/42", and the reason is worth stating where it is read: the helper is the only
    // form in which a changed template becomes a compile error rather than a dead link found by a user.
    //
    // Summary only, deliberately — no <param>. CS1573 fires per undocumented parameter as soon as ANY is
    // documented, and these parameters come from the URL template rather than from anything the generator
    // can describe. Documenting none of them keeps a consumer's warnings-as-errors build clean.
    private static void EmitRouteDoc(StringBuilder sb, Candidate c)
    {
        var cref = c.FullyQualifiedName.Replace('<', '{').Replace('>', '}');
        sb.Append("    /// <summary>The URL of <see cref=\"").Append(cref).Append("\"/>");

        if (c.Templates.Count > 0)
        {
            sb.Append(" — <c>").Append(EscapeXml(c.Templates[0])).Append("</c>");
        }

        sb.AppendLine(".</summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    ///     Prefer this over writing the path as a string: a template that changes then breaks the");
        sb.AppendLine("    ///     build here, rather than becoming a link that 404s for whoever clicks it.");
        sb.AppendLine("    /// </remarks>");
    }

    // A route template can legally contain characters that are markup inside a doc comment.
    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void EmitStub(StringBuilder sb, Candidate c)
    {
        sb.Append("    public static ").Append(RouteUrlFullName).Append(' ').Append(c.TypeName).AppendLine("()");
        sb.AppendLine(
            "        => throw new global::System.InvalidOperationException(\"Route source generation failed; see diagnostics.\");");
    }

    private static void EmitFactoryBody(StringBuilder sb, StringBuilder? extSb, Candidate c,
        List<TemplatePart> parts, List<ResolvedPathParam> pathParams, List<RoutePropInfo> queryProps)
    {
        // Signature: required path params first (in declaration order), then optional, then query
        var orderedPath = pathParams.OrderBy(p => p.Part.Optional ? 1 : 0).ToList();

        EmitRouteDoc(sb, c);
        sb.Append("    public static ").Append(RouteUrlFullName).Append(' ').Append(c.TypeName).Append('(');
        var first = true;
        foreach (var rp in orderedPath)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            var paramType = PathParamTypeFqn(rp.Prop, rp.Part.Constraint, rp.Part.Optional);
            sb.Append(paramType).Append(' ').Append(rp.Prop.Name);
            if (rp.Part.Optional)
            {
                sb.Append(" = null");
            }
        }

        foreach (var qp in queryProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            var paramType = qp.IsNullable ? qp.TypeFqn : qp.TypeFqn + "?";
            sb.Append(paramType).Append(' ').Append(qp.Name).Append(" = null");
        }

        sb.AppendLine(")");
        sb.AppendLine("    {");

        // Build path
        sb.Append("        var __path = \"\"");
        var pendingLiteral = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part is LiteralPart lp)
            {
                pendingLiteral.Append('/').Append(lp.Value);
            }
            else if (part is ParamPart pp)
            {
                var rp = pathParams.First(x => x.Part == pp);
                if (pp.Optional)
                {
                    if (pendingLiteral.Length > 0)
                    {
                        sb.Append(" + \"").Append(EscapeForCSharpStringLiteral(pendingLiteral.ToString())).Append('"');
                        pendingLiteral.Clear();
                    }

                    var ident = rp.Prop.Name;
                    sb.Append(" + (").Append(ident).Append(" is null ? \"\" : \"/\" + ").Append(FormatExpr(ident))
                        .Append(')');
                }
                else
                {
                    pendingLiteral.Append('/');
                    if (pendingLiteral.Length > 0)
                    {
                        sb.Append(" + \"").Append(EscapeForCSharpStringLiteral(pendingLiteral.ToString())).Append('"');
                        pendingLiteral.Clear();
                    }

                    var ident = rp.Prop.Name;
                    sb.Append(" + ").Append(FormatExpr(ident));
                }
            }
        }

        if (pendingLiteral.Length > 0)
        {
            sb.Append(" + \"").Append(EscapeForCSharpStringLiteral(pendingLiteral.ToString())).Append('"');
        }

        sb.AppendLine(";");

        // Empty path → root
        sb.AppendLine("        if (__path.Length == 0) __path = \"/\";");

        // Build query string
        if (queryProps.Count > 0)
        {
            sb.AppendLine("        global::System.Text.StringBuilder? __qs = null;");
            foreach (var qp in queryProps)
            {
                var ident = qp.Name;
                var qpName = qp.QueryParamName ?? qp.Name;
                // URL-encode the query KEY at generation time (the value is encoded at runtime via
                // EncodeExpr). The key is a compile-time constant, so baking the encoded form costs
                // nothing and keeps an explicit [QueryParam("a b")] / a name with '&'/'=' from
                // emitting a malformed query string. Property-name-derived keys are valid
                // identifiers, so this is a no-op for them.
                var encodedKey = Uri.EscapeDataString(qpName);
                if (qp.IsNullable)
                {
                    sb.Append("        if (").Append(ident).AppendLine(" is not null)");
                    sb.AppendLine("        {");
                    sb.AppendLine("            __qs ??= new global::System.Text.StringBuilder();");
                    sb.AppendLine("            __qs.Append(__qs.Length == 0 ? '?' : '&');");
                    sb.Append("            __qs.Append(\"").Append(EscapeForCSharpStringLiteral(encodedKey))
                        .AppendLine("=\");");
                    sb.Append("            __qs.Append(").Append(EncodeExpr(ident)).AppendLine(");");
                    sb.AppendLine("        }");
                }
                else
                {
                    // Non-nullable required query param — always emit
                    sb.AppendLine("        __qs ??= new global::System.Text.StringBuilder();");
                    sb.AppendLine("        __qs.Append(__qs.Length == 0 ? '?' : '&');");
                    sb.Append("        __qs.Append(\"").Append(EscapeForCSharpStringLiteral(encodedKey))
                        .AppendLine("=\");");
                    sb.Append("        __qs.Append(").Append(EncodeExpr(ident)).AppendLine(");");
                }
            }

            sb.Append("        return new ").Append(RouteUrlFullName).Append("(__path, __qs?.ToString(), typeof(")
                .Append(c.FullyQualifiedName).AppendLine("));");
        }
        else
        {
            sb.Append("        return new ").Append(RouteUrlFullName).Append("(__path, null, typeof(")
                .Append(c.FullyQualifiedName).AppendLine("));");
        }

        sb.AppendLine("    }");

        if (extSb is not null)
        {
            EmitNavigationExtension(extSb, c, orderedPath, queryProps);
        }
    }

    /// <summary>
    ///     Emits the per-page <c>SomePage.Url(...)</c> / <c>SomePage.Go(...)</c> pair as a C# 14 static
    ///     extension block. Both mirror the legacy <c>Routes.SomePage(...)</c> signature exactly — the same
    ///     path params (required before optional) then the query params — and <c>Url</c> forwards to it, so
    ///     the URL-building logic has exactly one implementation.
    ///     <para>
    ///         The block is emitted into the page's OWN namespace. Extension members resolve only when their
    ///         containing namespace is imported (a fully-qualified <c>My.Ns.SomePage.Go()</c> with no
    ///         <c>using</c> does not compile), so co-locating them means the import that brings the page type
    ///         into scope also brings its navigation helpers.
    ///     </para>
    ///     <para>
    ///         Each page gets its OWN container class. A static extension member lowers to a plain static
    ///         method on the containing class with no receiver parameter, so two pages that both take no
    ///         route parameters would lower to two identical <c>Url()</c> signatures and collide with CS0111
    ///         if they shared one container.
    ///     </para>
    /// </summary>
    private static void EmitNavigationExtension(StringBuilder sb, Candidate c,
        List<ResolvedPathParam> orderedPath, List<RoutePropInfo> queryProps)
    {
        // A route or query param literally named "replace" would collide with Go's history flag. The
        // page's own parameter wins and Go simply loses the flag for that page — a shadowed, silently
        // mis-bound argument is far worse than a missing convenience.
        var replaceFree = orderedPath.All(p => !string.Equals(p.Prop.Name, "replace", StringComparison.OrdinalIgnoreCase))
                          && queryProps.All(p => !string.Equals(p.Name, "replace", StringComparison.OrdinalIgnoreCase));

        // Must not be more visible than the page itself: the receiver type is part of a static extension
        // member's signature, so a public container over an internal page is CS0051.
        sb.Append(c.IsPubliclyVisible ? "public" : "internal").Append(" static class __RaskNav_")
            .AppendLine(c.TypeName);
        sb.AppendLine("{");
        sb.Append("    extension(").Append(c.FullyQualifiedName).AppendLine(")");
        sb.AppendLine("    {");

        sb.Append("        public static ").Append(RouteUrlFullName).Append(" Url(");
        AppendSignature(sb, orderedPath, queryProps);
        sb.Append(')').AppendLine();
        sb.Append("            => Routes.").Append(c.TypeName).Append('(');
        AppendArguments(sb, orderedPath, queryProps);
        sb.AppendLine(");");
        sb.AppendLine();

        sb.Append("        public static void Go(");
        AppendSignature(sb, orderedPath, queryProps);
        if (replaceFree)
        {
            if (orderedPath.Count > 0 || queryProps.Count > 0)
            {
                sb.Append(", ");
            }

            sb.Append("bool replace = false");
        }

        sb.Append(')').AppendLine();
        sb.Append("            => global::Rask.Core.Routing.Navigator.RequireCurrent().NavigateTo(Routes.")
            .Append(c.TypeName).Append('(');
        AppendArguments(sb, orderedPath, queryProps);
        sb.Append(')').Append(replaceFree ? ", replace" : string.Empty).AppendLine(");");

        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static void AppendSignature(StringBuilder sb, List<ResolvedPathParam> orderedPath,
        List<RoutePropInfo> queryProps)
    {
        var first = true;
        foreach (var rp in orderedPath)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(PathParamTypeFqn(rp.Prop, rp.Part.Constraint, rp.Part.Optional)).Append(' ')
                .Append(rp.Prop.Name);
            if (rp.Part.Optional)
            {
                sb.Append(" = null");
            }
        }

        foreach (var qp in queryProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(qp.IsNullable ? qp.TypeFqn : qp.TypeFqn + "?").Append(' ').Append(qp.Name).Append(" = null");
        }
    }

    private static void AppendArguments(StringBuilder sb, List<ResolvedPathParam> orderedPath,
        List<RoutePropInfo> queryProps)
    {
        var first = true;
        foreach (var rp in orderedPath)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(rp.Prop.Name);
        }

        foreach (var qp in queryProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(qp.Name);
        }
    }

    private static string FormatExpr(string ident)
        => FormatterFullName + ".Format(" + ident + ")";

    private static string EncodeExpr(string ident)
        => FormatterFullName + ".Format(" + ident + ")";

    private static string PathParamTypeFqn(RoutePropInfo prop, string? constraint, bool optional)
    {
        if (constraint is null)
        {
            if (!optional)
            {
                return prop.TypeFqn;
            }

            return prop.IsNullable ? prop.TypeFqn : prop.TypeFqn + "?";
        }

        switch (constraint)
        {
            case "int": return optional ? "int?" : "int";
            case "long": return optional ? "long?" : "long";
            case "bool": return optional ? "bool?" : "bool";
            case "guid": return optional ? "global::System.Guid?" : "global::System.Guid";
            default: return optional ? "string?" : "string";
        }
    }

    private static bool IsTypeCompatible(string underlyingTypeName, string? constraint)
    {
        if (constraint is null)
        {
            return true;
        }

        switch (constraint)
        {
            case "int": return underlyingTypeName == "int";
            case "long": return underlyingTypeName == "long";
            case "bool": return underlyingTypeName == "bool";
            case "guid": return underlyingTypeName == "global::System.Guid";
            default: return underlyingTypeName == "string";
        }
    }

    private static bool TryResolveFullTemplate(SourceProductionContext spc, Candidate c,
        Dictionary<string, Candidate> byFqn, int leafTemplateIndex, out string fullTemplate)
    {
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = c;
        var isLeaf = true;
        while (current is not null)
        {
            if (!seen.Add(current.FullyQualifiedName))
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(Rask007, c.RouteAttrLocation.ToLocation(), c.FullyQualifiedName));
                fullTemplate = string.Empty;
                return false;
            }

            // The leaf composes with whichever of its own templates we're resolving (each
            // call to this helper resolves ONE concrete URL). Parents always contribute
            // their first declared [Route] template — multi-route parents would otherwise
            // explode the chain combinatorially, and the URL formatter has no way to pick.
            var localTemplate = isLeaf ? current.Templates[leafTemplateIndex] : current.Templates[0];
            parts.Insert(0, localTemplate);
            isLeaf = false;
            if (current.ParentTypeFqn is null)
            {
                break;
            }

            byFqn.TryGetValue(current.ParentTypeFqn, out var parent);
            current = parent;
        }

        fullTemplate = JoinTemplates(parts);
        return true;
    }

    private static string JoinTemplates(List<string> parts)
    {
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (string.IsNullOrEmpty(p))
            {
                continue;
            }

            var trimmed = p;
            if (sb.Length > 0)
            {
                if (trimmed.StartsWith("/", StringComparison.Ordinal))
                {
                    trimmed = trimmed.Substring(1);
                }

                if (sb[sb.Length - 1] != '/' && trimmed.Length > 0)
                {
                    sb.Append('/');
                }
            }

            sb.Append(trimmed);
        }

        if (sb.Length == 0)
        {
            return "/";
        }

        if (sb[0] != '/')
        {
            sb.Insert(0, '/');
        }

        return sb.ToString();
    }

    private static bool TryParseTemplate(string template, out List<TemplatePart> parts, out string error)
    {
        parts = new List<TemplatePart>();
        error = string.Empty;
        if (string.IsNullOrEmpty(template))
        {
            return true;
        }

        var path = template.StartsWith("/", StringComparison.Ordinal) ? template.Substring(1) : template;
        if (path.Length == 0)
        {
            return true;
        }

        var segs = path.Split('/');
        foreach (var seg in segs)
        {
            if (seg.Length == 0)
            {
                error = "empty segment — remove the doubled '/', e.g. \"/users/{id:int}\"";
                return false;
            }

            if (seg[0] == '{' && seg[seg.Length - 1] == '}')
            {
                var inner = seg.Substring(1, seg.Length - 2);
                if (inner.StartsWith("**", StringComparison.Ordinal))
                {
                    error = "catch-all '{**...}' segments are not supported in typed routes — name the segments you need, e.g. \"/files/{folder}/{name}\", or match the rest inside the page";
                    return false;
                }

                var optional = false;
                if (inner.EndsWith("?", StringComparison.Ordinal))
                {
                    optional = true;
                    inner = inner.Substring(0, inner.Length - 1);
                }

                string name;
                string? constraint = null;
                var colon = inner.IndexOf(':');
                if (colon >= 0)
                {
                    name = inner.Substring(0, colon);
                    constraint = inner.Substring(colon + 1).ToLowerInvariant();
                }
                else
                {
                    name = inner;
                }

                if (name.Length == 0)
                {
                    error = "param has no name — name it, e.g. \"/users/{id}\" or \"/users/{id:guid?}\"";
                    return false;
                }

                parts.Add(new ParamPart(name, constraint, optional));
            }
            else if (seg.IndexOf('{') >= 0 || seg.IndexOf('}') >= 0)
            {
                error = "mixed literal/param segments are not supported — give the parameter its own segment, e.g. \"/order/{id}\" rather than \"/order-{id}\"";
                return false;
            }
            else
            {
                parts.Add(new LiteralPart(seg));
            }
        }

        return true;
    }

    private static string EscapeForCSharpStringLiteral(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }

    private abstract record TemplatePart;

    private sealed record LiteralPart(string Value) : TemplatePart;

    private sealed record ParamPart(string Name, string? Constraint, bool Optional) : TemplatePart;

    private sealed record ResolvedPathParam(ParamPart Part, RoutePropInfo Prop);

    private sealed record Candidate(
        string Namespace,
        string TypeName,
        string FullyQualifiedName,
        EquatableArray<string> Templates,
        string? ParentTypeFqn,
        EquatableArray<RoutePropInfo> Properties,
        LocationInfo RouteAttrLocation,
        bool IsNotFound,
        bool HasRouteAttr,
        bool IsPubliclyVisible = true);

    private readonly record struct RoutePropInfo(
        string Name,
        string TypeFqn,
        string UnderlyingTypeName,
        bool IsNullable,
        bool HasQueryParam,
        string? QueryParamName,
        bool HasRouteParam,
        string? RouteParamName,
        bool IsParsable,
        string UnderlyingTypeFqn,
        bool NeedsAotRegistration,
        LocationInfo Location);

    private sealed record OrphanCandidate(
        string ClassFqn,
        string Reason,
        EquatableArray<OrphanProp> Props);

    private readonly record struct OrphanProp(
        string Name,
        bool IsRouteParam,
        LocationInfo Location);

    internal readonly record struct LocationInfo
    {
        private readonly string? _filePath;
        private readonly int _length;
        private readonly int _start;

        public LocationInfo(Location? loc)
        {
            if (loc is null || loc == Location.None || loc.SourceTree is null)
            {
                _filePath = null;
                _start = 0;
                _length = 0;
                return;
            }

            _filePath = loc.SourceTree.FilePath;
            _start = loc.SourceSpan.Start;
            _length = loc.SourceSpan.Length;
        }

        public Location ToLocation()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                return Location.None;
            }

            return Location.Create(
                _filePath!,
                new TextSpan(_start, _length),
                new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));
        }
    }
}

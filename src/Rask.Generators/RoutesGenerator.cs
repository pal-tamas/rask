using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class RoutesGenerator : IIncrementalGenerator
{
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

        // The ONE Routes class lives in the project's root namespace, so code anywhere under it reaches
        // every page with no using. The assembly name stands in when RootNamespace is not set.
        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? string.Empty))
            .Select(static (pair, _) =>
                pair.Left.GlobalOptions.TryGetValue("build_property.RootNamespace", out var root)
                && !string.IsNullOrWhiteSpace(root)
                    ? root.Trim()
                    : SanitizeNamespace(pair.Right));

        var grouped = candidates.Collect().Combine(supportsExtensionMembers).Combine(rootNamespace);
        context.RegisterSourceOutput(grouped,
            static (spc, pair) => Emit(spc, pair.Left.Left, pair.Left.Right, pair.Right));

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

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol
            || symbol.IsAbstract
            || symbol.IsGenericType
            || !InheritsFromComponent(symbol))
        {
            return null;
        }

        var (templates, firstRouteAttrLocation, hasNotFound, notFoundAttrLocation, parentTypeFqn) =
            ReadRouteAttributes(symbol);

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        var properties = GetPageProperties(symbol, ctx.SemanticModel.Compilation);

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
            IsPubliclyVisible: IsExternallyVisible(symbol));
    }

    // What a page's [Route], [NotFound] and [ParentRoute] attributes say about it.
    private static (List<string> Templates, Location? FirstRoute, bool HasNotFound, Location? NotFound, string? ParentFqn)
        ReadRouteAttributes(INamedTypeSymbol symbol)
    {
        var templates = new List<string>();
        Location? firstRouteAttrLocation = null;
        Location? notFoundAttrLocation = null;
        string? parentTypeFqn = null;
        var hasNotFound = false;

        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (string.Equals(name, RouteAttrFullName, StringComparison.Ordinal))
            {
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string t)
                {
                    templates.Add(t);
                }

                firstRouteAttrLocation ??= attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            }
            else if (string.Equals(name, NotFoundAttrFullName, StringComparison.Ordinal))
            {
                hasNotFound = true;
                notFoundAttrLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            }
            else if (string.Equals(name, ParentRouteAttrFullName, StringComparison.Ordinal)
                     && attr.ConstructorArguments.Length > 0
                     && attr.ConstructorArguments[0].Value is INamedTypeSymbol p)
            {
                parentTypeFqn = p.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return (templates, firstRouteAttrLocation, hasNotFound, notFoundAttrLocation, parentTypeFqn);
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

    private static List<RoutePropInfo> GetPageProperties(INamedTypeSymbol symbol, Compilation compilation)
    {
        var result = new List<RoutePropInfo>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop
                || prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared
                || prop.DeclaredAccessibility != Accessibility.Public
                || prop.SetMethod is not { DeclaredAccessibility: Accessibility.Public })
            {
                continue;
            }

            result.Add(ToRouteProp(prop, compilation));
        }

        return result;
    }

    private static RoutePropInfo ToRouteProp(IPropertySymbol prop, Compilation compilation)
    {
        var (hasQueryParam, queryParamName, hasRouteParam, routeParamName) = ReadParamAttributes(prop);

        var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                         || (prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType ==
                             SpecialType.System_Nullable_T);

        var underlyingTypeName = GetUnderlyingTypeName(prop.Type);

        // Through GeneratedModelShape: a Rask.Data entity's generated model is an unresolved error type to
        // this generator, and its bare display name would not bind from the generated routes file.
        var typeFqn = Shared.GeneratedModelShape.DisplayName(
            prop.Type,
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                          | SymbolDisplayMiscellaneousOptions.UseSpecialTypes),
            compilation);

        var isParsable = IsBindableType(prop.Type);

        var underlyingSymbol = GetUnderlying(prop.Type);
        var underlyingTypeFqn = Shared.GeneratedModelShape.DisplayName(
            underlyingSymbol, SymbolDisplayFormat.FullyQualifiedFormat, compilation);

        // Register every parsable type that is NOT a compiler primitive with TypedParserRegistry so
        // a full-AOT (no MakeGenericMethod) build can bind it. SpecialType.None deliberately covers
        // more than user types — Guid, the date/time types, Int128/UInt128/Half and System.Version
        // are all non-special IParsable structs. Testing SpecialType (not the namespace) is what
        // keeps a System-namespace type like Version, which is NOT in the registry's primitive
        // seed, from silently falling through the gap. Re-registering a type the registry already
        // seeds is an idempotent no-op, and registrations are deduped by FQN at emit time.
        var needsAotRegistration = isParsable && underlyingSymbol.SpecialType == SpecialType.None;

        var loc = prop.Locations.FirstOrDefault();

        return new RoutePropInfo(
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
            new LocationInfo(loc));
    }

    // A property's [QueryParam] / [RouteParam], each with the explicit name it gives, if any.
    private static (bool HasQuery, string? QueryName, bool HasRoute, string? RouteName) ReadParamAttributes(
        IPropertySymbol prop)
    {
        string? queryParamName = null;
        var hasQueryParam = false;
        string? routeParamName = null;
        var hasRouteParam = false;
        foreach (var attr in prop.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (string.Equals(attrName, QueryParamAttrFullName, StringComparison.Ordinal))
            {
                hasQueryParam = true;
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string qn)
                {
                    queryParamName = qn;
                }
            }
            else if (string.Equals(attrName, RouteParamAttrFullName, StringComparison.Ordinal))
            {
                hasRouteParam = true;
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string rn)
                {
                    routeParamName = rn;
                }
            }
        }

        return (hasQueryParam, queryParamName, hasRouteParam, routeParamName);
    }

    private static OrphanCandidate? GetOrphanCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl
            || ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        var classAttrs = symbol.GetAttributes();
        if (classAttrs.Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), SkipFactoryAttrFullName, StringComparison.Ordinal)))
        {
            return null;
        }

        var inheritsComponent = InheritsFromComponent(symbol);

        // A class is a route target if it carries [Route].
        var isRouteTarget = classAttrs.Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), RouteAttrFullName, StringComparison.Ordinal));

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
            reason = "class has no [Route]";
        }

        if (reason is null)
        {
            return null;
        }

        var props = OrphanProps(symbol);
        if (props.Count == 0)
        {
            return null;
        }

        return new OrphanCandidate(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            reason,
            new EquatableArray<OrphanProp>(props));
    }

    // Every [RouteParam] / [QueryParam] on a class that cannot bind them.
    private static List<OrphanProp> OrphanProps(INamedTypeSymbol symbol)
    {
        var props = new List<OrphanProp>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol prop || prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared)
            {
                continue;
            }

            foreach (var attr in prop.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (!string.Equals(name, RouteParamAttrFullName, StringComparison.Ordinal) && !string.Equals(name, QueryParamAttrFullName, StringComparison.Ordinal))
                {
                    continue;
                }

                var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                          ?? prop.Locations.FirstOrDefault();
                props.Add(new OrphanProp(prop.Name, string.Equals(name, RouteParamAttrFullName, StringComparison.Ordinal), new LocationInfo(loc)));
            }
        }

        return props;
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
            if (!string.Equals(def.MetadataName, "IParsable`1", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(def.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal))
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
        bool supportsExtensionMembers, string rootNamespace)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var filtered = DropAmbiguous(spc, candidates);

        filtered = DropDuplicateNotFound(spc, filtered);

        ReportRouteCollisions(spc, filtered);

        if (filtered.Count == 0)
        {
            return;
        }

        var byFqn = filtered
            .GroupBy(c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // NotFound pages don't get Routes.X() factories — nobody navigates to NotFound by name.
        var routed = byFqn.Values.Where(c => !c.IsNotFound).ToList();
        if (routed.Count > 0)
        {
            EmitRoutesClass(spc, routed, byFqn, supportsExtensionMembers, rootNamespace);
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

    /// <summary>
    ///     Emits the project's ONE <c>Routes</c> class, in its root namespace. A page whose type name is
    ///     unique is a flat <c>Routes.{Page}(...)</c>; pages sharing a type name nest under the namespace
    ///     segments that follow the group's common prefix (<c>Routes.Admin.HomePage()</c>).
    /// </summary>
    private static void EmitRoutesClass(SourceProductionContext spc, List<Candidate> routed,
        Dictionary<string, Candidate> byFqn, bool supportsExtensionMembers, string rootNamespace)
    {
        var root = new RoutesNode("Routes");
        foreach (var group in routed.GroupBy(c => c.TypeName, StringComparer.Ordinal))
        {
            var pages = group.OrderBy(c => c.FullyQualifiedName, StringComparer.Ordinal).ToList();
            if (pages.Count == 1)
            {
                root.Pages.Add(pages[0]);
                continue;
            }

            var segments = pages.Select(c => SplitNamespace(c.Namespace)).ToList();
            var common = CommonPrefixLength(segments);
            for (var i = 0; i < pages.Count; i++)
            {
                var node = root;
                foreach (var segment in segments[i].Skip(common))
                {
                    node = node.Child(segment);
                }

                // Two pages with one namespace and one type name are nested types of different classes;
                // the first keeps the helper, as it always has.
                if (node.Pages.All(p => !string.Equals(p.TypeName, pages[i].TypeName, StringComparison.Ordinal)))
                {
                    node.Pages.Add(pages[i]);
                }
            }
        }

        var routesFqn = string.IsNullOrEmpty(rootNamespace) ? "global::Routes" : $"global::{rootNamespace}.Routes";
        RejectCollisions(spc, root, routesFqn);

        var body = new StringBuilder();
        var extensions = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
        EmitRoutesNode(spc, body, root, routesFqn, byFqn, supportsExtensionMembers ? extensions : null, isRoot: true);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        // The route helpers carry a <summary> and no <param>, which is warning-clean on its own — but
        // the pragma keeps it that way if a <param> is ever added here, since CS1573 then fires for
        // every parameter left undocumented and would break every consumer's build, not ours.
        sb.AppendLine("#pragma warning disable CS1573 // parameter has no matching param tag");
        sb.AppendLine();
        AppendInNamespace(sb, rootNamespace, body.ToString());

        // The per-page Url()/Go() blocks live in each PAGE's namespace, so the import that brings the page
        // into scope brings its helpers too; they forward to the fully qualified Routes method.
        foreach (var entry in extensions)
        {
            sb.AppendLine();
            AppendInNamespace(sb, entry.Key, entry.Value.ToString());
        }

        spc.AddSource("Routes.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitRoutesNode(SourceProductionContext spc, StringBuilder sb, RoutesNode node,
        string fqn, Dictionary<string, Candidate> byFqn, SortedDictionary<string, StringBuilder>? extensions,
        bool isRoot)
    {
        if (isRoot)
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("///     Type-safe URLs for this assembly's <c>[Route]</c> pages — one method per page, taking that");
            sb.AppendLine("///     page's route parameters. Build links with these rather than with path strings.");
            sb.AppendLine("/// </summary>");
        }
        else
        {
            sb.Append("/// <summary>The URLs of the <c>").Append(EscapeXml(node.Name))
                .AppendLine("</c> pages whose type name another page shares.</summary>");
        }

        sb.Append("public static partial class ").AppendLine(node.Name);
        sb.AppendLine("{");

        var members = new StringBuilder();
        foreach (var c in node.Pages.OrderBy(c => c.TypeName, StringComparer.Ordinal))
        {
            StringBuilder? ext = null;
            if (extensions is not null)
            {
                if (!extensions.TryGetValue(c.Namespace, out ext))
                {
                    ext = new StringBuilder();
                    extensions[c.Namespace] = ext;
                }
                else
                {
                    ext.AppendLine();
                }
            }

            EmitRouteFactory(spc, members, ext, c, byFqn, $"{fqn}.{c.TypeName}");
            members.AppendLine();
        }

        foreach (var child in node.Children.Values)
        {
            var nested = new StringBuilder();
            EmitRoutesNode(spc, nested, child, $"{fqn}.{child.Name}", byFqn, extensions, isRoot: false);
            AppendIndented(members, nested.ToString(), "    ");
            members.AppendLine();
        }

        sb.Append(members);
        sb.AppendLine("}");
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

    private static string[] SplitNamespace(string ns) =>
        string.IsNullOrEmpty(ns) ? [] : ns.Split('.');

    private static int CommonPrefixLength(List<string[]> segments)
    {
        var length = segments.Min(s => s.Length);
        for (var i = 0; i < length; i++)
        {
            var segment = segments[0][i];
            if (segments.Any(s => !string.Equals(s[i], segment, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return length;
    }

    private static void AppendInNamespace(StringBuilder sb, string ns, string body)
    {
        if (string.IsNullOrEmpty(ns))
        {
            sb.Append(body);
            return;
        }

        sb.Append("namespace ").AppendLine(ns);
        sb.AppendLine("{");
        AppendIndented(sb, body, "    ");
        sb.AppendLine("}");
    }

    private static void AppendIndented(StringBuilder sb, string text, string indent)
    {
        foreach (var line in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            if (line.Length > 0)
            {
                sb.Append(indent);
            }

            sb.AppendLine(line);
        }
    }

    // A project's assembly name can carry characters a namespace cannot (My-App); MSBuild's own default
    // RootNamespace maps them to '_', and the fallback does the same.
    private static string SanitizeNamespace(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var segments = name.Split('.').Select(segment =>
        {
            var chars = segment.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
            var id = new string(chars);
            return id.Length == 0 || char.IsDigit(id[0]) ? "_" + id : id;
        });
        return string.Join(".", segments);
    }

    private sealed class RoutesNode(string name)
    {
        public string Name { get; } =
            SyntaxFacts.GetKeywordKind(name.TrimStart('@')) != SyntaxKind.None ? "@" + name.TrimStart('@') : name;

        public List<Candidate> Pages { get; } = [];

        public SortedDictionary<string, RoutesNode> Children { get; } = new(StringComparer.Ordinal);

        public RoutesNode Child(string segment)
        {
            if (!Children.TryGetValue(segment, out var child))
            {
                child = new RoutesNode(segment);
                Children[segment] = child;
            }

            return child;
        }

        public IEnumerable<Candidate> AllPages() =>
            Pages.Concat(Children.Values.SelectMany(c => c.AllPages()));
    }

    private static void EmitRegistryInitializer(SourceProductionContext spc, IReadOnlyList<Candidate> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskRoutesRegistry");
        sb.AppendLine("{");
        AppendDynamicDependencies(sb, candidates);

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
        AppendRegistrations(sb, candidates);

        sb.AppendLine("        });");

        AppendParsableRegistrations(sb, candidates);

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("__RaskRoutesRegistry.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void AppendDynamicDependencies(StringBuilder sb, IReadOnlyList<Candidate> candidates)
    {
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
    }

    private static void AppendRegistrations(StringBuilder sb, IReadOnlyList<Candidate> candidates)
    {
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
    }

    private static void AppendParsableRegistrations(StringBuilder sb, IReadOnlyList<Candidate> candidates)
    {
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
    }

    private static void EmitRouteFactory(SourceProductionContext spc, StringBuilder sb, StringBuilder? extSb,
        Candidate c, Dictionary<string, Candidate> byFqn, string helperFqn)
    {
        if (ReportUnbindable(spc, c))
        {
            EmitStub(sb, c);
            return;
        }

        // Multi-route: validate EVERY declared template (so RASK004/005/006 fire on any
        // misconfigured template) and aggregate the set of matched RouteParam property names
        // across all of them. A RouteParam that appears in at least one template's segments
        // is considered bound — RASK008 only fires for properties that no template references.
        List<ITemplatePart>? firstParts = null;
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

            if (ResolvePathParams(spc, c, parts, matchedAcrossTemplates) is not { } resolved)
            {
                EmitStub(sb, c);
                return;
            }

            if (i == 0)
            {
                firstParts = parts;
                firstResolved = resolved;
            }
        }

        // RASK008: orphan [RouteParam] = a property no template segment binds.
        var orphan = c.Properties.FirstOrDefault(p => p.HasRouteParam && !matchedAcrossTemplates.Contains(p.Name));
        if (orphan.Name is not null)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask008, orphan.Location.ToLocation(), c.FullyQualifiedName,
                orphan.Name, orphan.RouteParamName ?? orphan.Name));
            EmitStub(sb, c);
            return;
        }

        var queryProps = c.Properties.Where(p => p.HasQueryParam).ToList();

        // URL formatter is built from the first template only — see TryResolveFullTemplate's
        // index-0 comment for the rationale.
        EmitFactoryBody(sb, extSb, c, firstParts!, firstResolved!, queryProps, helperFqn);
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

    // Binds each path parameter of one template to its [RouteParam] property, or reports why one
    // cannot be bound (RASK004/005/006) and returns null.
    private static List<ResolvedPathParam>? ResolvePathParams(
        SourceProductionContext spc, Candidate c, List<ITemplatePart> parts, HashSet<string> matchedAcrossTemplates)
    {
        var resolved = new List<ResolvedPathParam>();
        foreach (var p in parts.OfType<ParamPart>())
        {
            var prop = c.Properties.FirstOrDefault(x =>
                x.HasRouteParam &&
                string.Equals(x.RouteParamName ?? x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            if (prop.Name is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask004, c.RouteAttrLocation.ToLocation(), p.Name,
                    c.FullyQualifiedName));
                return null;
            }

            if (prop.HasQueryParam)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask006, prop.Location.ToLocation(), c.FullyQualifiedName,
                    prop.Name, p.Name));
                return null;
            }

            if (!IsTypeCompatible(prop.UnderlyingTypeName, p.Constraint))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask005, prop.Location.ToLocation(), c.FullyQualifiedName,
                    prop.Name, prop.TypeFqn, p.Constraint ?? "(none)"));
                return null;
            }

            matchedAcrossTemplates.Add(prop.Name);
            resolved.Add(new ResolvedPathParam(p, prop));
        }

        return resolved;
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
        List<ITemplatePart> parts, List<ResolvedPathParam> pathParams, List<RoutePropInfo> queryProps,
        string helperFqn)
    {
        // Signature: required path params first (in declaration order), then optional, then query
        var orderedPath = pathParams.OrderBy(p => p.Part.Optional ? 1 : 0).ToList();

        EmitRouteDoc(sb, c);
        sb.Append("    public static ").Append(RouteUrlFullName).Append(' ').Append(c.TypeName).Append('(');
        AppendFactoryParameters(sb, orderedPath, queryProps);

        sb.AppendLine(")");
        sb.AppendLine("    {");

        // Build path
        AppendPathExpression(sb, parts, pathParams);

        // Empty path → root
        sb.AppendLine("        if (__path.Length == 0) __path = \"/\";");

        // Build query string
        if (queryProps.Count > 0)
        {
            sb.AppendLine("        global::System.Text.StringBuilder? __qs = null;");
            AppendQueryString(sb, queryProps);

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
            EmitNavigationExtension(extSb, c, orderedPath, queryProps, helperFqn);
        }
    }

    private static void AppendFactoryParameters(
        StringBuilder sb, List<ResolvedPathParam> orderedPath, List<RoutePropInfo> queryProps)
    {
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
    }

    private static void AppendPathExpression(StringBuilder sb, List<ITemplatePart> parts, List<ResolvedPathParam> pathParams)
    {
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
    }

    private static void AppendQueryString(StringBuilder sb, List<RoutePropInfo> queryProps)
    {
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
        List<ResolvedPathParam> orderedPath, List<RoutePropInfo> queryProps, string helperFqn)
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
        sb.Append("            => ").Append(helperFqn).Append('(');
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
        sb.Append("            => global::Rask.Core.Routing.Navigator.RequireCurrent().NavigateTo(")
            .Append(helperFqn).Append('(');
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
            case "int": return string.Equals(underlyingTypeName, "int", StringComparison.Ordinal);
            case "long": return string.Equals(underlyingTypeName, "long", StringComparison.Ordinal);
            case "bool": return string.Equals(underlyingTypeName, "bool", StringComparison.Ordinal);
            case "guid": return string.Equals(underlyingTypeName, "global::System.Guid", StringComparison.Ordinal);
            default: return string.Equals(underlyingTypeName, "string", StringComparison.Ordinal);
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

    private static bool TryParseTemplate(string template, out List<ITemplatePart> parts, out string error)
    {
        parts = new List<ITemplatePart>();
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
                if (ParseParam(seg.Substring(1, seg.Length - 2), out error) is not { } param)
                {
                    return false;
                }

                parts.Add(param);
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

    // The inside of one `{…}` segment: name, optional `:constraint`, optional trailing `?`.
    private static ParamPart? ParseParam(string inner, out string error)
    {
        error = string.Empty;
        if (inner.StartsWith("**", StringComparison.Ordinal))
        {
            error = "catch-all '{**...}' segments are not supported in typed routes — name the segments you need, e.g. \"/files/{folder}/{name}\", or match the rest inside the page";
            return null;
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
            return null;
        }

        return new ParamPart(name, constraint, optional);
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

    private interface ITemplatePart;

    private sealed record LiteralPart(string Value) : ITemplatePart;

    private sealed record ParamPart(string Name, string? Constraint, bool Optional) : ITemplatePart;

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

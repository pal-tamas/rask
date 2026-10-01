using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators;

public sealed partial class RoutesGenerator
{
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
}

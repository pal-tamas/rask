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

    private const string FormatterFullName = "global::Rask.Core.Routing.RouteValueFormatter";

    private static readonly DiagnosticDescriptor Rask003 = new(
        "RASK003",
        "Malformed route template",
        "Route template '{0}' on '{1}' is malformed: {2}",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK003"));

    private static readonly DiagnosticDescriptor Rask004 = new(
        "RASK004",
        "Route segment has no matching property",
        "Route segment '{{{0}}}' on '{1}' has no matching public settable property",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK004"));

    private static readonly DiagnosticDescriptor Rask005 = new(
        "RASK005",
        "Property type does not match route constraint",
        "Property '{0}.{1}' has type '{2}', incompatible with route constraint '{3}'",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK005"));

    private static readonly DiagnosticDescriptor Rask006 = new(
        "RASK006",
        "[QueryParam] applied to a path-segment property",
        "Property '{0}.{1}' has [QueryParam] but is also bound by path segment '{{{2}}}'",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK006"));

    private static readonly DiagnosticDescriptor Rask007 = new(
        "RASK007",
        "[ParentRoute] cycle",
        "[ParentRoute] forms a cycle starting at '{0}'",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK007"));

    private static readonly DiagnosticDescriptor Rask008 = new(
        "RASK008",
        "[RouteParam] without matching path segment",
        "Property '{0}.{1}' has [RouteParam] but no path segment matches '{2}'",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK008"));

    private static readonly DiagnosticDescriptor Rask009 = new(
        "RASK009",
        "[RouteParam] on a non-routed class",
        "Property '{0}.{1}' has [RouteParam] but '{0}' is not a valid route target ({2})",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK009"));

    private static readonly DiagnosticDescriptor Rask010 = new(
        "RASK010",
        "[QueryParam] on a non-routed class",
        "Property '{0}.{1}' has [QueryParam] but '{0}' is not a valid route target ({2})",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK010"));

    private static readonly DiagnosticDescriptor Rask011 = new(
        "RASK011",
        "Route/query param type must implement IParsable<T>",
        "Property '{0}.{1}' of type '{2}' must be 'string' or implement 'System.IParsable<{2}>' to be bound by [RouteParam]/[QueryParam]",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK011"));

    private static readonly DiagnosticDescriptor Rask012 = new(
        "RASK012",
        "Multiple [NotFound] components",
        "Multiple [NotFound] components found in this assembly; only one is allowed ('{0}' is a duplicate)",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK012"));

    private static readonly DiagnosticDescriptor Rask013 = new(
        "RASK013",
        "[NotFound] cannot be combined with [Route]",
        "Class '{0}' has both [NotFound] and [Route]; remove [Route] (NotFound is the catch-all)",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK013"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c
                                    && c.AttributeLists.Count > 0
                                    && !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        var grouped = candidates.Collect();
        context.RegisterSourceOutput(grouped, static (spc, list) => Emit(spc, list));

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

        // Route<T>() helpers live in `Rask.Core.Routing.Generated` (the partial class that
        // ComponentFactoryGenerator already emits for Router/Outlet). They're emitted exactly
        // once per build, in the assembly that *defines* the Route record — i.e. only when
        // we're compiling Rask.Core itself. Downstream assemblies consume the compiled
        // helpers and don't re-emit.
        var ownsRoute = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var routeType = compilation.GetTypeByMetadataName("Rask.Core.Routing.Route");
            return routeType is not null
                   && SymbolEqualityComparer.Default.Equals(routeType.ContainingAssembly, compilation.Assembly);
        });
        context.RegisterSourceOutput(ownsRoute, static (spc, owns) =>
        {
            if (!owns)
            {
                return;
            }

            EmitRouteHelpers(spc);
        });
    }

    private static void EmitRouteHelpers(SourceProductionContext spc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core.Routing;");
        sb.AppendLine();
        sb.AppendLine("public static partial class Generated");
        sb.AppendLine("{");
        sb.AppendLine(
            "    public static global::Rask.Core.Routing.Route Route<[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] T>(string template, global::System.Collections.Generic.IReadOnlyList<global::Rask.Core.Routing.Route>? SubRoutes = null)");
        sb.AppendLine("        where T : global::Rask.Core.Component");
        sb.AppendLine("        => new(typeof(T), template, SubRoutes);");
        sb.AppendLine();
        sb.AppendLine(
            "    public static global::Rask.Core.Routing.Route Route<[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] T>(global::System.Collections.Generic.IReadOnlyList<global::Rask.Core.Routing.Route>? SubRoutes = null)");
        sb.AppendLine("        where T : global::Rask.Core.Component");
        sb.AppendLine(
            "        => new(typeof(T), global::Rask.Core.Routing.RouteTemplateResolver.GetLocalTemplate(typeof(T)), SubRoutes);");
        sb.AppendLine("}");

        spc.AddSource("Rask.Core.Routing.Generated.RouteFactory.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
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
            false,
            true);
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
        var hasRoute = classAttrs.Any(a => a.AttributeClass?.ToDisplayString() == RouteAttrFullName);

        string? reason = null;
        if (!inheritsComponent)
        {
            reason = "class does not inherit from Component";
        }
        else if (symbol.IsAbstract)
        {
            reason = "class is abstract";
        }
        else if (!hasRoute)
        {
            reason = "class is not marked [Route]";
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

    private static void Emit(SourceProductionContext spc, ImmutableArray<Candidate> candidates)
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
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(group.Key);
            if (hasNs)
            {
                sb.Append("namespace ").Append(group.Key).AppendLine(";");
                sb.AppendLine();
            }

            sb.AppendLine("public static partial class Routes");
            sb.AppendLine("{");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in routedInGroup.OrderBy(c => c.TypeName, StringComparer.Ordinal))
            {
                if (!seen.Add(c.TypeName))
                {
                    continue;
                }

                EmitRouteFactory(spc, sb, c, byFqn);
                sb.AppendLine();
            }

            sb.AppendLine("}");

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

        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init()");
        sb.AppendLine("    {");
        sb.AppendLine(
            "        global::Rask.Core.Routing.RouteRegistry.Add(new global::Rask.Core.Routing.RouteRegistration[]");
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

    private static void EmitRouteFactory(SourceProductionContext spc, StringBuilder sb, Candidate c,
        Dictionary<string, Candidate> byFqn)
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
        EmitFactoryBody(sb, c, firstParts!, firstResolved!, queryProps);
    }

    private static void EmitStub(StringBuilder sb, Candidate c)
    {
        sb.Append("    public static ").Append(RouteUrlFullName).Append(' ').Append(c.TypeName).AppendLine("()");
        sb.AppendLine(
            "        => throw new global::System.InvalidOperationException(\"Route source generation failed; see diagnostics.\");");
    }

    private static void EmitFactoryBody(StringBuilder sb, Candidate c, List<TemplatePart> parts,
        List<ResolvedPathParam> pathParams, List<RoutePropInfo> queryProps)
    {
        // Signature: required path params first (in declaration order), then optional, then query
        var orderedPath = pathParams.OrderBy(p => p.Part.Optional ? 1 : 0).ToList();

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
                error = "empty segment";
                return false;
            }

            if (seg[0] == '{' && seg[seg.Length - 1] == '}')
            {
                var inner = seg.Substring(1, seg.Length - 2);
                if (inner.StartsWith("**", StringComparison.Ordinal))
                {
                    error = "catch-all '{**...}' segments are not supported in typed routes";
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
                    error = "param has no name";
                    return false;
                }

                parts.Add(new ParamPart(name, constraint, optional));
            }
            else if (seg.IndexOf('{') >= 0 || seg.IndexOf('}') >= 0)
            {
                error = "mixed literal/param segments are not supported";
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
        bool HasRouteAttr);

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

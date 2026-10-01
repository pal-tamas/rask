using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators;

[Generator(LanguageNames.CSharp)]
public sealed partial class RoutesGenerator : IIncrementalGenerator
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

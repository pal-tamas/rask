using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK196 — an entity holds another aggregate's id by hand — <c>public VehicleId? VehicleId { get; private set; }</c>,
///     perhaps beside a <c>Vehicle</c> navigation — where one <c>public partial BelongsTo&lt;Vehicle&gt;? Vehicle { get; }</c>
///     says the same.
/// </summary>
/// <remarks>
///     <para>
///         The aggregate an id names is found as the read face finds it: a typed id by its type (the one aggregate keyed by
///         it), a relationship <c>Configure</c> writes out by its <c>HasOne&lt;T&gt;()</c>, any other id by its name — the
///         aggregate it is named after exactly, or the longest one its name ends in. Only where the move is no migration and
///         leaves the read face as it was: the navigation keeps the name the id gives it, the key its column.
///     </para>
///     <para>
///         Not for the row's own id, a part of a key of several columns, the tenant's (<c>IBelongsTo&lt;Tenant&gt;</c>), a
///         child's way back to the root holding it, or an entity with a form, which does not carry a <c>BelongsTo</c> yet.
///         A suggestion: the quick-fix makes the move.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HeldReferenceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic's property naming the reference the id becomes — <c>Vehicle</c>.</summary>
    public const string NavigationProperty = "Navigation";

    /// <summary>The diagnostic's property holding the metadata name of the aggregate referred to.</summary>
    public const string TargetProperty = "Target";

    private static readonly DiagnosticDescriptor Rask196 = new(
        "RASK196",
        "Another aggregate's id held by hand",
        "'{0}.{1}' holds the id of a '{2}' by hand — declare 'public partial {3} {4} {{ get; }}', which generates '{1}' and "
        + "reads the '{2}' it names",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An id of another aggregate, written by hand, is a reference no one can read but by hand. "
                     + "public partial BelongsTo<X>? X { get; } generates the same id — same name, type and column, so the "
                     + "move is no migration — maps it as a relationship that never cascades, and reads the X through its read "
                     + "face: x.X.Read(), or Include(x => x.X) on a query. A hand-written X navigation beside the id goes too.",
        helpLinkUri: DiagnosticHelp.Link("RASK196"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask196);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(BelongsToShape.MetadataName) is null)
            {
                return;
            }

            var aggregates = Aggregates.Of(start.Compilation);
            start.RegisterSymbolAction(ctx => Analyze(ctx, aggregates), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, Aggregates aggregates)
    {
        var entity = (INamedTypeSymbol)context.Symbol;
        if (!AggregateShape.IsMappedEntity(entity) ||
            (AggregateShape.IsAggregate(entity) &&
             (AggregateShape.TraitsOf(entity) & (AggregateShape.CreateModel | AggregateShape.UpdateModel)) != 0))
        {
            return;
        }

        var configured = ConfiguredRelationships.Of(entity, context.Compilation, context.CancellationToken);
        var keyParts = CompositeKeyShape.HasCompositeKey(entity)
            ? new HashSet<string>(CompositeKeyShape.ValidPartsOf(entity).Select(static p => p.Name), StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in entity.GetMembers().OfType<IPropertySymbol>())
        {
            if (!IsHeldId(property) || keyParts.Contains(property.Name) ||
                property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) is not PropertyDeclarationSyntax declaration ||
                Target(entity, property, configured, aggregates) is not { } target ||
                !Refers(entity, target, property))
            {
                continue;
            }

            var navigation = property.Name.Substring(0, property.Name.Length - BelongsToShape.KeySuffix.Length);
            var optional = IsNullable(property.Type);
            var type = "BelongsTo<" + target.Name + ">" + (optional ? "?" : "");
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(NavigationProperty, navigation)
                .Add(TargetProperty, MetadataName(target));

            context.ReportDiagnostic(Diagnostic.Create(
                Rask196, declaration.Identifier.GetLocation(), properties, entity.Name, property.Name, target.Name, type, navigation));
        }
    }

    // A stored {X}Id the entity declares: public to read, not the row's own key, not the tenant's.
    private static bool IsHeldId(IPropertySymbol property) =>
        property is { IsStatic: false, IsIndexer: false, IsOverride: false, DeclaredAccessibility: Accessibility.Public } &&
        property.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
        property.Name.Length > BelongsToShape.KeySuffix.Length &&
        property.Name.EndsWith(BelongsToShape.KeySuffix, StringComparison.Ordinal) &&
        !string.Equals(property.Name, AggregateShape.TenantColumn, StringComparison.Ordinal);

    // The aggregate the id names, as the read face would settle it: its type, then Configure, then its name.
    private static INamedTypeSymbol? Target(
        INamedTypeSymbol entity, IPropertySymbol property, IReadOnlyList<ReadRelationship> configured, Aggregates aggregates)
    {
        var key = Underlying(property.Type);
        var navigation = property.Name.Substring(0, property.Name.Length - BelongsToShape.KeySuffix.Length);

        if (aggregates.KeyedBy(key) is { } typed)
        {
            return typed;
        }

        var stated = configured.FirstOrDefault(r =>
            r.Navigation is null && r.ForeignKeys.Count == 1 && string.Equals(r.ForeignKeys[0], property.Name, StringComparison.Ordinal));
        if (stated is not null)
        {
            // The read face names it by the id only when the id ends in the target's last word; any other name would move.
            return aggregates.Named(stated.PrincipalTypeName) is { } principal &&
                   navigation.EndsWith(LastWord(principal.Name), StringComparison.Ordinal) &&
                   SameKey(principal, key)
                ? principal
                : null;
        }

        return aggregates.ByName(navigation) is { } named && SameKey(named, key) && !IsOwnId(entity, named, navigation) ? named : null;
    }

    // Whether the move keeps everything: a target the BelongsTo may name, and a free name for the reference.
    private static bool Refers(INamedTypeSymbol entity, INamedTypeSymbol target, IPropertySymbol property)
    {
        var navigation = property.Name.Substring(0, property.Name.Length - BelongsToShape.KeySuffix.Length);
        if (target.AllInterfaces.Any(static i => string.Equals(i.Name, "ITenant", StringComparison.Ordinal)) ||
            (!AggregateShape.IsAggregate(entity) && HoldsAsChild(target, entity)))
        {
            return false;
        }

        // A member of that name is only the hand-written navigation to the same aggregate, which the move replaces.
        return entity.GetMembers(navigation).All(m =>
            m is IPropertySymbol held && SymbolEqualityComparer.Default.Equals(Underlying(held.Type), target));
    }

    private static bool HoldsAsChild(INamedTypeSymbol target, INamedTypeSymbol entity) =>
        GeneratedModelShape.Properties(target).Any(p =>
            GeneratedModelShape.DescribeChild(target, p) is { } child && SymbolEqualityComparer.Default.Equals(child.ChildType, entity));

    private static bool IsOwnId(INamedTypeSymbol entity, INamedTypeSymbol target, string navigation) =>
        SymbolEqualityComparer.Default.Equals(entity, target) && string.Equals(navigation, entity.Name, StringComparison.Ordinal);

    private static bool SameKey(INamedTypeSymbol aggregate, ITypeSymbol key) =>
        AggregateShape.TryGetIdType(aggregate, out var id) && SymbolEqualityComparer.Default.Equals(id, key);

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static ITypeSymbol Underlying(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);

    // ForeignTaxCategory -> Category, as ConfiguredReferences settles a configured navigation's name.
    private static string LastWord(string name)
    {
        for (var i = name.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(name[i]))
            {
                return name.Substring(i);
            }
        }

        return name;
    }

    private static string MetadataName(INamedTypeSymbol type)
    {
        var names = new List<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            names.Insert(0, current.MetadataName);
        }

        var name = string.Join("+", names);
        return type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." + name : name;
    }

    /// <summary>The compilation's own aggregates, by name and by key type.</summary>
    private sealed class Aggregates
    {
        private readonly Dictionary<string, INamedTypeSymbol?> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<ITypeSymbol, INamedTypeSymbol?> _byKey = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, INamedTypeSymbol> _byFullName = new(StringComparer.Ordinal);

        public static Aggregates Of(Compilation compilation)
        {
            var found = new Aggregates();
            found.Walk(compilation.Assembly.GlobalNamespace);
            return found;
        }

        public INamedTypeSymbol? KeyedBy(ITypeSymbol key) => _byKey.TryGetValue(key, out var aggregate) ? aggregate : null;

        public INamedTypeSymbol? Named(string fullyQualified) => _byFullName.TryGetValue(fullyQualified, out var aggregate) ? aggregate : null;

        // The aggregate named exactly, else the longest one the name ends in; two of one length say nothing.
        public INamedTypeSymbol? ByName(string name)
        {
            if (_byName.TryGetValue(name, out var exact))
            {
                return exact;
            }

            var suffixes = _byName.Where(p => p.Key.Length < name.Length && name.EndsWith(p.Key, StringComparison.Ordinal))
                .GroupBy(static p => p.Key.Length)
                .OrderByDescending(static g => g.Key)
                .FirstOrDefault();
            return suffixes?.Count() == 1 ? suffixes.First().Value : null;
        }

        private void Walk(INamespaceSymbol space)
        {
            foreach (var member in space.GetMembers())
            {
                if (member is INamespaceSymbol nested)
                {
                    Walk(nested);
                }
                else if (member is INamedTypeSymbol type)
                {
                    Add(type);
                }
            }
        }

        private void Add(INamedTypeSymbol type)
        {
            foreach (var nested in type.GetTypeMembers())
            {
                Add(nested);
            }

            if (!AggregateShape.IsMappedEntity(type) || !AggregateShape.TryGetIdType(type, out var key) || key is null)
            {
                return;
            }

            // A typed id keys one entity; one that keys a child as well, or two of anything, settles nothing.
            var typed = key is INamedTypeSymbol { SpecialType: SpecialType.None } named &&
                        !string.Equals(named.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal);
            if (typed)
            {
                _byKey[key] = _byKey.ContainsKey(key) || !AggregateShape.IsAggregate(type) ? null : type;
            }

            if (AggregateShape.IsAggregate(type))
            {
                _byName[type.Name] = _byName.ContainsKey(type.Name) ? null : type;
                _byFullName[type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = type;
            }
        }
    }
}

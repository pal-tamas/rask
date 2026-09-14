using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators.Shared;

/// <summary>The part a property of an entity plays in its generated model.</summary>
/// <remarks>
/// There is no key role: the model never carries <c>Model&lt;TId&gt;.Id</c> (see <see cref="ModelShape" />).
/// </remarks>
internal enum ModelMemberRole
{
    /// <summary>An ordinary value.</summary>
    Value,

    /// <summary>An <c>IVersioned</c> entity's <c>int Version</c>, the token an edit form carries back.</summary>
    Version,
}

/// <summary>One property of an entity that its generated model carries.</summary>
internal sealed class ModelMember(IPropertySymbol property, ModelMemberRole role, ModelValueObject? valueObject)
{
    /// <summary>The entity's property; the model's property has the same name.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The part it plays.</summary>
    public ModelMemberRole Role { get; } = role;

    /// <summary>The nested model standing in for a value object, or null for a plain value.</summary>
    public ModelValueObject? ValueObject { get; } = valueObject;

    /// <summary>Whether the property is declared nullable (<c>?</c>).</summary>
    public bool Nullable => Property.Type.NullableAnnotation == NullableAnnotation.Annotated;
}

/// <summary>The nested model a value object becomes: <c>ProductModel.MoneyModel</c> for <c>Money</c>.</summary>
/// <remarks>
/// However the value object itself is declared — a positional record, private setters — its nested model is a
/// generated mutable class a form can bind into.
/// </remarks>
internal sealed class ModelValueObject(
    INamedTypeSymbol type,
    string modelName,
    IReadOnlyList<ModelValueObjectMember> members)
{
    /// <summary>The value object's own type.</summary>
    public INamedTypeSymbol Type { get; } = type;

    /// <summary>The nested class's simple name, unique within its model.</summary>
    public string ModelName { get; } = modelName;

    /// <summary>Its properties, in declaration order.</summary>
    public IReadOnlyList<ModelValueObjectMember> Members { get; } = members;
}

/// <summary>One property of a value object's nested model.</summary>
internal sealed class ModelValueObjectMember(IPropertySymbol property, ModelValueObject? valueObject)
{
    /// <summary>The value object's property.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The nested model for a value object inside a value object, or null for a plain value.</summary>
    public ModelValueObject? ValueObject { get; } = valueObject;

    /// <summary>Whether the property is declared nullable (<c>?</c>).</summary>
    public bool Nullable => Property.Type.NullableAnnotation == NullableAnnotation.Annotated;
}

/// <summary>Everything the generated model of one entity is made of.</summary>
/// <remarks>
/// The model never carries <c>Model&lt;TId&gt;.Id</c>. It is what a form posts back, so a key on it would be a key the
/// client chooses: an edit could be re-pointed at any row by changing one field (overposting). The id travels beside
/// it — a route parameter, or a command property the handler loads the row by.
/// </remarks>
internal sealed class ModelShape(
    INamedTypeSymbol entity,
    IReadOnlyList<ModelMember> members,
    IReadOnlyList<ModelValueObject> valueObjects)
{
    /// <summary>The entity.</summary>
    public INamedTypeSymbol Entity { get; } = entity;

    /// <summary>The model's properties, in the order they are emitted.</summary>
    public IReadOnlyList<ModelMember> Members { get; } = members;

    /// <summary>Every nested value-object model, each once, in the order they are emitted.</summary>
    public IReadOnlyList<ModelValueObject> ValueObjects { get; } = valueObjects;

    /// <summary>Whether the model carries a <see cref="ModelMemberRole.Version" />.</summary>
    public bool Versioned => Members.Any(static m => m.Role == ModelMemberRole.Version);
}

/// <summary>
///     The one definition of what <c>ModelInputGenerator</c> generates for a <c>Rask.Data.Model</c> entity:
///     which classes get a model, what the model is called, and which properties — and value-object
///     models — it carries.
/// </summary>
/// <remarks>
///     <para>
///         Shared as source because source generators cannot see each other's output. In the compilation
///         that declares <c>Product</c>, every OTHER generator sees <c>ProductModel</c> as an unresolved
///         error type with no members. A validator registry, a wire codec or an API client that has to
///         name or encode the model reconstructs it from here instead — and a second copy of these rules
///         would drift from the generator, with the drift landing as a codec that silently drops a field.
///     </para>
///     <para>
///         Symbol-based and stateless: nothing here is cached across calls, so it is safe to call from an
///         output callback over any compilation.
///     </para>
/// </remarks>
internal static class GeneratedModelShape
{
    /// <summary>The suffix the generated model takes after the entity's name.</summary>
    public const string ModelSuffix = "Model";

    /// <summary>How deep value objects nest inside a model before the rest is copied as plain values.</summary>
    public const int MaxValueObjectDepth = 4;

    private const string RaskDataNamespace = "Rask.Data";
    private const string ModelBase = "Model";
    private const string SkipModelAttribute = "Rask.Data.SkipModelAttribute";
    private const string NotMappedAttribute = "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute";
    private const string ValueObjectInterface = "Rask.Data.IValueObject";

    /// <summary>The display format generated code names types in: fully qualified, keeping <c>?</c>.</summary>
    public static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // ---- entities -------------------------------------------------------------------------------

    /// <summary>
    ///     Walks the base chain to <c>Rask.Data.Model</c> or <c>Model&lt;TId&gt;</c>, handing back <c>TId</c>.
    ///     Returns false for a class that is not an entity at all.
    /// </summary>
    public static bool TryGetIdType(INamedTypeSymbol symbol, out ITypeSymbol? idType)
    {
        idType = null;

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            // By name and namespace rather than by display string: Model and Model<TId> are both the
            // base, and a display string carries the type-parameter name, which is not ours to depend on.
            if (current.Name != ModelBase || current.ContainingNamespace?.ToDisplayString() != RaskDataNamespace)
            {
                continue;
            }

            if (current.TypeArguments.Length == 1)
            {
                idType = current.TypeArguments[0];
            }

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether <paramref name="symbol" /> is an entity the model generator considers at all: concrete,
    ///     non-static, non-generic, and not marked <c>[SkipModel]</c>. A candidate may still be refused a
    ///     model — see <see cref="GetsModel" />.
    /// </summary>
    public static bool IsCandidate(INamedTypeSymbol symbol) =>
        symbol is { IsAbstract: false, IsStatic: false, IsGenericType: false } &&
        TryGetIdType(symbol, out _) &&
        !HasAttribute(symbol, SkipModelAttribute);

    /// <summary>
    ///     A hand-written type already occupying the model's name beside <paramref name="entity" /> that the
    ///     generator cannot extend (it is not a <c>partial</c> class), or null when the name is free.
    /// </summary>
    public static INamedTypeSymbol? ClashingType(INamedTypeSymbol entity) =>
        entity.ContainingNamespace.GetTypeMembers(ModelName(entity)) is { Length: > 0 } existing &&
        !existing.All(IsPartialSourceClass)
            ? existing[0]
            : null;

    /// <summary>Whether a model is actually generated for <paramref name="symbol" />.</summary>
    public static bool GetsModel(INamedTypeSymbol symbol) =>
        IsCandidate(symbol) && symbol.ContainingType is null && ClashingType(symbol) is null;

    /// <summary>The model's simple name: <c>ProductModel</c>.</summary>
    public static string ModelName(INamedTypeSymbol entity) => entity.Name + ModelSuffix;

    /// <summary>The model's fully qualified name, ready to emit: <c>global::Shop.ProductModel</c>.</summary>
    public static string ModelFqn(INamedTypeSymbol entity) =>
        (entity.ContainingNamespace is null || entity.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + entity.ContainingNamespace.ToDisplayString() + ".") + ModelName(entity);

    /// <summary>
    ///     The entity whose generated model <paramref name="type" /> is, as another generator sees it — or
    ///     null when it is not one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two shapes answer. An <b>error type</b> named <c>XModel</c> is the usual one: the model is
    ///         generated beside the entity, so in that same compilation no other generator can resolve it.
    ///         The entity must then be the ONLY model-bearing entity named <c>X</c> declared in the
    ///         compilation — within the error type's namespace when it was written qualified — because an
    ///         unresolved simple name carries nothing else to choose by.
    ///     </para>
    ///     <para>
    ///         A <b>hand-written <c>partial</c> class</b> named <c>XModel</c> in the entity's own namespace
    ///         is the other. It resolves, but only to the author's half: every generated property is
    ///         missing from it, and encoding what is visible would send a model with none of its values.
    ///     </para>
    /// </remarks>
    public static INamedTypeSymbol? EntityFor(ITypeSymbol type, Compilation compilation) =>
        EntitiesFor(type, compilation) is { Count: 1 } one ? one[0] : null;

    /// <summary>
    ///     Every entity whose generated model <paramref name="type" /> could be, by the rules of
    ///     <see cref="EntityFor" />. More than one means an unqualified name that nothing a generator can see
    ///     chooses between — the using directives that settle it for the compiler are not on the symbol.
    /// </summary>
    public static IReadOnlyList<INamedTypeSymbol> EntitiesFor(ITypeSymbol type, Compilation compilation)
    {
        if (type is not INamedTypeSymbol { Arity: 0, ContainingType: null } named ||
            named.Name.Length <= ModelSuffix.Length ||
            !named.Name.EndsWith(ModelSuffix, StringComparison.Ordinal))
        {
            return [];
        }

        var unresolved = named.TypeKind == TypeKind.Error;
        if (!unresolved && !(named.TypeKind == TypeKind.Class && IsPartialSourceClass(named)))
        {
            return [];
        }

        var qualifier = named.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null;
        var entityName = named.Name.Substring(0, named.Name.Length - ModelSuffix.Length);

        var found = new List<INamedTypeSymbol>();
        foreach (var symbol in compilation.GetSymbolsWithName(entityName, SymbolFilter.Type))
        {
            if (symbol is not INamedTypeSymbol candidate || candidate.ContainingType is not null ||
                !IsCandidate(candidate))
            {
                continue;
            }

            var candidateNamespace = candidate.ContainingNamespace is { IsGlobalNamespace: false } cns
                ? cns.ToDisplayString()
                : null;

            // A resolved partial is in a real namespace, so the entity must share it exactly; an unresolved
            // name only constrains the namespace when the author qualified it.
            if ((!unresolved || qualifier is not null) && !string.Equals(candidateNamespace, qualifier, StringComparison.Ordinal))
            {
                continue;
            }

            if (!found.Contains(candidate, SymbolEqualityComparer.Default))
            {
                found.Add(candidate);
            }
        }

        return found;
    }

    /// <summary>
    ///     <paramref name="type" /> displayed in <paramref name="format" />, with every unresolved generated
    ///     model inside it — <c>ProductModel</c>, <c>List&lt;ProductModel&gt;</c> — written as its fully
    ///     qualified name.
    /// </summary>
    /// <remarks>
    ///     An error type displays as the bare name its author wrote, and a bare name emitted into generated
    ///     code in another namespace does not bind. A type that mentions no generated model is displayed
    ///     exactly as before, so no existing generated output changes.
    /// </remarks>
    public static string DisplayName(ITypeSymbol type, SymbolDisplayFormat format, Compilation? compilation) =>
        compilation is null || !MentionsUnresolvedModel(type, compilation)
            ? type.ToDisplayString(format)
            : Rebuild(type, format, compilation);

    /// <summary>
    ///     The unresolved generated model inside <paramref name="type" /> when it is <c>ProductModel?</c> — or
    ///     null when it is anything else.
    /// </summary>
    /// <remarks>
    ///     The compiler cannot tell that a type it cannot find is a class, so an unresolved <c>ProductModel?</c>
    ///     binds as <c>Nullable&lt;ProductModel&gt;</c>, a nullable VALUE type. A generated model is a class, so
    ///     what the author wrote is a nullable reference: the model itself, which may be null.
    /// </remarks>
    public static ITypeSymbol? NullableUnresolvedModel(ITypeSymbol type, Compilation compilation) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } nullable &&
        nullable.TypeArguments[0].TypeKind == TypeKind.Error &&
        EntitiesFor(nullable.TypeArguments[0], compilation).Count > 0
            ? nullable.TypeArguments[0]
            : null;

    private static bool MentionsUnresolvedModel(ITypeSymbol type, Compilation compilation) =>
        type switch
        {
            IArrayTypeSymbol array => MentionsUnresolvedModel(array.ElementType, compilation),
            INamedTypeSymbol { TypeKind: TypeKind.Error } error when EntityFor(error, compilation) is not null => true,
            INamedTypeSymbol named => named.TypeArguments.Any(t => MentionsUnresolvedModel(t, compilation)),
            _ => false,
        };

    private static string Rebuild(ITypeSymbol type, SymbolDisplayFormat format, Compilation compilation)
    {
        var annotated = (format.MiscellaneousOptions & SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier) != 0
                        && type.NullableAnnotation == NullableAnnotation.Annotated
                        && !type.IsValueType
                ? "?"
                : "";

        switch (type)
        {
            case IArrayTypeSymbol array:
                return Rebuild(array.ElementType, format, compilation) + "[" + new string(',', array.Rank - 1) + "]" + annotated;

            case INamedTypeSymbol { TypeKind: TypeKind.Error } error when EntityFor(error, compilation) is { } entity:
                return ModelFqn(entity) + annotated;

            // Displayed as the nullable reference it is, not as Nullable<T> around a class.
            case INamedTypeSymbol nullable when NullableUnresolvedModel(nullable, compilation) is { } inner:
                return Rebuild(inner, format, compilation) +
                       ((format.MiscellaneousOptions & SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier) != 0 ? "?" : "");

            case INamedTypeSymbol { TypeArguments.Length: > 0 } generic when MentionsUnresolvedModel(generic, compilation):
                var definition = generic.ToDisplayString(format
                    .WithGenericsOptions(SymbolDisplayGenericsOptions.None)
                    .RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
                return definition + "<" +
                       string.Join(", ", generic.TypeArguments.Select(t => Rebuild(t, format, compilation))) +
                       ">" + annotated;

            default:
                return type.ToDisplayString(format);
        }
    }

    // ---- the model ------------------------------------------------------------------------------

    /// <summary>The properties and value-object models the generated model of <paramref name="entity" /> carries.</summary>
    public static ModelShape Describe(INamedTypeSymbol entity, CancellationToken cancellationToken = default)
    {
        var timestamped = Implements(entity, "Rask.Data.ITimestamped");
        var softDeletable = Implements(entity, "Rask.Data.ISoftDeletable");
        var versioned = Implements(entity, "Rask.Data.IVersioned");

        var valueObjects = new ValueObjectCollector(ModelName(entity));
        var members = new List<ModelMember>();

        foreach (var property in Properties(entity))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsKey(property))
            {
                continue;
            }

            if (DescribeMember(property, timestamped, softDeletable, versioned, valueObjects) is { } member)
            {
                members.Add(member);
            }
        }

        return new ModelShape(entity, members, valueObjects.Shapes);
    }

    // Derived members first, so a property re-declared lower down hides the base one by name.
    private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol entity)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = entity; type is not null && !IsNonGenericModel(type); type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (seen.Add(property.Name))
                {
                    yield return property;
                }
            }
        }
    }

    private static ModelMember? DescribeMember(
        IPropertySymbol property,
        bool timestamped,
        bool softDeletable,
        bool versionedMarker,
        ValueObjectCollector valueObjects)
    {
        if (property.IsStatic || property.IsIndexer ||
            property.DeclaredAccessibility != Accessibility.Public ||
            property.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
        {
            return null;
        }

        // Framework-owned: the events buffer, and the columns the interceptors stamp.
        if ((property.Name == "DomainEvents" && IsRaskDataType(property.ContainingType)) ||
            (timestamped && property.Name is "CreatedAt" or "UpdatedAt") ||
            (softDeletable && property.Name == "DeletedAt"))
        {
            return null;
        }

        if (HasAttribute(property, SkipModelAttribute) || HasAttribute(property, NotMappedAttribute) ||
            IsNavigationOrCollection(property.Type))
        {
            return null;
        }

        var role = versionedMarker && property.Name == "Version" && property.Type.SpecialType == SpecialType.System_Int32
            ? ModelMemberRole.Version
            : ModelMemberRole.Value;

        // Computed — no setter of any accessibility and no backing field — so EF Core does not map it either.
        if (property.SetMethod is null && !HasBackingField(property))
        {
            return null;
        }

        return new ModelMember(property, role, valueObjects.Of(property.Type, 0, ImmutableHashSet<string>.Empty));
    }

    /// <summary>
    ///     Builds each value object's nested model once per entity, however many places it appears.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only one nested class per name is emitted, so every use of a value object must agree with
    ///         that one class. Recomputing the shape at each use did not guarantee that: the depth limit
    ///         and the cycle guard both depend on the path taken, so <c>Money</c> reached directly and
    ///         <c>Money</c> reached four levels down came out as different shapes, and the second one's
    ///         conversions named members the emitted class did not have.
    ///     </para>
    ///     <para>
    ///         Names are made unique for the same reason: <c>Shop.Money</c> and <c>Billing.Money</c> in one
    ///         entity would otherwise both claim <c>MoneyModel</c>. The second becomes <c>MoneyModel2</c>.
    ///         The model's own name is reserved too, since a nested class may not share its outer class's.
    ///     </para>
    /// </remarks>
    private sealed class ValueObjectCollector(string modelName)
    {
        private readonly Dictionary<string, ModelValueObject> _byType = new(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new(StringComparer.Ordinal) { modelName };

        public List<ModelValueObject> Shapes { get; } = new();

        public ModelValueObject? Of(ITypeSymbol type, int depth, ImmutableHashSet<string> seen)
        {
            if (depth >= MaxValueObjectDepth || type is not INamedTypeSymbol named || named.IsAbstract ||
                !Implements(named, ValueObjectInterface))
            {
                return null;
            }

            var typeName = named.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);
            if (seen.Contains(typeName))
            {
                return null;
            }

            if (_byType.TryGetValue(typeName, out var known))
            {
                return known;
            }

            var properties = named.GetMembers().OfType<IPropertySymbol>()
                .Where(static p => !p.IsStatic && !p.IsIndexer &&
                                   p.DeclaredAccessibility == Accessibility.Public &&
                                   p.GetMethod is { DeclaredAccessibility: Accessibility.Public })
                .ToList();

            if (properties.Count == 0)
            {
                return null;
            }

            var inner = seen.Add(typeName);
            var members = properties
                .Select(p => new ModelValueObjectMember(p, Of(p.Type, depth + 1, inner)))
                .ToList();

            var shape = new ModelValueObject(named, UniqueName(named.Name + ModelSuffix), members);
            _byType[typeName] = shape;
            Shapes.Add(shape);
            return shape;
        }

        private string UniqueName(string preferred)
        {
            var name = preferred;
            for (var suffix = 2; !_names.Add(name); suffix++)
            {
                name = preferred + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return name;
        }
    }

    // ---- symbol helpers -------------------------------------------------------------------------

    /// <summary>Whether every declaration of <paramref name="type" /> is a <c>partial class</c> in source.</summary>
    public static bool IsPartialSourceClass(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Length > 0 &&
        type.DeclaringSyntaxReferences.All(static r =>
            r.GetSyntax() is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword));

    /// <summary>Whether <paramref name="symbol" /> carries the attribute with that full name.</summary>
    public static bool HasAttribute(ISymbol symbol, string fullyQualifiedAttribute) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fullyQualifiedAttribute);

    private static bool IsNonGenericModel(INamedTypeSymbol type) =>
        type is { Name: ModelBase, TypeArguments.Length: 0 } && IsRaskDataType(type);

    private static bool IsRaskDataType(INamedTypeSymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() == RaskDataNamespace;

    private static bool IsKey(IPropertySymbol property) =>
        property.Name == "Id" &&
        property.ContainingType is { Name: ModelBase, TypeArguments.Length: 1 } model &&
        IsRaskDataType(model);

    private static bool Implements(INamedTypeSymbol type, string fullyQualifiedInterface) =>
        type.AllInterfaces.Any(i => i.ToDisplayString() == fullyQualifiedInterface);

    private static bool HasBackingField(IPropertySymbol property)
    {
        var definition = property.OriginalDefinition;
        return definition.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, definition));
    }

    private static bool IsNavigationOrCollection(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String ||
            type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            return false;
        }

        if (type is INamedTypeSymbol named && TryGetIdType(named, out _))
        {
            return true;
        }

        return type is IArrayTypeSymbol ||
               type.SpecialType == SpecialType.System_Collections_IEnumerable ||
               type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable);
    }
}

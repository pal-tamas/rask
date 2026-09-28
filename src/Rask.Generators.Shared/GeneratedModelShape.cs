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
/// There is no key role: the model never carries <c>Entity&lt;TId&gt;.Id</c> (see <see cref="ModelShape.Key" />).
/// </remarks>
internal enum ModelMemberRole
{
    /// <summary>An ordinary value, copied both ways.</summary>
    Value,

    /// <summary>An aggregate's <c>int Version</c>: read into the model, never written back.</summary>
    Version,
}

/// <summary>How generated code rebuilds a value object from its nested model, best first.</summary>
internal enum ModelValueObjectBuild
{
    /// <summary>A PUBLIC constructor naming every property — a positional record: <c>new Money(amount, currency)</c>.</summary>
    Constructor,

    /// <summary>A PUBLIC parameterless constructor and a public setter on every property: <c>new Money { Amount = … }</c>.</summary>
    Initializer,

    /// <summary>A non-public constructor naming every property, called through <c>[UnsafeAccessor]</c>.</summary>
    AccessorConstructor,

    /// <summary>
    ///     A parameterless constructor of any accessibility, then every property written: a public setter
    ///     directly, a non-public setter or a backing field through <c>[UnsafeAccessor]</c>.
    /// </summary>
    AccessorMembers,
}

/// <summary>How generated code gets a value back into an entity property.</summary>
internal enum ModelWriteKind
{
    /// <summary>A public setter, assigned directly.</summary>
    Public,

    /// <summary>A non-public setter, reached through an <c>[UnsafeAccessor]</c> method.</summary>
    Setter,

    /// <summary>No usable setter but a compiler backing field, reached through an <c>[UnsafeAccessor]</c> field.</summary>
    Field,
}

/// <summary>One property of an entity that its generated model carries.</summary>
internal sealed class ModelMember(IPropertySymbol property, ModelMemberRole role, ModelWriteKind? write, ModelValueObject? valueObject)
{
    /// <summary>The entity's property; the model's property has the same name.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The part it plays.</summary>
    public ModelMemberRole Role { get; } = role;

    /// <summary>How it is written back. Null only for <see cref="ModelMemberRole.Version" />, which never is.</summary>
    public ModelWriteKind? Write { get; } = write;

    /// <summary>The nested model standing in for a value object, or null for a plain value.</summary>
    public ModelValueObject? ValueObject { get; } = valueObject;

    /// <summary>Whether the property is declared nullable (<c>?</c>).</summary>
    public bool Nullable => Property.Type.NullableAnnotation == NullableAnnotation.Annotated;
}

/// <summary>The nested model a value object becomes: <c>ProductModel.MoneyModel</c> for <c>Money</c>.</summary>
/// <remarks>
/// However the value object itself is built, its nested model is always a generated mutable class — so the wire
/// shape and the TypeScript read only <see cref="ModelName" /> and <see cref="Members" />, never the build.
/// </remarks>
internal sealed class ModelValueObject(
    INamedTypeSymbol type,
    string modelName,
    ModelValueObjectBuild build,
    IMethodSymbol constructor,
    IReadOnlyList<IPropertySymbol>? constructorOrder,
    IReadOnlyList<ModelValueObjectMember> members)
{
    /// <summary>The value object's own type.</summary>
    public INamedTypeSymbol Type { get; } = type;

    /// <summary>The nested class's simple name, unique within its model.</summary>
    public string ModelName { get; } = modelName;

    /// <summary>How the value object is rebuilt from the model.</summary>
    public ModelValueObjectBuild Build { get; } = build;

    /// <summary>The constructor it is built through: the one naming every property, or the parameterless one.</summary>
    public IMethodSymbol Constructor { get; } = constructor;

    /// <summary>The properties in constructor-parameter order, or null when it is rebuilt by object initializer.</summary>
    public IReadOnlyList<IPropertySymbol>? ConstructorOrder { get; } = constructorOrder;

    /// <summary>Whether the value object is rebuilt through a constructor naming every property.</summary>
    public bool ByConstructor => ConstructorOrder is not null;

    /// <summary>Its properties, in declaration order.</summary>
    public IReadOnlyList<ModelValueObjectMember> Members { get; } = members;

    /// <summary>
    ///     Whether it holds exactly one plain value (<c>record Email(string Value)</c>). Such a value object is one column
    ///     and is carried on the model as that value itself — <c>string? Email</c> — with no nested model.
    /// </summary>
    public bool SingleValue => Members.Count == 1 && Members[0].ValueObject is null;
}

/// <summary>One property of a value object's nested model.</summary>
internal sealed class ModelValueObjectMember(IPropertySymbol property, ModelValueObject? valueObject, ModelWriteKind? write)
{
    /// <summary>The value object's property.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>
    ///     For <see cref="ModelValueObjectBuild.AccessorMembers" />: how the property is written after construction.
    ///     Null for every other build.
    /// </summary>
    public ModelWriteKind? Write { get; } = write;

    /// <summary>The nested model for a value object inside a value object, or null for a plain value.</summary>
    public ModelValueObject? ValueObject { get; } = valueObject;

    /// <summary>Whether the property is declared nullable (<c>?</c>).</summary>
    public bool Nullable => Property.Type.NullableAnnotation == NullableAnnotation.Annotated;
}

/// <summary>How generated code reaches the collection it has to add to and remove from.</summary>
internal enum ModelChildAccess
{
    /// <summary>The property's own type is a writable collection, so the property is used directly.</summary>
    Property,

    /// <summary>The property hands out a read-only view, so its backing field is written instead.</summary>
    Field,

    /// <summary>Neither — Rask cannot sync this collection, and says so with RASK088.</summary>
    None,
}

/// <summary>A collection of children an aggregate holds: the lines of an order, the items of a basket.</summary>
/// <remarks>
/// Only <c>Entity&lt;TId&gt;</c> children are here. A collection of <c>Aggregate&lt;TId&gt;</c> is somebody else's
/// data — RASK087 says so — and is never carried on the model, never synced, and never deleted on the parent's
/// behalf.
/// </remarks>
internal sealed class ModelChild(
    IPropertySymbol property,
    INamedTypeSymbol childType,
    ModelChildAccess access,
    IFieldSymbol? field)
{
    /// <summary>The aggregate's property: <c>Lines</c>.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The child entity type: <c>OrderLine</c>.</summary>
    public INamedTypeSymbol ChildType { get; } = childType;

    /// <summary>How generated code writes the collection.</summary>
    public ModelChildAccess Access { get; } = access;

    /// <summary>The backing field, for <see cref="ModelChildAccess.Field" />.</summary>
    public IFieldSymbol? Field { get; } = field;

    /// <summary>The property's name, which is also the model's.</summary>
    public string Name => Property.Name;
}

/// <summary>A collection of values an entity holds: the tags of a note, the stops of a trip.</summary>
/// <remarks>
/// One column either way — a primitive collection for plain values, a JSON column for value objects — and
/// replaced wholesale on save, because a value has no identity to reconcile against.
/// </remarks>
internal sealed class ModelValueCollection(
    IPropertySymbol property,
    ITypeSymbol element,
    INamedTypeSymbol? valueObject,
    IFieldSymbol? field,
    ModelValueObject? elementModel = null)
{
    /// <summary>The entity's property: <c>Tags</c>.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>What the collection holds: <c>string</c>, <c>Stop</c>.</summary>
    public ITypeSymbol Element { get; } = element;

    /// <summary>The element as a value object, or null when it is a plain value.</summary>
    public INamedTypeSymbol? ValueObject { get; } = valueObject;

    /// <summary>The backing field to write through, or null when the property itself is writable.</summary>
    public IFieldSymbol? Field { get; } = field;

    /// <summary>
    ///     The nested model each element is carried as on the form — <c>StopModel</c> — or null for a plain
    ///     value, which is carried as itself.
    /// </summary>
    public ModelValueObject? ElementModel { get; } = elementModel;

    /// <summary>The property's name, which is also the model's and the read face's.</summary>
    public string Name => Property.Name;
}

/// <summary>Everything the generated model of one entity is made of.</summary>
internal sealed class ModelShape(
    INamedTypeSymbol entity,
    ITypeSymbol? idType,
    IPropertySymbol? key,
    IReadOnlyList<ModelMember> members,
    IReadOnlyList<ModelValueObject> valueObjects,
    IReadOnlyList<ModelChild> children,
    IReadOnlyList<ModelValueCollection> valueCollections)
{
    /// <summary>The entity.</summary>
    public INamedTypeSymbol Entity { get; } = entity;

    /// <summary>The <c>TId</c> of <c>Entity&lt;TId&gt;</c>.</summary>
    public ITypeSymbol? IdType { get; } = idType;

    /// <summary>
    ///     <c>Entity&lt;TId&gt;.Id</c>, which the model deliberately does NOT carry.
    ///     non-generic base.
    /// </summary>
    /// <remarks>
    ///     A model is what a form posts back, so a key on it is a key the client chooses: an edit could be
    ///     re-pointed at any row by changing one field (overposting). The id travels beside the model instead —
    ///     <c>Update(id, model)</c> — and a create never takes one. It is exposed here only for the generated
    ///     create to assign a key EF Core would not generate.
    /// </remarks>
    public IPropertySymbol? Key { get; } = key;

    /// <summary>The model's properties, in the order they are emitted.</summary>
    public IReadOnlyList<ModelMember> Members { get; } = members;

    /// <summary>Every nested value-object model, each once, in the order they are emitted.</summary>
    public IReadOnlyList<ModelValueObject> ValueObjects { get; } = valueObjects;

    /// <summary>The child collections this aggregate holds, in the order they are emitted.</summary>
    public IReadOnlyList<ModelChild> Children { get; } = children;

    /// <summary>The collections of values it holds, in the order they are emitted.</summary>
    public IReadOnlyList<ModelValueCollection> ValueCollections { get; } = valueCollections;

    /// <summary>Whether the model carries a <see cref="ModelMemberRole.Version" />.</summary>
    public bool Versioned => Members.Any(static m => m.Role == ModelMemberRole.Version);
}

/// <summary>
///     The one definition of what <c>ModelInputGenerator</c> generates for a <c>Rask.Data.Aggregate&lt;TId&gt;</c>:
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
    private const string NotMappedAttribute = "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute";

    /// <summary>The display format generated code names types in: fully qualified, keeping <c>?</c>.</summary>
    public static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // ---- entities -------------------------------------------------------------------------------

    /// <summary>
    ///     Walks the base chain to <c>Rask.Data.Entity&lt;TId&gt;</c>, handing back <c>TId</c>. Returns false for a
    ///     class that is not an entity at all.
    /// </summary>
    public static bool TryGetIdType(INamedTypeSymbol symbol, out ITypeSymbol? idType) =>
        AggregateShape.TryGetIdType(symbol, out idType);

    /// <summary>
    ///     Whether <paramref name="symbol" /> is a type the model generator considers at all: a concrete, non-static,
    ///     non-generic AGGREGATE. Only an aggregate is edited through a form. A candidate may still be refused a
    ///     model — see <see cref="GetsModel" />.
    /// </summary>
    public static bool IsCandidate(INamedTypeSymbol symbol) => AggregateShape.IsMappedEntity(symbol);

    /// <summary>Whether <paramref name="symbol" /> is a child: an entity that is not a root of its own.</summary>
    /// <remarks>
    /// A child gets a model so its parent's form can carry it, but no write surface of its own: it is created,
    /// changed and removed as part of the aggregate that holds it, never off its own type.
    /// </remarks>
    public static bool IsChildEntity(INamedTypeSymbol symbol) =>
        AggregateShape.IsMappedEntity(symbol) && !AggregateShape.IsAggregate(symbol);

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
        TryGetIdType(entity, out var idType);

        var valueObjects = new ValueObjectCollector(ModelName(entity));
        var members = new List<ModelMember>();
        var key = AggregateShape.KeyProperty(entity);

        foreach (var property in Properties(entity))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DescribeMember(property, valueObjects) is { } member)
            {
                members.Add(member);
            }
        }

        // The one framework column a form carries: the aggregate's Version, read into the model and sent back so a
        // stale save is refused. CreatedAt, UpdatedAt and DeletedAt are the framework's and never on the model.
        if (VersionProperty(entity) is { } version)
        {
            members.Add(new ModelMember(version, ModelMemberRole.Version, null, null));
        }

        var children = new List<ModelChild>();
        var collections = new List<ModelValueCollection>();

        foreach (var property in Properties(entity))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DescribeChild(entity, property) is { } child)
            {
                children.Add(child);
                continue;
            }

            // The element's nested model is registered through the SAME collector as every other value
            // object, so `Money` reached through a collection and `Money` reached directly are one class.
            if (DescribeValueCollection(entity, property) is { } collection)
            {
                collections.Add(new ModelValueCollection(
                    collection.Property,
                    collection.Element,
                    collection.ValueObject,
                    collection.Field,
                    collection.ValueObject is null
                        ? null
                        : valueObjects.Of(collection.ValueObject, 0, ImmutableHashSet<string>.Empty)));
            }
        }

        return new ModelShape(entity, idType, key, members, valueObjects.Shapes, children, collections);
    }

    /// <summary>
    ///     The child collection <paramref name="property" /> is, or null when it is not one.
    /// </summary>
    /// <remarks>
    ///     A collection of <c>Entity&lt;TId&gt;</c> that is not an <c>Aggregate&lt;TId&gt;</c>. Everything else —
    ///     a collection of values, of value objects, or of other aggregate roots — is not a child, and the last of
    ///     those is what RASK087 warns about.
    /// </remarks>
    public static ModelChild? DescribeChild(INamedTypeSymbol entity, IPropertySymbol property)
    {
        if (property.IsStatic || property.IsIndexer ||
            property.DeclaredAccessibility != Accessibility.Public ||
            property.GetMethod is not { DeclaredAccessibility: Accessibility.Public } ||
            HasAttribute(property, NotMappedAttribute) ||
            CollectionElement(property.Type) is not { } element ||
            !AggregateShape.IsEntity(element) ||
            AggregateShape.IsAggregate(element) ||
            element is not INamedTypeSymbol childType)
        {
            return null;
        }

        // Written through the property when its own type can be added to — `List<Line> Lines { get; }` — and
        // through the backing field when it hands out a read-only view, which is the shape Rask recommends.
        if (ImplementsCollectionOf(property.Type, element))
        {
            return new ModelChild(property, childType, ModelChildAccess.Property, null);
        }

        var fields = entity.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst && ImplementsCollectionOf(f.Type, element))
            .ToList();

        // Exactly one, or Rask would be guessing which field the property hands out.
        return fields.Count == 1
            ? new ModelChild(property, childType, ModelChildAccess.Field, fields[0])
            : new ModelChild(property, childType, ModelChildAccess.None, null);
    }

    /// <summary>
    ///     The collection of values <paramref name="property" /> is, or null when it is not one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A collection whose element is neither an entity nor an aggregate: <c>IReadOnlyList&lt;string&gt;
    ///         Tags</c>, <c>IReadOnlyList&lt;Stop&gt; Stops</c>. Both are one column — a primitive collection for
    ///         plain values, a JSON column for value objects — and both are replaced wholesale, which is what a
    ///         value is. A collection that wants its own identity is a collection of <c>Entity&lt;TId&gt;</c>
    ///         instead, and <see cref="DescribeChild" /> answers for that one.
    ///     </para>
    ///     <para>
    ///         Null for anything Rask has no column for — a <c>Dictionary</c>, a collection of some framework
    ///         type — which is left to the entity's own <c>Configure</c> rather than guessed at.
    ///     </para>
    /// </remarks>
    public static ModelValueCollection? DescribeValueCollection(INamedTypeSymbol entity, IPropertySymbol property)
    {
        if (property.IsStatic || property.IsIndexer ||
            property.DeclaredAccessibility != Accessibility.Public ||
            property.GetMethod is not { DeclaredAccessibility: Accessibility.Public } ||
            HasAttribute(property, NotMappedAttribute) ||
            // A byte[] is a BLOB, not a collection of bytes — and it implements IEnumerable<byte>, so
            // without this it would be mapped as one and every file column would change shape.
            property.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } ||
            CollectionElement(property.Type) is not { } element ||
            AggregateShape.IsEntity(element))
        {
            return null;
        }

        var valueObject = AggregateShape.IsValueObjectType(element) ? element as INamedTypeSymbol : null;

        if (valueObject is null && !IsStorableValue(element))
        {
            return null;
        }

        // Written through the property when it has a setter, and through the one backing field when it hands
        // out a read-only view — the shape Rask recommends, and the shape that is silently unmapped today.
        if (property.SetMethod is { IsInitOnly: false })
        {
            return new ModelValueCollection(property, element, valueObject, null);
        }

        var fields = entity.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst && ImplementsCollectionOf(f.Type, element))
            .ToList();

        // Exactly one, or Rask would be guessing which field the property hands out. None means the property
        // is computed, which is not stored state at all.
        return fields.Count == 1 ? new ModelValueCollection(property, element, valueObject, fields[0]) : null;
    }

    /// <summary>Every collection of values <paramref name="entity" /> holds, its bases included.</summary>
    /// <param name="entity">The entity to walk.</param>
    public static IEnumerable<ModelValueCollection> ValueCollectionsOf(INamedTypeSymbol entity)
    {
        foreach (var property in Properties(entity))
        {
            if (DescribeValueCollection(entity, property) is { } collection)
            {
                yield return collection;
            }
        }
    }

    /// <summary>Whether <paramref name="type" /> is a value a column can hold on its own.</summary>
    private static bool IsStorableValue(ITypeSymbol type)
    {
        var bare = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        return bare.TypeKind == TypeKind.Enum ||
               bare.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String ||
               bare.ToDisplayString() is "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
                   or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly" or "System.Uri";
    }

    /// <summary>The <c>T</c> of the <c>IEnumerable&lt;T&gt;</c> <paramref name="type" /> is, or null.</summary>
    private static ITypeSymbol? CollectionElement(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return null;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return named.TypeArguments[0];
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (contract.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return contract.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="type" /> is an <c>ICollection&lt;TElement&gt;</c> generated code can add to.</summary>
    private static bool ImplementsCollectionOf(ITypeSymbol type, ITypeSymbol element)
    {
        if (type is INamedTypeSymbol { IsGenericType: true } named &&
            named.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_ICollection_T &&
            SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], element))
        {
            return true;
        }

        return type.AllInterfaces.Any(i =>
            i.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_ICollection_T &&
            SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], element));
    }

    // Derived members first, so a property re-declared lower down hides the base one by name. The walk stops at
    // Rask's own bases, whose members (Id, the timestamps, Version, DeletedAt, the events) are the framework's.
    private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol entity)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Stops at the framework's bases: Rask.Data's (Id, the timestamps, Version) and Rask.Auth's Authenticatable, whose
        // credentials a form must never write.
        for (var type = entity; type is not null && !IsRaskDataType(type) && !IsRaskAuthBase(type); type = type.BaseType)
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

    private static ModelMember? DescribeMember(IPropertySymbol property, ValueObjectCollector valueObjects)
    {
        if (property.IsStatic || property.IsIndexer ||
            property.DeclaredAccessibility != Accessibility.Public ||
            property.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
        {
            return null;
        }

        if (HasAttribute(property, NotMappedAttribute) || IsNavigationOrCollection(property.Type))
        {
            return null;
        }

        if (WriteKindOf(property) is not { } write)
        {
            // Computed — no setter and no backing field — so EF Core does not map it either.
            return null;
        }

        return new ModelMember(
            property, ModelMemberRole.Value, write, valueObjects.Of(property.Type, 0, ImmutableHashSet<string>.Empty));
    }

    // Aggregate<TId>.Version as the aggregate inherits it.
    private static IPropertySymbol? VersionProperty(INamedTypeSymbol entity)
    {
        for (var current = entity.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: "Aggregate", TypeArguments.Length: 1 } && IsRaskDataType(current))
            {
                return current.GetMembers("Version").OfType<IPropertySymbol>().FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>How generated code writes <paramref name="property" />, or null when it cannot.</summary>
    public static ModelWriteKind? WriteKindOf(IPropertySymbol property)
    {
        if (!IsNameableFromGeneratedCode(property.ContainingType))
        {
            return null;
        }

        if (property.SetMethod is { IsInitOnly: false } setter)
        {
            return setter.DeclaredAccessibility == Accessibility.Public ? ModelWriteKind.Public : ModelWriteKind.Setter;
        }

        return HasBackingField(property) ? ModelWriteKind.Field : null;
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
            if (depth >= MaxValueObjectDepth || type is not INamedTypeSymbol named ||
                !AggregateShape.IsValueObjectType(named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
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

            // Stored state only. A computed property (`Display => $"{Amount} {Currency}"`) has neither a setter nor a
            // backing field, so no form can write it and no build strategy below can rebuild it — counting it would
            // make a record's primary constructor one parameter short and drop the whole nested model.
            var properties = named.GetMembers().OfType<IPropertySymbol>()
                .Where(static p => !p.IsStatic && !p.IsIndexer &&
                                   p.DeclaredAccessibility == Accessibility.Public &&
                                   p.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                                   (p.SetMethod is not null || HasBackingField(p)))
                .ToList();

            if (properties.Count == 0)
            {
                return null;
            }

            // How it is rebuilt from the model, best first. The public shapes are plain C#. The non-public ones — a
            // private constructor, or `{ get; private set; }`, which is what RASK084 steers a value object
            // towards — go through [UnsafeAccessor], so they need a type generated code can name, and a
            // non-generic one: an accessor into a generic type has to be declared generic itself. Anything else
            // is copied across as the value itself, and a form cannot bind into it.
            // Matched by name AND type: the generated call passes each property's value straight in, so a constructor
            // that parses `string currency` into a `CurrencyCode Currency` would be a CS1503 inside generated code.
            var fullConstructors = named.InstanceConstructors
                .Where(c => c.Parameters.Length == properties.Count &&
                            c.Parameters.All(parameter =>
                                properties.Any(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                                                    SameTypeIgnoringNullability(p.Type, parameter.Type))))
                .ToList();
            var parameterless = named.InstanceConstructors.FirstOrDefault(static c => c.Parameters.Length == 0);
            var reachable = !named.IsGenericType && IsNameableFromGeneratedCode(named);
            var writes = properties.Select(WriteKindOf).ToList();

            ModelValueObjectBuild build;
            IMethodSymbol constructor;
            if (fullConstructors.FirstOrDefault(static c => c.DeclaredAccessibility == Accessibility.Public) is { } open)
            {
                build = ModelValueObjectBuild.Constructor;
                constructor = open;
            }
            else if (parameterless is { DeclaredAccessibility: Accessibility.Public } &&
                     properties.All(static p => p.SetMethod is { DeclaredAccessibility: Accessibility.Public }))
            {
                build = ModelValueObjectBuild.Initializer;
                constructor = parameterless;
            }
            else if (reachable && fullConstructors.Count > 0)
            {
                build = ModelValueObjectBuild.AccessorConstructor;
                constructor = fullConstructors[0];
            }
            else if (reachable && parameterless is not null && writes.All(static w => w is not null))
            {
                build = ModelValueObjectBuild.AccessorMembers;
                constructor = parameterless;
            }
            else
            {
                return null;
            }

            var inner = seen.Add(typeName);
            var members = properties
                .Select((p, i) => new ModelValueObjectMember(
                    p,
                    Of(p.Type, depth + 1, inner),
                    build == ModelValueObjectBuild.AccessorMembers ? writes[i] : null))
                .ToList();

            var order = build is ModelValueObjectBuild.Constructor or ModelValueObjectBuild.AccessorConstructor
                ? constructor.Parameters
                    .Select(parameter =>
                        properties.First(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
                    .ToList()
                : null;

            var shape = new ModelValueObject(named, UniqueName(named.Name + ModelSuffix), build, constructor, order, members);
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

    private static bool IsRaskDataType(INamedTypeSymbol? type) =>
        type?.ContainingNamespace?.ToDisplayString() == RaskDataNamespace;

    private static bool IsRaskAuthBase(INamedTypeSymbol type) =>
        type is { Name: "Authenticatable" } && type.ContainingNamespace?.ToDisplayString() == "Rask.Auth";

    private static bool SameTypeIgnoringNullability(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.Default.Equals(
            left.WithNullableAnnotation(NullableAnnotation.None),
            right.WithNullableAnnotation(NullableAnnotation.None));

    private static bool HasBackingField(IPropertySymbol property)
    {
        var definition = property.OriginalDefinition;
        return definition.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, definition));
    }

    private static bool IsNameableFromGeneratedCode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
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

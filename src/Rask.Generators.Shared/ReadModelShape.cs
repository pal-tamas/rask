using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators;

namespace Rask.Generators.Shared;

/// <summary>One column on a read face: a primitive, named for C# and mapped to the column the write model uses.</summary>
/// <param name="Name">The member's name — <c>TotalAmount</c>.</param>
/// <param name="ColumnName">The column it maps to — <c>Total_Amount</c>, which the write model already owns.</param>
/// <param name="Path">
///     Where it lives on the write model — <c>Total.Amount</c> — so the runtime can find the property EF
///     actually built and copy what it decided.
/// </param>
/// <param name="TypeName">Its fully-qualified type, without a nullable annotation.</param>
/// <param name="Nullable">Whether it is declared nullable.</param>
/// <param name="IsReferenceType">
///     Whether the type is a class, so a non-nullable one needs an initializer the compiler accepts.
/// </param>
internal sealed record ReadMember(
    string Name,
    string ColumnName,
    string Path,
    string TypeName,
    bool Nullable,
    bool IsReferenceType);

/// <summary>
///     An id that MIGHT be a reference to another aggregate — resolved once every entity is known.
/// </summary>
/// <remarks>
/// Inference cannot happen while walking one entity: whether <c>CustomerId</c> points anywhere depends on
/// what else the compilation declares. So the symbol pass records the candidate and the emit pass resolves
/// it, which is also what keeps symbols out of an incremental generator's pipeline.
/// </remarks>
/// <param name="Property">The id property — <c>ShippedByUserId</c>.</param>
/// <param name="Target">The property's name without <c>Id</c> — <c>ShippedByUser</c>, which the navigation takes.</param>
/// <param name="IdTypeName">The id's type, for checking it against the target's key.</param>
/// <param name="Nullable">Whether the id is nullable, and so the navigation too.</param>
internal sealed record ReadReference(
    string Property,
    string Target,
    string IdTypeName,
    bool Nullable);

/// <summary>A navigation on a read face, once its target is known.</summary>
/// <param name="Name">The navigation's name, taken from the PROPERTY — <c>ShippedByUser</c> from <c>ShippedByUserId</c>.</param>
/// <param name="TargetReadType">The read face it points at — <c>global::Shop.UserRead</c>.</param>
/// <param name="ForeignKey">The id property it is inferred from — <c>ShippedByUserId</c>.</param>
/// <param name="Nullable">Whether the id is nullable, and so the navigation too.</param>
internal sealed record ReadNavigation(
    string Name,
    string TargetReadType,
    string ForeignKey,
    bool Nullable);

/// <summary>A collection of children on a read face, and the navigation back from each child to its root.</summary>
/// <param name="Name">The collection's name — <c>Lines</c>.</param>
/// <param name="ChildReadType">The child's read face — <c>global::Shop.OrderLineRead</c>.</param>
/// <param name="ChildWriteType">The child's write type — <c>global::Shop.OrderLine</c>.</param>
/// <param name="Inverse">The navigation back to the root on the child's face — <c>Order</c>.</param>
internal sealed record ReadChildCollection(
    string Name,
    string ChildReadType,
    string ChildWriteType,
    string Inverse);

/// <summary>A collection of values on a read face: one column, queryable like any other.</summary>
/// <remarks>
/// A value object is NOT flattened here, the way a single one is. Flattening is what turns <c>Money Total</c>
/// into two columns; a collection of them is a single JSON column, so there is nothing to flatten it into. The
/// face carries the value object's own type, which is safe for exactly the reason it is a value object: no
/// identity, no behaviour, nothing to save through.
/// </remarks>
/// <param name="Name">The collection's name — <c>Tags</c>, <c>Stops</c>.</param>
/// <param name="ElementTypeName">What it holds — <c>string</c>, <c>global::Trips.Stop</c>.</param>
/// <param name="IsValueObject">Whether the element is a value object, which makes the column JSON.</param>
internal sealed record ReadValueCollection(
    string Name,
    string ElementTypeName,
    bool IsValueObject);

/// <summary>Everything the read face of one entity is made of.</summary>
/// <remarks>
/// Strings and equatable arrays only: an incremental generator that carries an <c>ISymbol</c> between steps
/// keeps a whole compilation alive and never hits its cache.
/// </remarks>
/// <param name="Name">The face's simple name — <c>OrderRead</c>.</param>
/// <param name="FullyQualifiedName">The face's fully-qualified name — <c>global::Shop.OrderRead</c>.</param>
/// <param name="Namespace">The namespace it is emitted into, empty for the global one.</param>
/// <param name="SourceName">The write type's simple name — <c>Order</c>.</param>
/// <param name="SourceTypeName">The write type it reads — <c>global::Shop.Order</c>.</param>
/// <param name="Accessibility">The write type's accessibility, which the face takes.</param>
/// <param name="TableName">The table the convention derives, overridden at runtime by the write model.</param>
/// <param name="IdTypeName">The key's fully-qualified type.</param>
/// <param name="IsAggregate">Whether the source is an aggregate root rather than a child.</param>
/// <param name="Members">The primitive columns, in the order they are emitted.</param>
/// <param name="References">The ids that might be references, for the emit pass to resolve.</param>
/// <param name="Children">The child collections.</param>
/// <param name="ValueCollections">The collections of values: tags, stops.</param>
/// <param name="Location">Where the entity is declared, for a diagnostic to point at.</param>
internal sealed record ReadShape(
    string Name,
    string FullyQualifiedName,
    string Namespace,
    string SourceName,
    string SourceTypeName,
    string Accessibility,
    string TableName,
    string IdTypeName,
    bool IsAggregate,
    EquatableArray<ReadMember> Members,
    EquatableArray<ReadReference> References,
    EquatableArray<ReadChildCollection> Children,
    EquatableArray<ReadValueCollection> ValueCollections,
    SymbolLocation? Location);

/// <summary>
///     The one definition of what a read face looks like: a primitive view of an entity's columns, with the
///     navigations the write model is not allowed to have.
/// </summary>
/// <remarks>
///     <para>
///         Shared as source for the same reason <see cref="GeneratedModelShape" /> is: source generators cannot
///         see each other's output, so anything that has to name a read face reconstructs it from here rather
///         than keeping a second copy of the rules that would drift.
///     </para>
///     <para>
///         This describes the CLR <em>shape</em> only. The MAPPING — which column a member really lands on,
///         whether a property was ignored in the entity's own <c>Configure</c>, what value converter it carries
///         — is mirrored from the built write model at runtime, because none of that is visible to a
///         symbol-based generator. The column names here are the convention's answer, and the runtime overrides
///         them wherever the write model actually decided otherwise.
///     </para>
/// </remarks>
internal static class ReadModelShape
{
    /// <summary>The suffix a read face takes after its entity's name.</summary>
    public const string ReadSuffix = "Read";

    private const string NotMappedAttribute = "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute";

    /// <summary>Whether a read face is generated for <paramref name="symbol" />.</summary>
    /// <remarks>
    /// Every mapped entity gets one, children included: reads have no borders, so a part is queryable on its
    /// own even though it is only writable through its root.
    /// </remarks>
    public static bool IsCandidate(INamedTypeSymbol symbol) => AggregateShape.IsMappedEntity(symbol);

    /// <summary>
    ///     The read face's simple name: <c>OrderRead</c>, or <c>OuterOrderRead</c> for a nested entity.
    /// </summary>
    /// <remarks>
    ///     A read face is always emitted at the top level — nesting it would need the containing type to be
    ///     <c>partial</c>, which is not something declaring an entity should require. The containing names
    ///     are carried into the face's own name so two entities called <c>Post</c> under different parents
    ///     do not collide, and nobody types the result: <c>Post.Read</c> is the door.
    /// </remarks>
    public static string ReadName(INamedTypeSymbol entity)
    {
        var name = entity.Name + ReadSuffix;

        for (var parent = entity.ContainingType; parent is not null; parent = parent.ContainingType)
        {
            name = parent.Name + name;
        }

        return name;
    }

    /// <summary>The read face's fully-qualified name, ready to emit.</summary>
    public static string ReadFqn(INamedTypeSymbol entity) =>
        (entity.ContainingNamespace is null || entity.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + entity.ContainingNamespace.ToDisplayString() + ".") + ReadName(entity);

    /// <summary>
    ///     Describes the read face of <paramref name="entity" />, or null when it has no key to be found by.
    /// </summary>
    /// <remarks>
    ///     The ids it holds are recorded as CANDIDATE references and resolved later, by
    ///     <see cref="Resolve" />: whether <c>CustomerId</c> points anywhere depends on what else the
    ///     compilation declares, which is not knowable while walking one entity.
    /// </remarks>
    /// <param name="entity">The write model to read.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public static ReadShape? Describe(INamedTypeSymbol entity, CancellationToken cancellationToken = default)
    {
        var members = new List<ReadMember>();
        var references = new List<ReadReference>();
        var children = new List<ReadChildCollection>();
        var valueCollections = new List<ReadValueCollection>();

        foreach (var property in StoredProperties(entity))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (GeneratedModelShape.DescribeChild(entity, property) is { } child)
            {
                children.Add(new ReadChildCollection(
                    property.Name,
                    ReadFqn(child.ChildType),
                    child.ChildType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    entity.Name));
                continue;
            }

            if (IsIgnored(property) || IsCollectionOfEntities(property.Type))
            {
                continue;
            }

            // One column, not a set of flattened ones — see ReadValueCollection.
            if (GeneratedModelShape.DescribeValueCollection(entity, property) is { } values)
            {
                valueCollections.Add(new ReadValueCollection(
                    property.Name, TypeOf(values.Element), values.ValueObject is not null));
                continue;
            }

            // A value object is not a member of its own: it is the columns it maps to.
            if (AggregateShape.IsValueObjectType(property.Type))
            {
                Flatten(property.Type, property.Name, property.Name, property.Name, members, 0, cancellationToken);
                continue;
            }

            members.Add(new ReadMember(
                property.Name,
                property.Name,
                property.Name,
                TypeOf(property.Type),
                IsNullable(property.Type),
                IsReferenceType(property.Type)));

            if (property.Name.EndsWith("Id", StringComparison.Ordinal) && property.Name.Length > 2)
            {
                references.Add(new ReadReference(
                    property.Name,
                    property.Name.Substring(0, property.Name.Length - 2),
                    TypeOf(property.Type),
                    IsNullable(property.Type)));
            }
        }

        if (!AggregateShape.TryGetIdType(entity, out var idType) || idType is null)
        {
            return null;
        }

        AddFrameworkColumns(entity, members);

        return new ReadShape(
            ReadName(entity),
            ReadFqn(entity),
            entity.ContainingNamespace is null || entity.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : entity.ContainingNamespace.ToDisplayString(),
            entity.Name,
            entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            entity.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            entity.Name,
            TypeOf(idType),
            AggregateShape.IsAggregate(entity),
            new EquatableArray<ReadMember>([.. members]),
            new EquatableArray<ReadReference>([.. references]),
            new EquatableArray<ReadChildCollection>([.. children]),
            new EquatableArray<ReadValueCollection>([.. valueCollections]),
            SymbolLocation.From(entity));
    }

    /// <summary>What an entity offers the resolver: its read face and the key a reference must match.</summary>
    internal readonly struct ReadTarget(string readTypeName, string keyTypeName, string ownerTypeName)
    {
        /// <summary>The read face — <c>global::Shop.CustomerRead</c>.</summary>
        public string ReadTypeName { get; } = readTypeName;

        /// <summary>The aggregate's key type, which a reference's id must be.</summary>
        public string KeyTypeName { get; } = keyTypeName;

        /// <summary>The write type, so a self-reference can be told apart.</summary>
        public string OwnerTypeName { get; } = ownerTypeName;
    }

    /// <summary>Why a reference that looked like one produced no navigation.</summary>
    internal enum ReadReferenceProblem
    {
        /// <summary>It resolved, or it never looked like a reference at all.</summary>
        None,

        /// <summary>The name matches an aggregate, but the id is not that aggregate's key type.</summary>
        KeyTypeMismatch,

        /// <summary>The name matches more than one aggregate by suffix.</summary>
        Ambiguous,
    }

    /// <summary>
    ///     The navigation a <c>{X}Id</c> implies, or null with the reason.
    /// </summary>
    /// <remarks>
    ///     Two rules, both predictable. An exact match — <c>CustomerId</c> against an aggregate named
    ///     <c>Customer</c>. Or a suffix match — <c>ShippedByUserId</c> against <c>User</c> — which is what lets
    ///     one entity hold two references to the same aggregate without them colliding. The navigation is named
    ///     after the PROPERTY, never the target, for exactly that reason.
    /// </remarks>
    /// <param name="reference">The candidate recorded by <see cref="Describe" />.</param>
    /// <param name="owner">The fully-qualified write type the reference is declared on.</param>
    /// <param name="aggregates">Every aggregate in the compilation, by simple name.</param>
    /// <param name="problem">Why it did not resolve, when the shape says it should have.</param>
    public static ReadNavigation? Resolve(
        ReadReference reference,
        string owner,
        IReadOnlyDictionary<string, ReadTarget> aggregates,
        out ReadReferenceProblem problem)
    {
        problem = ReadReferenceProblem.None;

        if (aggregates.TryGetValue(reference.Target, out var exact))
        {
            return Accept(reference, owner, exact, ref problem);
        }

        // Suffix: ShippedByUserId -> User. Longest wins, and a tie is ambiguous rather than a guess.
        ReadTarget found = default;
        var matched = 0;
        var tied = false;

        foreach (var pair in aggregates)
        {
            if (pair.Key.Length >= reference.Target.Length ||
                !reference.Target.EndsWith(pair.Key, StringComparison.Ordinal))
            {
                continue;
            }

            if (pair.Key.Length == matched)
            {
                tied = true;
                continue;
            }

            if (pair.Key.Length < matched)
            {
                continue;
            }

            found = pair.Value;
            matched = pair.Key.Length;
            tied = false;
        }

        if (matched == 0)
        {
            return null;   // not a reference at all — an ordinary column
        }

        if (tied)
        {
            problem = ReadReferenceProblem.Ambiguous;
            return null;
        }

        return Accept(reference, owner, found, ref problem);
    }

    private static ReadNavigation? Accept(
        ReadReference reference, string owner, ReadTarget target, ref ReadReferenceProblem problem)
    {
        // An id of the declaring type itself is a self-reference: a column, and nothing more.
        if (string.Equals(target.OwnerTypeName, owner, StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.Equals(target.KeyTypeName, reference.IdTypeName, StringComparison.Ordinal))
        {
            problem = ReadReferenceProblem.KeyTypeMismatch;
            return null;
        }

        return new ReadNavigation(reference.Target, target.ReadTypeName, reference.Property, reference.Nullable);
    }

    private static ITypeSymbol Underlying(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    // Price_Amount, ShipTo_Country_Code — EF's own complex-property naming, which the write model uses.
    private static void Flatten(
        ITypeSymbol valueObject,
        string memberPrefix,
        string columnPrefix,
        string pathPrefix,
        List<ReadMember> members,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth >= AggregateShape.MaxValueObjectDepth || valueObject is not INamedTypeSymbol named)
        {
            return;
        }

        var stored = AggregateShape.StoredProperties(named).ToList();

        // A value object holding one value takes the owner's own column name rather than a compound one.
        if (stored.Count == 1 && !AggregateShape.IsValueObjectType(stored[0].Type))
        {
            members.Add(new ReadMember(
                memberPrefix,
                columnPrefix,
                pathPrefix + "." + stored[0].Name,
                TypeOf(stored[0].Type),
                IsNullable(valueObject),
                IsReferenceType(stored[0].Type)));
            return;
        }

        foreach (var property in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsIgnored(property))
            {
                continue;
            }

            var member = memberPrefix + property.Name;
            var column = columnPrefix + "_" + property.Name;
            var path = pathPrefix + "." + property.Name;

            if (AggregateShape.IsValueObjectType(property.Type))
            {
                Flatten(property.Type, member, column, path, members, depth + 1, cancellationToken);
                continue;
            }

            // A value object the owner declared nullable makes every column under it nullable.
            members.Add(new ReadMember(
                member,
                column,
                path,
                TypeOf(property.Type),
                IsNullable(property.Type) || IsNullable(valueObject),
                IsReferenceType(property.Type)));
        }
    }

    // Id and the timestamps for every entity; Version and DeletedAt only for a root, which is the only thing
    // that has them.
    private static void AddFrameworkColumns(INamedTypeSymbol entity, List<ReadMember> members)
    {
        if (AggregateShape.TryGetIdType(entity, out var idType) && idType is not null)
        {
            members.Insert(0, new ReadMember("Id", "Id", "Id", TypeOf(idType), false, IsReferenceType(idType)));
        }

        members.Add(new ReadMember("CreatedAt", "CreatedAt", "CreatedAt", "global::System.DateTime", false, false));
        members.Add(new ReadMember("UpdatedAt", "UpdatedAt", "UpdatedAt", "global::System.DateTime", false, false));

        if (!AggregateShape.IsAggregate(entity))
        {
            return;
        }

        members.Add(new ReadMember("Version", "Version", "Version", "int", false, false));
        members.Add(new ReadMember("DeletedAt", "DeletedAt", "DeletedAt", "global::System.DateTime", true, false));
    }

    // Derived first, so a property re-declared lower down hides the base one, and stopping at Rask's own
    // bases — their members are the framework columns, added above.
    private static IEnumerable<IPropertySymbol> StoredProperties(INamedTypeSymbol entity)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = entity; type is not null && !IsFrameworkBase(type); type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer ||
                    property.DeclaredAccessibility != Accessibility.Public ||
                    property.GetMethod is not { DeclaredAccessibility: Accessibility.Public } ||
                    !seen.Add(property.Name))
                {
                    continue;
                }

                // Computed — no setter and no backing field — so the write model does not map it either.
                // Collections are the exception: `IReadOnlyCollection<OrderLine> Lines => _lines` and
                // `IReadOnlyList<string> Tags => _tags` have neither, and both are stored state Rask maps
                // through the backing field.
                if (property.SetMethod is null && !HasBackingField(property) &&
                    !IsCollectionOfEntities(property.Type) &&
                    GeneratedModelShape.DescribeValueCollection(entity, property) is null)
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    private static bool IsFrameworkBase(INamedTypeSymbol type) =>
        type is { Name: "Entity" or "Aggregate", TypeArguments.Length: 1 } &&
        type.ContainingNamespace?.ToDisplayString() == "Rask.Data";

    private static bool HasBackingField(IPropertySymbol property) =>
        property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));

    private static bool IsIgnored(IPropertySymbol property) =>
        property.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == NotMappedAttribute);

    private static bool IsCollectionOfEntities(ITypeSymbol type) =>
        type.AllInterfaces.Any(static i =>
            i.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
            AggregateShape.IsEntity(i.TypeArguments[0]));

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static bool IsReferenceType(ITypeSymbol type) =>
        Underlying(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)).IsReferenceType;

    private static string TypeOf(ITypeSymbol type) =>
        Underlying(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated))
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}

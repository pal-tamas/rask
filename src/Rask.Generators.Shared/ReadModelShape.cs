using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators;

namespace Rask.Generators.Shared;

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
    public const string ReadSuffix = ReadFaceName.Suffix;

    private const string NotMappedAttribute = "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute";

    /// <summary>Whether a read face is generated for <paramref name="symbol" />.</summary>
    /// <remarks>
    /// Every mapped entity gets one, children included: reads have no borders, so a part is queryable on its
    /// own even though it is only writable through its root.
    /// </remarks>
    public static bool IsCandidate(INamedTypeSymbol symbol) => AggregateShape.IsMappedEntity(symbol);

    /// <summary>The read face's simple name: <c>OrderRead</c>, or <c>OuterOrderRead</c> for a nested entity.</summary>
    public static string ReadName(INamedTypeSymbol entity) => ReadFaceName.Of(entity);

    /// <summary>The read face's fully-qualified name, ready to emit.</summary>
    public static string ReadFqn(INamedTypeSymbol entity) => ReadFaceName.Fqn(entity);

    /// <summary>
    ///     Describes the read face of <paramref name="entity" />, or null when it has no key to be found by.
    /// </summary>
    /// <remarks>
    ///     The ids it holds are recorded as CANDIDATE references and resolved later, by
    ///     <see cref="TryResolve" />: whether <c>CustomerId</c> points anywhere depends on what else the
    ///     compilation declares, which is not knowable while walking one entity.
    /// </remarks>
    /// <param name="entity">The write model to read.</param>
    /// <param name="relationships">What its <c>Configure</c> declares — see <see cref="ConfiguredRelationships" />.</param>
    /// <param name="columns">The column patterns its <c>Configure</c> gives value objects — see <see cref="ConfiguredColumns" />.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    public static ReadShape? Describe(
        INamedTypeSymbol entity,
        IReadOnlyList<ReadRelationship> relationships,
        IReadOnlyList<ColumnPattern> columns,
        CancellationToken cancellationToken = default)
    {
        var members = new List<ReadMember>();
        var references = new List<ReadReference>();
        var children = new List<ReadChildCollection>();
        var valueCollections = new List<ReadValueCollection>();

        // A key of several columns stands where Id does, part by part: the generated {Aggregate}Id columns are
        // nowhere in this compilation's symbols, and a scalar part the entity declares is listed once, with them.
        var keyParts = CompositeKeyShape.HasCompositeKey(entity) ? CompositeKeyShape.ValidPartsOf(entity) : null;
        if (keyParts is { Count: 0 })
        {
            return null;
        }

        var partNames = new HashSet<string>(keyParts?.Select(static p => p.Name) ?? [], StringComparer.Ordinal);

        foreach (var property in StoredProperties(entity).Where(p => !partNames.Contains(p.Name)))
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

            if (!IsIgnored(property) && !IsCollectionOfEntities(property.Type))
            {
                var configured = columns.FirstOrDefault(c => string.Equals(c.Property, property.Name, StringComparison.Ordinal));

                // A value object kept whole as JSON (b.Json(…)) has no columns to flatten into: the face leaves it out.
                if (configured is { Json: true })
                {
                    continue;
                }

                AddMember(entity, property, configured?.Pattern, members, references, valueCollections, cancellationToken);
            }
        }

        if (!AggregateShape.TryGetIdType(entity, out var idType) || idType is null)
        {
            return null;
        }

        AddBelongsToKeys(entity, members);
        AddKey(entity, members, references, keyParts);
        AddFrameworkColumns(entity, members);
        AddTenantColumn(entity, members);
        return Shape(entity, idType, members, references, children, valueCollections, relationships);
    }

    // A collection of values, the columns of a value object (named after its pattern, when Configure gives one), or one
    // plain column.
    private static void AddMember(
        INamedTypeSymbol entity,
        IPropertySymbol property,
        string? pattern,
        List<ReadMember> members,
        List<ReadReference> references,
        List<ReadValueCollection> valueCollections,
        CancellationToken cancellationToken)
    {
        // A password is never read back (RASK175 refuses storing one), and a secret is stored but never shown: the face
        // leaves both out.
        if (AggregateShape.IsPassword(Underlying(property.Type)) || AggregateShape.IsSecret(property.Type))
        {
            return;
        }

        // One column, not a set of flattened ones — see ReadValueCollection.
        if (GeneratedModelShape.DescribeValueCollection(entity, property) is { } values)
        {
            // Its elements would carry a secret onto the face, and from there to a page.
            if (AggregateShape.HoldsSecret(values.Element))
            {
                return;
            }

            valueCollections.Add(new ReadValueCollection(
                property.Name, TypeOf(values.Element), values.ValueObject is not null));
            return;
        }

        // A value object is not a member of its own: it is the columns it maps to.
        if (AggregateShape.ValueObjectTypeOf(property.Type) is { } valueObject)
        {
            var before = members.Count;
            Flatten(property.Type, new Prefixes(property.Name, property.Name, property.Name, pattern), members, 0, cancellationToken);

            // One value is one column under the property's own name: it is shown as the property says, and shaped as
            // the value object declares — a base record's own `__Field`; one declared straight from Value has none.
            if (members.Count == before + 1 && AggregateShape.HoldsOneValue(valueObject))
            {
                var shown = Shown(members[before], property, AggregateShape.ValueBaseOf(valueObject) is "Text" or "Number" or "Integer" or "Day" or "Moment"
                    ? valueObject.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".__Field"
                    : null);

                // ProductId Product: whether ProductId is another entity's key is known only once every entity is.
                members[before] = shown with
                {
                    AsKey = shown with
                    {
                        Path = property.Name,
                        TypeName = TypeOf(property.Type),
                        IsReferenceType = IsReferenceType(property.Type),
                        Shape = FieldShapeHints.Annotated(property, null),
                    },
                };
            }

            return;
        }

        AddColumn(property, members, references);
    }

    // Id — or the parts of a key of several columns, first and in the declaration's order, which the runtime keys the
    // face by when it has no write model to read the key from. Each aggregate a part names is a reference like any
    // other {X}Id: the same rule gives it its navigation.
    private static void AddKey(
        INamedTypeSymbol entity, List<ReadMember> members, List<ReadReference> references, IReadOnlyList<KeyPart>? keyParts)
    {
        if (keyParts is null)
        {
            if (AggregateShape.TryGetIdType(entity, out var idType) && idType is not null)
            {
                members.Insert(0, new ReadMember("Id", "Id", "Id", TypeOf(idType), false, IsReferenceType(idType)));
            }

            return;
        }

        members.InsertRange(0, keyParts.Select(static p =>
            new ReadMember(p.Name, p.Name, p.Name, TypeOf(p.Type), false, IsReferenceType(p.Type))));
        references.InsertRange(0, keyParts
            .Where(static p => p.Aggregate is not null && AggregateShape.IsAggregate(p.Aggregate))
            .Select(static p => new ReadReference(p.Name, p.Aggregate!.Name, TypeOf(p.Type), false)));
    }

    // The key each BelongsTo generates, which no symbol here shows: a column of the target's key type, nullable as the
    // reference is. Its navigation is the declaration's, as a relationship (ConfiguredRelationships), never a guess.
    private static void AddBelongsToKeys(INamedTypeSymbol entity, List<ReadMember> members)
    {
        foreach (var reference in BelongsToShape.Of(entity))
        {
            var name = BelongsToShape.KeyName(reference);
            if (BelongsToShape.KeyTypeOf(reference) is not { } key || members.Exists(m => string.Equals(m.Name, name, StringComparison.Ordinal)))
            {
                continue;
            }

            members.Add(new ReadMember(name, name, name, TypeOf(key), BelongsToShape.IsOptional(reference), IsReferenceType(key)));
        }
    }

    // A primitive column, and — for a {X}Id — the candidate reference it might be.
    private static void AddColumn(IPropertySymbol property, List<ReadMember> members, List<ReadReference> references)
    {
        members.Add(Shown(
            new ReadMember(
                property.Name,
                property.Name,
                property.Name,
                TypeOf(property.Type),
                IsNullable(property.Type),
                IsReferenceType(property.Type)),
            property,
            null));

        if (Candidate(property.Name, TypeOf(property.Type), IsNullable(property.Type)) is { } reference)
        {
            references.Add(reference);
        }
    }

    // A {X}Id names the X it might point at; any other name points nowhere.
    private static ReadReference? Candidate(string name, string idTypeName, bool nullable) =>
        name.EndsWith("Id", StringComparison.Ordinal) && name.Length > 2
            ? new ReadReference(name, name.Substring(0, name.Length - 2), idTypeName, nullable)
            : null;

    /// <summary>
    ///     <paramref name="shape" /> with every column whose declared type is one of <paramref name="keyTypes" /> kept in
    ///     that type — <c>ProductId Product</c>, <c>ProductId? ProductId</c> — rather than the value it holds.
    /// </summary>
    /// <remarks>
    ///     The write model draws the same line: a type an entity is keyed by is a converted column wherever it appears,
    ///     never a value object. Kept typed, a <c>{X}Id</c> is a reference candidate like a plain <c>Guid</c> one, so its
    ///     navigation is inferred too. A one-value type no entity is keyed by stays the value it holds.
    /// </remarks>
    /// <param name="shape">The face as <see cref="Describe" /> left it.</param>
    /// <param name="keyTypes">The fully-qualified key type of every entity the faces are emitted for.</param>
    public static ReadShape KeepKeysTyped(ReadShape shape, ISet<string> keyTypes)
    {
        if (!shape.Members.Any(m => m.AsKey is { } key && keyTypes.Contains(key.TypeName)))
        {
            return shape;
        }

        // The candidates stay in the order their columns are declared, which is the order a name taken twice is settled by.
        var members = new List<ReadMember>(shape.Members.Count);
        var references = new List<ReadReference>(shape.References.Count + 1);
        var declared = shape.References.ToLookup(static r => r.Property, StringComparer.Ordinal);

        foreach (var member in shape.Members)
        {
            var kept = member.AsKey is { } key && keyTypes.Contains(key.TypeName) ? key : member;
            members.Add(kept);
            references.AddRange(declared[member.Name]);

            if (!ReferenceEquals(kept, member) && Candidate(kept.Name, kept.TypeName, kept.Nullable) is { } reference)
            {
                references.Add(reference);
            }
        }

        return shape with
        {
            Members = new EquatableArray<ReadMember>([.. members]),
            References = new EquatableArray<ReadReference>([.. references]),
        };
    }

    // What the property a member reads says about showing it: its attributes, copied, and its field shape.
    private static ReadMember Shown(ReadMember member, IPropertySymbol property, string? valueObjectShape) =>
        member with
        {
            Shown = FieldShapeHints.ShownAttributes(property) is { Length: > 0 } shown ? shown : null,
            Shape = FieldShapeHints.Annotated(property, valueObjectShape),
        };

    private static ReadShape Shape(
        INamedTypeSymbol entity,
        ITypeSymbol idType,
        List<ReadMember> members,
        List<ReadReference> references,
        List<ReadChildCollection> children,
        List<ReadValueCollection> valueCollections,
        IReadOnlyList<ReadRelationship> relationships) =>
        new(
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
            new EquatableArray<ReadRelationship>(relationships),
            SymbolLocation.From(entity));

    /// <summary>What an entity offers the resolver: its read face and the key a reference must match.</summary>
    internal readonly struct ReadTarget(string name, string readTypeName, string keyTypeName, string ownerTypeName)
    {
        /// <summary>The entity's simple name — <c>Customer</c>, what an id's name or a typed id's name says.</summary>
        public string Name { get; } = name;

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
    ///     The one entity a typed id is the key of — a <c>VirtualTankId</c> keys the <c>VirtualTank</c> child — which then
    ///     settles the reference whatever the property is called: a <c>FromVirtualTankId</c> never reaches the
    ///     <c>Tank</c> aggregate its name ends in.
    /// </summary>
    /// <remarks>
    ///     Only a type of the app's own: a <see cref="Guid" /> or an <c>int</c> keys many entities and says nothing, so
    ///     the name decides those. A typed id that keys several entities says nothing either.
    /// </remarks>
    /// <param name="reference">The candidate recorded by <see cref="Describe" />.</param>
    /// <param name="owner">The fully-qualified write type the reference is declared on.</param>
    /// <param name="aggregates">The aggregates, by simple name.</param>
    /// <param name="children">The child entities, by simple name.</param>
    /// <param name="navigation">The navigation, or null for the row's own id.</param>
    /// <returns>Whether the id's type is the key of exactly one entity.</returns>
    public static bool TryResolveByType(
        ReadReference reference,
        string owner,
        IReadOnlyDictionary<string, ReadTarget> aggregates,
        IReadOnlyDictionary<string, ReadTarget> children,
        out ReadNavigation? navigation)
    {
        navigation = null;
        if (!IsTypedId(reference.IdTypeName))
        {
            return false;
        }

        var keyed = aggregates.Values.Concat(children.Values)
            .Where(target => string.Equals(target.KeyTypeName, reference.IdTypeName, StringComparison.Ordinal))
            .Take(2)
            .ToList();
        if (keyed.Count != 1)
        {
            return false;
        }

        var target = keyed[0];
        navigation = IsOwnId(reference, owner, target)
            ? null
            : new ReadNavigation(reference.Target, target.ReadTypeName, reference.Property, reference.Nullable);
        return true;
    }

    /// <summary>
    ///     Whether a <c>{X}Id</c> names one of <paramref name="targets" /> by the one rule asked for, and if it
    ///     does, the navigation it implies — or null, with the reason when there is one.
    /// </summary>
    /// <remarks>
    ///     Two rules, both predictable. An exact match — <c>CustomerId</c> against an aggregate named
    ///     <c>Customer</c>. Or a suffix match — <c>ShippedByUserId</c> against <c>User</c> — which is what lets
    ///     one entity hold two references to the same aggregate without them colliding. The navigation is named
    ///     after the PROPERTY, never the target, for exactly that reason. The caller tries them in order, every
    ///     exact match before any suffix match, so a longer name that is someone's exact name is never taken
    ///     by a shorter one it happens to end in.
    /// </remarks>
    /// <param name="reference">The candidate recorded by <see cref="Describe" />.</param>
    /// <param name="owner">The fully-qualified write type the reference is declared on.</param>
    /// <param name="targets">The entities this attempt may point at, by simple name.</param>
    /// <param name="exactName">The exact rule when true, the suffix rule when false.</param>
    /// <param name="navigation">The navigation, when the match produced one.</param>
    /// <param name="problem">Why it did not resolve, when the shape says it should have.</param>
    /// <returns>Whether the name matched, which settles the reference either way.</returns>
    public static bool TryResolve(
        ReadReference reference,
        string owner,
        IReadOnlyDictionary<string, ReadTarget> targets,
        bool exactName,
        out ReadNavigation? navigation,
        out ReadReferenceProblem problem)
    {
        navigation = null;
        problem = ReadReferenceProblem.None;

        if (exactName)
        {
            if (!targets.TryGetValue(reference.Target, out var exact))
            {
                return false;
            }

            navigation = Accept(reference, owner, exact, exactName: true, ref problem);
            return true;
        }

        // Suffix: ShippedByUserId -> User. Longest wins, and a tie is ambiguous rather than a guess.
        ReadTarget found = default;
        var matched = 0;
        var tied = false;

        foreach (var pair in targets)
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
            return false;
        }

        if (tied)
        {
            problem = ReadReferenceProblem.Ambiguous;
            return true;
        }

        navigation = Accept(reference, owner, found, exactName: false, ref problem);
        return true;
    }

    private static ReadNavigation? Accept(
        ReadReference reference, string owner, ReadTarget target, bool exactName, ref ReadReferenceProblem problem)
    {
        // An id named exactly like the declaring type — Category.CategoryId — is that row's own id, a column and
        // nothing more. A longer name that ends in it — Supply.SumSupplyId — can only point at another row of the
        // same table, and gets its join like any other reference (EF aliases the second use of the table).
        if (exactName && string.Equals(target.OwnerTypeName, owner, StringComparison.Ordinal))
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

    private static bool IsOwnId(ReadReference reference, string owner, ReadTarget target) =>
        string.Equals(reference.Target, target.Name, StringComparison.Ordinal) &&
        string.Equals(target.OwnerTypeName, owner, StringComparison.Ordinal);

    // A type of the app's own — global::Fuel.VirtualTankId — rather than a keyword (int, string) or a System type (Guid).
    private static bool IsTypedId(string idTypeName) =>
        idTypeName.StartsWith("global::", StringComparison.Ordinal) &&
        !idTypeName.StartsWith("global::System.", StringComparison.Ordinal);

    private static ITypeSymbol Underlying(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    // Price_Amount, ShipTo_Country_Code — EF's own complex-property naming, which the write model uses — or, under a
    // pattern its Configure gives (`b.Columns(n => n.Km, "{Member}Km")`), StartKm and EndKm for the member and the column
    // alike; below the first level EF's naming again, as Rask.Data's Columns names them.
    private static void Flatten(
        ITypeSymbol valueObject,
        Prefixes prefixes,
        List<ReadMember> members,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth >= AggregateShape.MaxValueObjectDepth || AggregateShape.ValueObjectTypeOf(valueObject) is not { } named)
        {
            return;
        }

        var stored = AggregateShape.StoredProperties(named).ToList();

        // An optional value object makes every column under it nullable, however deep: EF Core reads a row that has
        // none as NULLs in all of them.
        var optional = prefixes.Optional || IsNullable(valueObject);

        // A value object holding one value takes the owner's own column name rather than a compound one.
        if (AggregateShape.HoldsOneValue(named))
        {
            members.Add(new ReadMember(
                prefixes.Member,
                prefixes.Column,
                prefixes.Path + "." + stored[0].Name,
                TypeOf(stored[0].Type),
                optional,
                IsReferenceType(stored[0].Type)));
            return;
        }

        foreach (var property in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A secret inside a value object is left out as one on the entity is.
            if (IsIgnored(property) || AggregateShape.HoldsSecret(property.Type))
            {
                continue;
            }

            var (member, column) = prefixes.Pattern is { } pattern
                ? Patterned(prefixes.Member, pattern, property.Name)
                : (prefixes.Member + property.Name, prefixes.Column + "_" + property.Name);
            var path = prefixes.Path + "." + property.Name;

            if (AggregateShape.ValueObjectTypeOf(property.Type) is not null)
            {
                Flatten(property.Type, new Prefixes(member, column, path, null, optional), members, depth + 1, cancellationToken);
                continue;
            }

            members.Add(new ReadMember(
                member,
                column,
                path,
                TypeOf(property.Type),
                IsNullable(property.Type) || optional,
                IsReferenceType(property.Type)));
        }
    }

    // StartKm for both, where the pattern makes a name C# can declare; otherwise the column only, beside KmStart.
    private static (string Member, string Column) Patterned(string memberPrefix, string pattern, string name)
    {
        var column = ConfiguredColumns.Apply(pattern, name);
        return (IsDeclarable(column) ? column : memberPrefix + name, column);
    }

    // A name C# takes as a member as it is: a valid identifier that is no reserved keyword (`class`, `in`), which only an
    // @ could declare. A contextual one (`value`, `record`) is an ordinary name there.
    private static bool IsDeclarable(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name)
        && Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;

    /// <summary>
    ///     What a value object's members are named under — the read face's, the column's, the write model's path, and the
    ///     pattern — and whether a value object holding them is optional.
    /// </summary>
    private readonly record struct Prefixes(string Member, string Column, string Path, string? Pattern, bool Optional = false);

    // The framework columns the class says: CreatedAt / UpdatedAt, Version, DeletedAt.
    private static void AddFrameworkColumns(INamedTypeSymbol entity, List<ReadMember> members)
    {
        // Only the columns the table has. A face that showed one the consts ruled out would compile —
        // OrderBy(d => d.CreatedAt) — and fail at the first query, on a column that was never there.
        if (AggregateShape.StampsCreated(entity))
        {
            members.Add(new ReadMember("CreatedAt", "CreatedAt", "CreatedAt", "global::System.DateTime", false, false));
        }

        if (AggregateShape.StampsUpdated(entity))
        {
            members.Add(new ReadMember("UpdatedAt", "UpdatedAt", "UpdatedAt", "global::System.DateTime", false, false));
        }

        if (AggregateShape.HasVersion(entity))
        {
            members.Add(new ReadMember("Version", "Version", "Version", "int", false, false));
        }

        if (AggregateShape.SoftDeletes(entity) || AggregateShape.SoftRemoves(entity))
        {
            members.Add(new ReadMember("DeletedAt", "DeletedAt", "DeletedAt", "global::System.DateTime", true, false));
        }
    }

    // A per-tenant ROOT's face reads its tenant, so a read under Tenant.Across() can tell whose row it is — in the
    // column's own type: the one the root declares, else its tenant's key, else Guid. A column only, never a
    // navigation, and never on a child, which belongs to its root's tenant. The face is init-only, so nothing writes it.
    private static void AddTenantColumn(INamedTypeSymbol entity, List<ReadMember> members)
    {
        if (!AggregateShape.IsAggregate(entity) || !AggregateShape.Has(entity, AggregateShape.PerTenant) ||
            AggregateShape.TenantParentOf(entity) is not null ||
            members.Exists(static m => string.Equals(m.Name, AggregateShape.TenantColumn, StringComparison.Ordinal)))
        {
            return;
        }

        var type = DeclaredTenantType(entity) ?? AggregateShape.TenantKeyOf(entity) ?? "global::System.Guid";
        members.Add(new ReadMember(
            AggregateShape.TenantColumn, AggregateShape.TenantColumn, AggregateShape.TenantColumn, type, true, false));
    }

    private static string? DeclaredTenantType(INamedTypeSymbol entity)
    {
        for (var type = entity; type is not null && !IsFrameworkBase(type); type = type.BaseType)
        {
            if (type.GetMembers(AggregateShape.TenantColumn).OfType<IPropertySymbol>().FirstOrDefault() is { } declared)
            {
                return TypeOf(declared.Type);
            }
        }

        return null;
    }

    // Derived first, so a property re-declared lower down hides the base one, and stopping at Rask's own
    // bases — their members are the framework columns, added above. A declared TenantId is the framework's
    // too: AddTenantColumn puts it on a per-tenant root's face, in the column's type, and nowhere else.
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
                    !seen.Add(property.Name) ||
                    string.Equals(property.Name, AggregateShape.TenantColumn, StringComparison.Ordinal))
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
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal);

    private static bool HasBackingField(IPropertySymbol property) =>
        property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));

    private static bool IsIgnored(IPropertySymbol property) =>
        property.GetAttributes().Any(static a => string.Equals(a.AttributeClass?.ToDisplayString(), NotMappedAttribute, StringComparison.Ordinal));

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

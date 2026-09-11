using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>How a type is encoded on the wire.</summary>
internal enum WireKind
{
    /// <summary>A value with a direct JSON representation — number, string, bool.</summary>
    Scalar,

    /// <summary>An enum, encoded as its underlying numeric value so a rename does not break the wire.</summary>
    Enum,

    /// <summary>A nullable value or reference type wrapping another shape.</summary>
    Nullable,

    /// <summary>A byte array, encoded as base64.</summary>
    Bytes,

    /// <summary>A sequence, encoded as a JSON array.</summary>
    Sequence,

    /// <summary>A string-keyed map, encoded as a JSON object.</summary>
    Dictionary,

    /// <summary>A composite with properties, encoded as a JSON object.</summary>
    Object,

    /// <summary>A <c>RemoteFile</c>, which travels as a multipart part rather than in the JSON.</summary>
    File,

    /// <summary>A type with no wire encoding. <see cref="WireType.Reason" /> says why.</summary>
    Unsupported,
}

/// <summary>How a sequence is rebuilt after its elements are read.</summary>
internal enum SequenceShape
{
    /// <summary>A <c>T[]</c>.</summary>
    Array,

    /// <summary>A concrete <c>List&lt;T&gt;</c>.</summary>
    List,

    /// <summary>An interface a <c>List&lt;T&gt;</c> satisfies.</summary>
    Interface,
}

/// <summary>One property of an <see cref="WireKind.Object" />, as it appears on the wire.</summary>
internal sealed class WireMember(string clrName, string wireName, WireType type, bool nullable = false)
{
    /// <summary>
    ///     Whether this property may be null on the wire, for a <em>reference</em> type.
    /// </summary>
    /// <remarks>
    ///     A nullable value type is already its own shape (<see cref="WireKind.Nullable" />); this
    ///     covers the reference case, which the classifier otherwise cannot see. The codec does not
    ///     need it — it writes JSON null for a null reference either way — but a consumer that
    ///     generates types from this model does: saying <c>string</c> where <c>string | null</c> can
    ///     arrive is a promise the wire does not keep.
    ///     <para>
    ///         Only an explicit <c>?</c> counts. In a project with nullable contexts switched off
    ///         every reference is technically nullable, and saying so would put <c>| null</c> on every
    ///         string in the file — noise that reads as a broken generator rather than as a warning.
    ///         Rask projects enable nullable contexts, and so do the scaffolded templates, so the
    ///         annotation is a reliable statement of what the author meant.
    ///     </para>
    /// </remarks>
    public bool Nullable { get; } = nullable;

    /// <summary>The C# property name, used to read the value off an instance.</summary>
    public string ClrName { get; } = clrName;

    /// <summary>The JSON property name — camelCase, or whatever <c>[JsonPropertyName]</c> pinned.</summary>
    public string WireName { get; } = wireName;

    /// <summary>The property's shape.</summary>
    public WireType Type { get; } = type;
}

/// <summary>The wire shape of one type, as a tree the emitter walks.</summary>
internal sealed class WireType
{
    /// <summary>What kind of encoding this type gets.</summary>
    public WireKind Kind { get; set; }

    /// <summary>The fully qualified type name, ready to emit.</summary>
    public string Fqn { get; set; } = string.Empty;

    /// <summary>For <see cref="WireKind.Scalar" />: the <c>WireJson</c> reader, or a reader expression.</summary>
    public string? ReadExpression { get; set; }

    /// <summary>For <see cref="WireKind.Scalar" />: how to write the value, with <c>{0}</c> for the value.</summary>
    public string? WriteExpression { get; set; }

    /// <summary>The wrapped shape: a nullable's underlying type, a sequence's element, a map's value.</summary>
    public WireType? Inner { get; set; }

    /// <summary>For <see cref="WireKind.Sequence" />: how to rebuild the collection.</summary>
    public SequenceShape Sequence { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the symbol, used to key generated codec methods. Null for a
    ///     shape with no symbol to hold — a generated model another generator emits, which this
    ///     compilation cannot see (see <see cref="GeneratedModelShape" />).
    /// </summary>
    public INamedTypeSymbol? Symbol { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" />: whether the type is a reference type, stated outright for a
    ///     shape that has no <see cref="Symbol" /> to ask. Null means "ask the symbol".
    /// </summary>
    public bool? IsReference { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the simple name a declaration of this shape takes, for a
    ///     shape that has no <see cref="Symbol" /> to take it from.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Whether a value of this shape can be null, and so needs null handling on both sides.</summary>
    public bool IsReferenceType => IsReference ?? Symbol?.IsReferenceType == true;

    /// <summary>For <see cref="WireKind.Object" />: the properties, in declaration order.</summary>
    public List<WireMember> Members { get; } = new();

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the constructor parameter names, in order, when the type is
    ///     built by constructor. Null when it is built with an object initializer.
    /// </summary>
    public List<string>? ConstructorParameters { get; set; }

    /// <summary>For <see cref="WireKind.Unsupported" />: what has no encoding, in the diagnostic's words.</summary>
    public string? Reason { get; set; }

    /// <summary>True when this shape, or anything inside it, carries a file.</summary>
    public bool ContainsFile =>
        Kind == WireKind.File
        || (Inner?.ContainsFile ?? false)
        || Members.Any(m => m.Type.ContainsFile);
}

/// <summary>
///     Decides how a contract type is encoded, or why it cannot be. This is the single place that
///     defines what a remote message is allowed to look like — RASK053 is just this walk, reported.
/// </summary>
internal static class WireShape
{
    private const string CqrsNamespace = "Rask.Cqrs";

    private static readonly SymbolDisplayFormat FqnFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.None);

    /// <summary>
    ///     Classifies <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <param name="allowFile">
    ///     Whether a <c>RemoteFile</c> is legal here. True only for a message's own top-level properties:
    ///     a file nested inside a list or a sub-object has no index the multipart body could address, so
    ///     it is rejected rather than silently dropped.
    /// </param>
    /// <param name="stack">The types currently being classified, so a cycle is caught rather than hung on.</param>
    /// <param name="compilation">
    ///     The compilation <paramref name="type" /> comes from. Given it, a <c>Rask.Data</c> entity's
    ///     generated <c>{Entity}Model</c> — which no other generator can see, because it is generated in
    ///     this same compilation — is classified from the entity instead of being reported as a type with no
    ///     way to build it. Without it, that model is Unsupported, exactly as before.
    /// </param>
    public static WireType Classify(
        ITypeSymbol type,
        bool allowFile,
        HashSet<ITypeSymbol>? stack = null,
        Compilation? compilation = null)
    {
        stack ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        // An unresolved `ProductModel?` binds as Nullable<ProductModel>, because the compiler cannot tell an
        // unknown type is a class. A generated model is one: this is the model itself, null-checked like any
        // reference. Taken first, or the branch below would wrap a class in Nullable<T>.
        if (compilation is not null && GeneratedModelShape.NullableUnresolvedModel(type, compilation) is { } model)
        {
            return Classify(model, allowFile, stack, compilation);
        }

        // A nullable value type is a wrapper first and its underlying shape second, so unwrap before
        // anything else looks at it.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var underlying = Classify(nullable.TypeArguments[0], false, stack, compilation);
            return underlying.Kind == WireKind.Unsupported
                ? underlying
                : new WireType
                {
                    Kind = WireKind.Nullable,
                    Fqn = Fqn(type, compilation),
                    Inner = underlying,
                };
        }

        if (Scalars.TryGetValue(type.SpecialType, out var special))
        {
            return Scalar(type, special.Read, special.Write);
        }

        var byName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (NamedScalars.TryGetValue(byName, out var named))
        {
            return Scalar(type, named.Read, named.Write);
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return new WireType
            {
                Kind = WireKind.Enum,
                Fqn = Fqn(type, compilation),
                ReadExpression = "global::Rask.Wire.WireJson.ReadInt64",
                WriteExpression = "writer.WriteNumberValue((long){0})",

                // Carried for the TypeScript emitter, which needs the member names to emit a real
                // enum. The codec ignores it: on the wire this is a number either way.
                Symbol = type as INamedTypeSymbol,
            };
        }

        if (IsRemoteFile(type))
        {
            return allowFile
                ? new WireType { Kind = WireKind.File, Fqn = Fqn(type, compilation) }
                : Unsupported(
                    type,
                    "a RaskFile is only allowed as a direct property of the message — nested inside a "
                    + "collection or another object there is no part of the multipart body that could carry it",
                    compilation);
        }

        if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
        {
            // Before the general array branch: bytes travel as base64, not as an array of numbers. The
            // difference is roughly 4x on the wire for any payload worth calling bytes.
            return new WireType
            {
                Kind = WireKind.Bytes,
                Fqn = Fqn(type, compilation),
                ReadExpression = "global::Rask.Wire.WireJson.ReadBytes",
                WriteExpression = "writer.WriteBase64StringValue({0})",
            };
        }

        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                return Unsupported(type, "only single-dimensional arrays have a JSON encoding", compilation);
            }

            var element = Classify(array.ElementType, false, stack, compilation);
            return element.Kind == WireKind.Unsupported
                ? element
                : new WireType
                {
                    Kind = WireKind.Sequence,
                    Sequence = SequenceShape.Array,
                    Fqn = Fqn(type, compilation),
                    Inner = element,
                };
        }

        if (type is not INamedTypeSymbol named2)
        {
            return Unsupported(type, "it is not a type a codec can be generated for", compilation);
        }

        // Before anything reads the type's own members: to this compilation a generated model is either
        // an error type with none, or the author's partial half with none of the generated ones.
        if (compilation is not null && GeneratedModelShape.EntitiesFor(named2, compilation) is { Count: > 0 } entities)
        {
            if (entities.Count == 1)
            {
                return ClassifyModel(named2, entities[0], stack, compilation);
            }

            // Otherwise the generic "no way to build it" below would be true, and no help at all: the type
            // exists in the real build, and the fix is one namespace away.
            return Unsupported(
                type,
                "it is the generated model of more than one entity ("
                + string.Join(", ", entities.Select(e => "'" + e.ToDisplayString() + "'"))
                + "), and a source generator cannot see which one the name binds to — write it with its "
                + "namespace, as '" + GeneratedModelShape.ModelFqn(entities[0]).Substring("global::".Length) + "'",
                compilation);
        }

        if (TryClassifyDictionary(named2, stack, compilation) is { } dictionary)
        {
            return dictionary;
        }

        if (TryClassifySequence(named2, stack, compilation) is { } sequence)
        {
            return sequence;
        }

        return ClassifyObject(named2, stack, membersMayCarryFiles: allowFile, compilation);
    }

    private static WireType? TryClassifyDictionary(INamedTypeSymbol type, HashSet<ITypeSymbol> stack, Compilation? compilation)
    {
        var definition = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!DictionaryDefinitions.Contains(definition) || type.TypeArguments.Length != 2)
        {
            return null;
        }

        // A JSON object's keys are strings. A dictionary keyed by anything else would need a key
        // encoding the two sides agree on, which is a bigger promise than it looks.
        if (type.TypeArguments[0].SpecialType != SpecialType.System_String)
        {
            return Unsupported(
                type,
                "a dictionary travels as a JSON object, whose keys are strings — key type "
                + $"'{type.TypeArguments[0].ToDisplayString()}' has no key encoding",
                compilation);
        }

        var value = Classify(type.TypeArguments[1], false, stack, compilation);
        return value.Kind == WireKind.Unsupported
            ? value
            : new WireType
            {
                Kind = WireKind.Dictionary,
                Fqn = Fqn(type, compilation),
                Inner = value,
            };
    }

    private static WireType? TryClassifySequence(INamedTypeSymbol type, HashSet<ITypeSymbol> stack, Compilation? compilation)
    {
        var definition = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!SequenceDefinitions.TryGetValue(definition, out var shape) || type.TypeArguments.Length != 1)
        {
            return null;
        }

        var element = Classify(type.TypeArguments[0], false, stack, compilation);
        return element.Kind == WireKind.Unsupported
            ? element
            : new WireType
            {
                Kind = WireKind.Sequence,
                Sequence = shape,
                Fqn = Fqn(type, compilation),
                Inner = element,
            };
    }

    // membersMayCarryFiles is true only for the message's own object. A file is addressed by its index
    // in the multipart body, and that index is written where the property sits in the JSON — so a file one
    // level down, inside a nested object or a list, has nowhere to be addressed from. Allowing it would
    // mean silently dropping the bytes.
    private static WireType ClassifyObject(
        INamedTypeSymbol type,
        HashSet<ITypeSymbol> stack,
        bool membersMayCarryFiles,
        Compilation? compilation)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            return Unsupported(
                type,
                "an interface names no single concrete type, so the receiver cannot know what to build — "
                + "use the concrete type",
                compilation);
        }

        if (type.IsAbstract)
        {
            return Unsupported(
                type,
                "an abstract type cannot be constructed by the receiver — use a concrete type, or model the "
                + "alternatives as separate messages",
                compilation);
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            return Unsupported(type, "'object' has no shape to encode — give the property its real type", compilation);
        }

        if (type.IsGenericType)
        {
            return Unsupported(type, "a generic type has no single wire shape — use a closed, concrete type", compilation);
        }

        if (type.IsRecord && type.TypeKind == TypeKind.Struct)
        {
            // Nothing wrong with it in principle; it just has not been exercised, and quietly emitting an
            // untested shape is worse than saying so.
            return Unsupported(type, "record structs are not supported as contract members yet", compilation);
        }

        // A cycle would make the emitter recurse forever, and it has no JSON encoding anyway: the value
        // is infinite.
        if (!stack.Add(type))
        {
            return Unsupported(
                type,
                "it refers back to itself, and a value that contains itself has no finite encoding",
                compilation);
        }

        try
        {
            var result = new WireType
            {
                Kind = WireKind.Object,
                Fqn = Fqn(type, compilation),
                Symbol = type,
            };

            var properties = type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p => p is
                {
                    IsStatic: false,
                    IsIndexer: false,
                    DeclaredAccessibility: Accessibility.Public,
                    GetMethod: not null,
                })
                .Where(p => p.Name != "EqualityContract")
                .ToList();

            var constructor = ChooseConstructor(type, properties);
            if (constructor is null)
            {
                return Unsupported(
                    type,
                    "the receiver has no way to build it: it needs either a public constructor whose "
                    + "parameters all match properties, or a public parameterless constructor with settable "
                    + "properties",
                    compilation);
            }

            if (constructor.Parameters.Length > 0)
            {
                result.ConstructorParameters = constructor.Parameters.Select(p => p.Name).ToList();
            }

            foreach (var property in properties)
            {
                // A get-only property that no constructor parameter feeds cannot be restored, so sending it
                // would be a lie: the receiver would drop it. Skip it rather than pretend.
                if (constructor.Parameters.Length > 0)
                {
                    if (!constructor.Parameters.Any(p =>
                            string.Equals(p.Name, property.Name, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }
                else if (property.SetMethod is null || property.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                // Only a directly file-typed property inherits the permission; anything else gets false,
                // which is what stops it flowing another level down.
                var memberAllowsFile = membersMayCarryFiles && IsRemoteFile(property.Type);
                var member = Classify(property.Type, memberAllowsFile, stack, compilation);
                if (member.Kind == WireKind.Unsupported)
                {
                    return MemberUnsupported(property, member, compilation);
                }

                result.Members.Add(new WireMember(
                    property.Name,
                    WireName(property),
                    member,
                    IsNullable(property.Type, compilation)));
            }

            return result;
        }
        finally
        {
            stack.Remove(type);
        }
    }

    /// <summary>
    ///     A <c>Rask.Data</c> entity's generated model, rebuilt from the entity because this compilation
    ///     cannot see it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The model is a mutable class with a public parameterless constructor and a settable property
    ///         per member, so it is always built with an object initializer. Value objects become the
    ///         nested models <c>{Model}.{ValueObject}Model</c>, which is the same shape again.
    ///     </para>
    ///     <para>
    ///         Wire names are the camelCase of the property name, and deliberately NOT read from the
    ///         entity's <c>[JsonPropertyName]</c>: the generated model copies only DataAnnotations onto its
    ///         properties, so the model a JSON serializer — or an ASP.NET endpoint — actually sees carries no
    ///         such attribute. Honouring the entity's pin here would make this codec disagree with every
    ///         other reader of the same model.
    ///     </para>
    ///     <para>
    ///         Classified with the ENTITY on the cycle stack, so an entity whose model reaches itself —
    ///         through a property typed as its own model — is reported rather than walked forever. An error
    ///         type cannot stand in for that: two unresolved references to the same name need not be one
    ///         symbol.
    ///     </para>
    /// </remarks>
    private static WireType ClassifyModel(
        INamedTypeSymbol modelType,
        INamedTypeSymbol entity,
        HashSet<ITypeSymbol> stack,
        Compilation compilation)
    {
        var modelFqn = GeneratedModelShape.ModelFqn(entity);

        if (!stack.Add(entity))
        {
            return new WireType
            {
                Kind = WireKind.Unsupported,
                Fqn = modelFqn,
                Reason = "it refers back to itself, and a value that contains itself has no finite encoding",
            };
        }

        try
        {
            var shape = GeneratedModelShape.Describe(entity);
            var result = new WireType
            {
                Kind = WireKind.Object,
                Fqn = modelFqn,
                Name = GeneratedModelShape.ModelName(entity),
                IsReference = true,
            };

            foreach (var member in shape.Members)
            {
                var wire = member.ValueObject is { } valueObject
                    ? ClassifyValueObjectModel(valueObject, modelFqn, stack, compilation)
                    : Classify(member.Property.Type, false, stack, compilation);

                if (wire.Kind == WireKind.Unsupported)
                {
                    return MemberUnsupported(member.Property, wire, compilation);
                }

                result.Members.Add(new WireMember(
                    member.Property.Name, CamelCase(member.Property.Name), wire, member.Nullable));
            }

            // The author's own partial half, when there is one, is part of the same object: its settable
            // properties travel too, named the ordinary way since they can carry their own pins.
            if (modelType.TypeKind != TypeKind.Error)
            {
                foreach (var property in modelType.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property is not
                        {
                            IsStatic: false,
                            IsIndexer: false,
                            DeclaredAccessibility: Accessibility.Public,
                            GetMethod: not null,
                            SetMethod.DeclaredAccessibility: Accessibility.Public,
                        } ||
                        result.Members.Any(m => m.ClrName == property.Name))
                    {
                        continue;
                    }

                    var wire = Classify(property.Type, false, stack, compilation);
                    if (wire.Kind == WireKind.Unsupported)
                    {
                        return MemberUnsupported(property, wire, compilation);
                    }

                    result.Members.Add(new WireMember(
                        property.Name,
                        WireName(property),
                        wire,
                        IsNullable(property.Type, compilation)));
                }
            }

            return result;
        }
        finally
        {
            stack.Remove(entity);
        }
    }

    // A value object's nested model. Every nested model is a direct member class of the entity's model,
    // however deep it sits, so its name is always the model's FQN plus its own name.
    private static WireType ClassifyValueObjectModel(
        ModelValueObject valueObject,
        string modelFqn,
        HashSet<ITypeSymbol> stack,
        Compilation compilation)
    {
        var result = new WireType
        {
            Kind = WireKind.Object,
            Fqn = modelFqn + "." + valueObject.ModelName,
            Name = valueObject.ModelName,
            IsReference = true,
        };

        foreach (var member in valueObject.Members)
        {
            var wire = member.ValueObject is { } nested
                ? ClassifyValueObjectModel(nested, modelFqn, stack, compilation)
                : Classify(member.Property.Type, false, stack, compilation);

            if (wire.Kind == WireKind.Unsupported)
            {
                return MemberUnsupported(member.Property, wire, compilation);
            }

            result.Members.Add(new WireMember(
                member.Property.Name, CamelCase(member.Property.Name), wire, member.Nullable));
        }

        return result;
    }

    private static WireType MemberUnsupported(IPropertySymbol property, WireType member, Compilation? compilation) =>
        new()
        {
            Kind = WireKind.Unsupported,
            Fqn = Fqn(property.Type, compilation),
            Reason = $"'{property.Name}' has type '{property.Type.ToDisplayString()}', which {member.Reason}",
        };

    // Prefer the constructor that feeds the most properties — for a record that is the positional one,
    // which is the shape contracts almost always take. A public parameterless constructor is the
    // fallback, paired with settable properties.
    private static IMethodSymbol? ChooseConstructor(INamedTypeSymbol type, List<IPropertySymbol> properties)
    {
        var candidates = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        var matching = candidates
            .Where(c => c.Parameters.Length > 0)
            .Where(c => c.Parameters.All(p => properties.Any(prop =>
                string.Equals(prop.Name, p.Name, System.StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        return matching ?? candidates.FirstOrDefault(c => c.Parameters.Length == 0);
    }

    // [JsonPropertyName] wins where it is present, so a contract that already pinned its wire names keeps
    // them. Otherwise camelCase, matching what every other JSON producer in the ecosystem defaults to.
    //
    // Public because the island generator names props with it too. A second implementation of "what is
    // this property called on the wire" would drift from this one, and the drift would land in the gap
    // between the C# that writes the JSON and the TypeScript that declares it — a runtime bug that
    // type-checks on both sides.
    public static string WireName(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "System.Text.Json.Serialization.JsonPropertyNameAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string pinned)
            {
                return pinned;
            }
        }

        return CamelCase(property.Name);
    }

    /// <summary>
    ///     Whether a property of <paramref name="type" /> is declared nullable (<c>?</c>) — including an
    ///     unresolved generated model written <c>ProductModel?</c>, which carries no annotation because it
    ///     bound as <c>Nullable&lt;T&gt;</c> (see <see cref="GeneratedModelShape.NullableUnresolvedModel" />).
    /// </summary>
    public static bool IsNullable(ITypeSymbol type, Compilation? compilation) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        (compilation is not null && GeneratedModelShape.NullableUnresolvedModel(type, compilation) is not null);

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);

    // The file type a MESSAGE declares is Rask.Core's RaskFile - the same one a file input hands a
    // component, on every host. Matched by name because a generator reads symbols: recognising it here
    // costs Rask.Cqrs no reference to Rask.Core, and keeps the mediator standalone.
    //
    // RemoteFile is not part of this. It is the wire-side carrier the transports pass around, and the
    // conversion between the two is emitted into the consumer's own compilation, which sees both.
    private static bool IsRemoteFile(ITypeSymbol type) =>
        type.Name == "RaskFile" && type.ContainingNamespace?.ToDisplayString() == "Rask.Core.Forms";

    private static WireType Scalar(ITypeSymbol type, string read, string write) => new()
    {
        Kind = WireKind.Scalar,
        Fqn = Fqn(type, null),
        ReadExpression = read,
        WriteExpression = write,
    };

    private static WireType Unsupported(ITypeSymbol type, string reason, Compilation? compilation) => new()
    {
        Kind = WireKind.Unsupported,
        Fqn = Fqn(type, compilation),
        Reason = reason,
    };

    // Through GeneratedModelShape so a generated model inside the type — List<ProductModel> — is named in
    // full. Its bare name would not bind from the generated codec's namespace.
    private static string Fqn(ITypeSymbol type, Compilation? compilation) =>
        GeneratedModelShape.DisplayName(type, FqnFormat, compilation);

    private static readonly Dictionary<SpecialType, (string Read, string Write)> Scalars = new()
    {
        [SpecialType.System_Boolean] = ("global::Rask.Wire.WireJson.ReadBoolean", "writer.WriteBooleanValue({0})"),
        [SpecialType.System_Byte] = ("global::Rask.Wire.WireJson.ReadByte", "writer.WriteNumberValue({0})"),
        [SpecialType.System_SByte] = ("global::Rask.Wire.WireJson.ReadSByte", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int16] = ("global::Rask.Wire.WireJson.ReadInt16", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt16] = ("global::Rask.Wire.WireJson.ReadUInt16", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int32] = ("global::Rask.Wire.WireJson.ReadInt32", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt32] = ("global::Rask.Wire.WireJson.ReadUInt32", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int64] = ("global::Rask.Wire.WireJson.ReadInt64", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt64] = ("global::Rask.Wire.WireJson.ReadUInt64", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Single] = ("global::Rask.Wire.WireJson.ReadSingle", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Double] = ("global::Rask.Wire.WireJson.ReadDouble", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Decimal] = ("global::Rask.Wire.WireJson.ReadDecimal", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Char] = ("global::Rask.Wire.WireJson.ReadChar", "global::Rask.Wire.WireJson.WriteCharValue(writer, {0})"),
        [SpecialType.System_String] = ("global::Rask.Wire.WireJson.ReadString", "writer.WriteStringValue({0})"),
        [SpecialType.System_DateTime] = ("global::Rask.Wire.WireJson.ReadDateTime", "writer.WriteStringValue({0})"),
    };

    private static readonly Dictionary<string, (string Read, string Write)> NamedScalars = new()
    {
        ["global::System.Guid"] = ("global::Rask.Wire.WireJson.ReadGuid", "writer.WriteStringValue({0})"),
        ["global::System.DateTimeOffset"] = ("global::Rask.Wire.WireJson.ReadDateTimeOffset", "writer.WriteStringValue({0})"),
        ["global::System.DateOnly"] = ("global::Rask.Wire.WireJson.ReadDateOnly", "global::Rask.Wire.WireJson.WriteDateOnlyValue(writer, {0})"),
        ["global::System.TimeOnly"] = ("global::Rask.Wire.WireJson.ReadTimeOnly", "global::Rask.Wire.WireJson.WriteTimeOnlyValue(writer, {0})"),
        ["global::System.TimeSpan"] = ("global::Rask.Wire.WireJson.ReadTimeSpan", "global::Rask.Wire.WireJson.WriteTimeSpanValue(writer, {0})"),
        ["global::System.Uri"] = ("global::Rask.Wire.WireJson.ReadUri", "global::Rask.Wire.WireJson.WriteUriValue(writer, {0})"),
    };

    private static readonly HashSet<string> DictionaryDefinitions = new()
    {
        "global::System.Collections.Generic.Dictionary<TKey, TValue>",
        "global::System.Collections.Generic.IDictionary<TKey, TValue>",
        "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
    };

    private static readonly Dictionary<string, SequenceShape> SequenceDefinitions = new()
    {
        ["global::System.Collections.Generic.List<T>"] = SequenceShape.List,
        ["global::System.Collections.Generic.IList<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.ICollection<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.IEnumerable<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.IReadOnlyList<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.IReadOnlyCollection<T>"] = SequenceShape.Interface,
    };
}

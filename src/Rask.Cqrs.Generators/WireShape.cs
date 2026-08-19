using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Cqrs.Generators;

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
internal sealed class WireMember(string clrName, string wireName, WireType type)
{
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

    /// <summary>For <see cref="WireKind.Object" />: the symbol, used to key generated codec methods.</summary>
    public INamedTypeSymbol? Symbol { get; set; }

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
    public static WireType Classify(ITypeSymbol type, bool allowFile, HashSet<ITypeSymbol>? stack = null)
    {
        stack ??= new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        // A nullable value type is a wrapper first and its underlying shape second, so unwrap before
        // anything else looks at it.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var underlying = Classify(nullable.TypeArguments[0], false, stack);
            return underlying.Kind == WireKind.Unsupported
                ? underlying
                : new WireType
                {
                    Kind = WireKind.Nullable,
                    Fqn = Fqn(type),
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
                Fqn = Fqn(type),
                ReadExpression = "global::Rask.Cqrs.WireJson.ReadInt64",
                WriteExpression = "writer.WriteNumberValue((long){0})",
            };
        }

        if (IsRemoteFile(type))
        {
            return allowFile
                ? new WireType { Kind = WireKind.File, Fqn = Fqn(type) }
                : Unsupported(
                    type,
                    "a RaskFile is only allowed as a direct property of the message — nested inside a "
                    + "collection or another object there is no part of the multipart body that could carry it");
        }

        if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
        {
            // Before the general array branch: bytes travel as base64, not as an array of numbers. The
            // difference is roughly 4x on the wire for any payload worth calling bytes.
            return new WireType
            {
                Kind = WireKind.Bytes,
                Fqn = Fqn(type),
                ReadExpression = "global::Rask.Cqrs.WireJson.ReadBytes",
                WriteExpression = "writer.WriteBase64StringValue({0})",
            };
        }

        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                return Unsupported(type, "only single-dimensional arrays have a JSON encoding");
            }

            var element = Classify(array.ElementType, false, stack);
            return element.Kind == WireKind.Unsupported
                ? element
                : new WireType
                {
                    Kind = WireKind.Sequence,
                    Sequence = SequenceShape.Array,
                    Fqn = Fqn(type),
                    Inner = element,
                };
        }

        if (type is not INamedTypeSymbol named2)
        {
            return Unsupported(type, "it is not a type a codec can be generated for");
        }

        if (TryClassifyDictionary(named2, stack) is { } dictionary)
        {
            return dictionary;
        }

        if (TryClassifySequence(named2, stack) is { } sequence)
        {
            return sequence;
        }

        return ClassifyObject(named2, stack, membersMayCarryFiles: allowFile);
    }

    private static WireType? TryClassifyDictionary(INamedTypeSymbol type, HashSet<ITypeSymbol> stack)
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
                + $"'{type.TypeArguments[0].ToDisplayString()}' has no key encoding");
        }

        var value = Classify(type.TypeArguments[1], false, stack);
        return value.Kind == WireKind.Unsupported
            ? value
            : new WireType
            {
                Kind = WireKind.Dictionary,
                Fqn = Fqn(type),
                Inner = value,
            };
    }

    private static WireType? TryClassifySequence(INamedTypeSymbol type, HashSet<ITypeSymbol> stack)
    {
        var definition = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!SequenceDefinitions.TryGetValue(definition, out var shape) || type.TypeArguments.Length != 1)
        {
            return null;
        }

        var element = Classify(type.TypeArguments[0], false, stack);
        return element.Kind == WireKind.Unsupported
            ? element
            : new WireType
            {
                Kind = WireKind.Sequence,
                Sequence = shape,
                Fqn = Fqn(type),
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
        bool membersMayCarryFiles)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            return Unsupported(
                type,
                "an interface names no single concrete type, so the receiver cannot know what to build — "
                + "use the concrete type");
        }

        if (type.IsAbstract)
        {
            return Unsupported(
                type,
                "an abstract type cannot be constructed by the receiver — use a concrete type, or model the "
                + "alternatives as separate messages");
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            return Unsupported(type, "'object' has no shape to encode — give the property its real type");
        }

        if (type.IsGenericType)
        {
            return Unsupported(type, "a generic type has no single wire shape — use a closed, concrete type");
        }

        if (type.IsRecord && type.TypeKind == TypeKind.Struct)
        {
            // Nothing wrong with it in principle; it just has not been exercised, and quietly emitting an
            // untested shape is worse than saying so.
            return Unsupported(type, "record structs are not supported as contract members yet");
        }

        // A cycle would make the emitter recurse forever, and it has no JSON encoding anyway: the value
        // is infinite.
        if (!stack.Add(type))
        {
            return Unsupported(
                type,
                "it refers back to itself, and a value that contains itself has no finite encoding");
        }

        try
        {
            var result = new WireType
            {
                Kind = WireKind.Object,
                Fqn = Fqn(type),
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
                    + "properties");
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
                var member = Classify(property.Type, memberAllowsFile, stack);
                if (member.Kind == WireKind.Unsupported)
                {
                    return new WireType
                    {
                        Kind = WireKind.Unsupported,
                        Fqn = Fqn(property.Type),
                        Reason = $"'{property.Name}' has type '{property.Type.ToDisplayString()}', which {member.Reason}",
                    };
                }

                result.Members.Add(new WireMember(property.Name, WireName(property), member));
            }

            return result;
        }
        finally
        {
            stack.Remove(type);
        }
    }

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
    private static string WireName(IPropertySymbol property)
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

        var name = property.Name;
        if (name.Length == 0 || char.IsLower(name[0]))
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

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
        Fqn = Fqn(type),
        ReadExpression = read,
        WriteExpression = write,
    };

    private static WireType Unsupported(ITypeSymbol type, string reason) => new()
    {
        Kind = WireKind.Unsupported,
        Fqn = Fqn(type),
        Reason = reason,
    };

    private static string Fqn(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.None));

    private static readonly Dictionary<SpecialType, (string Read, string Write)> Scalars = new()
    {
        [SpecialType.System_Boolean] = ("global::Rask.Cqrs.WireJson.ReadBoolean", "writer.WriteBooleanValue({0})"),
        [SpecialType.System_Byte] = ("global::Rask.Cqrs.WireJson.ReadByte", "writer.WriteNumberValue({0})"),
        [SpecialType.System_SByte] = ("global::Rask.Cqrs.WireJson.ReadSByte", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int16] = ("global::Rask.Cqrs.WireJson.ReadInt16", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt16] = ("global::Rask.Cqrs.WireJson.ReadUInt16", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int32] = ("global::Rask.Cqrs.WireJson.ReadInt32", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt32] = ("global::Rask.Cqrs.WireJson.ReadUInt32", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int64] = ("global::Rask.Cqrs.WireJson.ReadInt64", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt64] = ("global::Rask.Cqrs.WireJson.ReadUInt64", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Single] = ("global::Rask.Cqrs.WireJson.ReadSingle", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Double] = ("global::Rask.Cqrs.WireJson.ReadDouble", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Decimal] = ("global::Rask.Cqrs.WireJson.ReadDecimal", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Char] = ("global::Rask.Cqrs.WireJson.ReadChar", "global::Rask.Cqrs.WireJson.WriteCharValue(writer, {0})"),
        [SpecialType.System_String] = ("global::Rask.Cqrs.WireJson.ReadString", "writer.WriteStringValue({0})"),
        [SpecialType.System_DateTime] = ("global::Rask.Cqrs.WireJson.ReadDateTime", "writer.WriteStringValue({0})"),
    };

    private static readonly Dictionary<string, (string Read, string Write)> NamedScalars = new()
    {
        ["global::System.Guid"] = ("global::Rask.Cqrs.WireJson.ReadGuid", "writer.WriteStringValue({0})"),
        ["global::System.DateTimeOffset"] = ("global::Rask.Cqrs.WireJson.ReadDateTimeOffset", "writer.WriteStringValue({0})"),
        ["global::System.DateOnly"] = ("global::Rask.Cqrs.WireJson.ReadDateOnly", "global::Rask.Cqrs.WireJson.WriteDateOnlyValue(writer, {0})"),
        ["global::System.TimeOnly"] = ("global::Rask.Cqrs.WireJson.ReadTimeOnly", "global::Rask.Cqrs.WireJson.WriteTimeOnlyValue(writer, {0})"),
        ["global::System.TimeSpan"] = ("global::Rask.Cqrs.WireJson.ReadTimeSpan", "global::Rask.Cqrs.WireJson.WriteTimeSpanValue(writer, {0})"),
        ["global::System.Uri"] = ("global::Rask.Cqrs.WireJson.ReadUri", "global::Rask.Cqrs.WireJson.WriteUriValue(writer, {0})"),
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

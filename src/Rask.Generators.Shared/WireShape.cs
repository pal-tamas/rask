using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     Decides how a contract type is encoded, or why it cannot be. This is the single place that
///     defines what a remote message is allowed to look like — RASK053 is just this walk, reported.
/// </summary>
internal static class WireShape
{
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

        if (TryClassifyLeaf(type, allowFile, compilation) is { } leaf)
        {
            return leaf;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ClassifyArray(array, stack, compilation);
        }

        if (type is not INamedTypeSymbol named2)
        {
            return Unsupported(type, "it is not a type a codec can be generated for — use a class, record, primitive, enum or collection of them", compilation);
        }

        // Before anything reads the type's own members: to this compilation a generated model is either
        // an error type with none, or the author's partial half with none of the generated ones.
        if (compilation is not null && GeneratedModelShape.EntitiesFor(named2, compilation) is { Count: > 0 } entities)
        {
            return ClassifyGeneratedModel(named2, entities, stack, compilation);
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

    // The shapes that need no walk: scalars, enums, a message's file, and bytes.
    private static WireType? TryClassifyLeaf(ITypeSymbol type, bool allowFile, Compilation? compilation)
    {
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
                    "an IRaskFile is only allowed as a direct property of the message — nested inside a "
                    + "collection or another object there is no part of the multipart body that could carry it — make it a direct property of the message",
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

        return null;
    }

    private static WireType ClassifyArray(IArrayTypeSymbol array, HashSet<ITypeSymbol> stack, Compilation? compilation)
    {
        if (array.Rank != 1)
        {
            return Unsupported(array, "only single-dimensional arrays have a JSON encoding — use a jagged array or a list of lists", compilation);
        }

        var element = Classify(array.ElementType, false, stack, compilation);
        return element.Kind == WireKind.Unsupported
            ? element
            : new WireType
            {
                Kind = WireKind.Sequence,
                Sequence = SequenceShape.Array,
                Fqn = Fqn(array, compilation),
                Inner = element,
            };
    }

    private static WireType ClassifyGeneratedModel(
        INamedTypeSymbol type,
        IReadOnlyList<INamedTypeSymbol> entities,
        HashSet<ITypeSymbol> stack,
        Compilation compilation)
    {
        if (entities.Count == 1)
        {
            return ClassifyModel(type, entities[0], stack, compilation);
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
                + $"'{type.TypeArguments[0].ToDisplayString()}' has no key encoding — key it by string",
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
        if (ObjectRejection(type) is { } reason)
        {
            return Unsupported(type, reason, compilation);
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
            return DescribeObject(type, stack, membersMayCarryFiles, compilation);
        }
        finally
        {
            stack.Remove(type);
        }
    }

    // Why an object of this type cannot travel at all, before any of its members is looked at.
    private static string? ObjectRejection(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            return "an interface names no single concrete type, so the receiver cannot know what to build — "
                   + "use the concrete type";
        }

        if (type.IsAbstract)
        {
            return "an abstract type cannot be constructed by the receiver — use a concrete type, or model the "
                   + "alternatives as separate messages";
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            return "'object' has no shape to encode — give the property its real type";
        }

        if (type.IsGenericType)
        {
            return "a generic type has no single wire shape — use a closed, concrete type";
        }

        // Nothing wrong with a record struct in principle; it just has not been exercised, and quietly emitting
        // an untested shape is worse than saying so.
        return type.IsRecord && type.TypeKind == TypeKind.Struct
            ? "record structs are not supported as contract members yet — use a record class"
            : null;
    }

    private static WireType DescribeObject(
        INamedTypeSymbol type,
        HashSet<ITypeSymbol> stack,
        bool membersMayCarryFiles,
        Compilation? compilation)
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
            .Where(p => !string.Equals(p.Name, "EqualityContract", StringComparison.Ordinal))
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

        foreach (var property in properties.Where(p => IsRestorable(p, constructor)))
        {
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

    // A get-only property that no constructor parameter feeds cannot be restored, so sending it would be a
    // lie: the receiver would drop it. It is skipped rather than pretended.
    private static bool IsRestorable(IPropertySymbol property, IMethodSymbol constructor) =>
        constructor.Parameters.Length > 0
            ? constructor.Parameters.Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase))
            : property.SetMethod is { DeclaredAccessibility: Accessibility.Public };

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
                var wire = ClassifyModelMember(member.Property.Type, member.ValueObject, modelFqn, stack, compilation);

                if (wire.Kind == WireKind.Unsupported)
                {
                    return MemberUnsupported(member.Property, wire, compilation);
                }

                // Every property of a generated model is nullable — a null is a value the model does not give.
                result.Members.Add(new WireMember(
                    member.Property.Name, Identifiers.CamelCase(member.Property.Name), wire, true));
            }

            // The author's own partial half, when there is one, is part of the same object.
            return modelType.TypeKind != TypeKind.Error
                ? AddAuthorsHalf(modelType, result, stack, compilation)
                : result;
        }
        finally
        {
            stack.Remove(entity);
        }
    }

    // The settable properties of the author's partial half travel too, named the ordinary way since they can
    // carry their own pins. Hands back the model, or the first member that cannot travel.
    private static WireType AddAuthorsHalf(
        INamedTypeSymbol modelType,
        WireType result,
        HashSet<ITypeSymbol> stack,
        Compilation compilation)
    {
        var settable = modelType.GetMembers().OfType<IPropertySymbol>()
            .Where(property => property is
            {
                IsStatic: false,
                IsIndexer: false,
                DeclaredAccessibility: Accessibility.Public,
                GetMethod: not null,
                SetMethod.DeclaredAccessibility: Accessibility.Public,
            });

        foreach (var property in settable)
        {
            if (result.Members.Any(m => string.Equals(m.ClrName, property.Name, StringComparison.Ordinal)))
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

        return result;
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
            var wire = ClassifyModelMember(member.Property.Type, member.ValueObject, modelFqn, stack, compilation);

            if (wire.Kind == WireKind.Unsupported)
            {
                return MemberUnsupported(member.Property, wire, compilation);
            }

            result.Members.Add(new WireMember(
                member.Property.Name, Identifiers.CamelCase(member.Property.Name), wire, true));
        }

        return result;
    }

    // A member of a generated model, as the model declares it: nullable, a one-value value object as its value, any
    // other value object as its nested model.
    private static WireType ClassifyModelMember(
        ITypeSymbol entityType,
        ModelValueObject? valueObject,
        string modelFqn,
        HashSet<ITypeSymbol> stack,
        Compilation compilation)
    {
        if (valueObject is { SingleValue: true } single)
        {
            return Classify(AsNullable(single.Members[0].Property.Type, compilation), false, stack, compilation);
        }

        return valueObject is { } nested
            ? ClassifyValueObjectModel(nested, modelFqn, stack, compilation)
            : Classify(AsNullable(entityType, compilation), false, stack, compilation);
    }

    // T? for a value type (Nullable<T>), an annotated reference otherwise — the type the model property is declared with.
    private static ITypeSymbol AsNullable(ITypeSymbol type, Compilation compilation)
    {
        if (!type.IsValueType)
        {
            return type.WithNullableAnnotation(NullableAnnotation.Annotated);
        }

        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? type
            : compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(type);
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
                string.Equals(prop.Name, p.Name, StringComparison.OrdinalIgnoreCase))))
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
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    "System.Text.Json.Serialization.JsonPropertyNameAttribute",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string pinned)
            {
                return pinned;
            }
        }

        return Identifiers.CamelCase(property.Name);
    }

    /// <summary>
    ///     Whether a property of <paramref name="type" /> is declared nullable (<c>?</c>) — including an
    ///     unresolved generated model written <c>ProductModel?</c>, which carries no annotation because it
    ///     bound as <c>Nullable&lt;T&gt;</c> (see <see cref="GeneratedModelShape.NullableUnresolvedModel" />).
    /// </summary>
    public static bool IsNullable(ITypeSymbol type, Compilation? compilation) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        (compilation is not null && GeneratedModelShape.NullableUnresolvedModel(type, compilation) is not null);

    // The file type a MESSAGE declares is Rask.Core's IRaskFile - the same one a file input hands a
    // component, on every host. Matched by name because a generator reads symbols: recognising it here
    // costs Rask.Cqrs no reference to Rask.Core, and keeps the mediator standalone.
    //
    // RemoteFile is not part of this. It is the wire-side carrier the transports pass around, and the
    // conversion between the two is emitted into the consumer's own compilation, which sees both.
    private static bool IsRemoteFile(ITypeSymbol type) =>
        string.Equals(type.Name, "IRaskFile", StringComparison.Ordinal) &&
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Core.Forms", StringComparison.Ordinal);

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

    private static readonly Dictionary<string, (string Read, string Write)> NamedScalars = new(StringComparer.Ordinal)
    {
        ["global::System.Guid"] = ("global::Rask.Wire.WireJson.ReadGuid", "writer.WriteStringValue({0})"),
        ["global::System.DateTimeOffset"] = ("global::Rask.Wire.WireJson.ReadDateTimeOffset", "writer.WriteStringValue({0})"),
        ["global::System.DateOnly"] = ("global::Rask.Wire.WireJson.ReadDateOnly", "global::Rask.Wire.WireJson.WriteDateOnlyValue(writer, {0})"),
        ["global::System.TimeOnly"] = ("global::Rask.Wire.WireJson.ReadTimeOnly", "global::Rask.Wire.WireJson.WriteTimeOnlyValue(writer, {0})"),
        ["global::System.TimeSpan"] = ("global::Rask.Wire.WireJson.ReadTimeSpan", "global::Rask.Wire.WireJson.WriteTimeSpanValue(writer, {0})"),
        ["global::System.Uri"] = ("global::Rask.Wire.WireJson.ReadUri", "global::Rask.Wire.WireJson.WriteUriValue(writer, {0})"),
    };

    private static readonly HashSet<string> DictionaryDefinitions = new(StringComparer.Ordinal)
    {
        "global::System.Collections.Generic.Dictionary<TKey, TValue>",
        "global::System.Collections.Generic.IDictionary<TKey, TValue>",
        "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
    };

    private static readonly Dictionary<string, SequenceShape> SequenceDefinitions = new(StringComparer.Ordinal)
    {
        ["global::System.Collections.Generic.List<T>"] = SequenceShape.List,
        ["global::System.Collections.Generic.IList<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.ICollection<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.IEnumerable<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.IReadOnlyList<T>"] = SequenceShape.Interface,
        ["global::System.Collections.Generic.IReadOnlyCollection<T>"] = SequenceShape.Interface,
    };
}

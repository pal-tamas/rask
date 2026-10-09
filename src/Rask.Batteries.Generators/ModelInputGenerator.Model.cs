using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

public sealed partial class ModelInputGenerator
{
    private static Entity? GetEntity(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.Node is not ClassDeclarationSyntax declaration ||
            ctx.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // The same entities the registry maps — see ModelRegistryGenerator.GetCandidate for why abstract,
        // static and generic classes are not ones. Which ones get a model, and what it carries, is decided
        // in GeneratedModelShape, because other generators have to reconstruct this model without seeing it.
        if (!GeneratedModelShape.IsCandidate(symbol))
        {
            return null;
        }

        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var location = SymbolLocation.From(symbol);

        if (symbol.ContainingType is { } outer)
        {
            return Entity.Refused(name, symbol.Name, location, Refusal.Nested, outer.ToDisplayString());
        }

        if (GeneratedModelShape.ClashingType(symbol) is { } existing)
        {
            return Entity.Refused(name, symbol.Name, location, Refusal.Clash, existing.ToDisplayString());
        }

        return Accepted(symbol, name, location, cancellationToken);
    }

    private static Entity Accepted(
        INamedTypeSymbol symbol, string name, SymbolLocation? location, CancellationToken cancellationToken)
    {
        var shape = GeneratedModelShape.Describe(symbol, cancellationToken);
        var converted = new Dictionary<ModelValueObject, ValueObjectShape>();
        var constructible = symbol.InstanceConstructors.Any(static c => c.Parameters.Length == 0);
        var keyWrite = constructible && shape.Key is { } key && GeneratedModelShape.WriteKindOf(key) is { } kind
            ? WriteOf(key, kind, "__" + kind + "Key_" + key.Name, byRef: false)
            : null;
        var (keySource, keyFactory) = KeySourceOf(shape.IdType);

        return new Entity(
            name,
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            shape.IdType?.ToDisplayString(TypeFormat),
            constructible,
            shape.Versioned,
            keyWrite,
            keySource,
            keyFactory,
            shape.IdType?.IsReferenceType ?? false,
            location,
            Refusal.None,
            null,
            new EquatableArray<Member>(shape.Members.Select((m, index) => ToMember(m, index, converted))),
            new EquatableArray<ValueObjectShape>(shape.ValueObjects.Select(v => ToShape(v, converted))),
            GeneratedModelShape.IsChildEntity(symbol),
            new EquatableArray<ChildShape>(shape.Children
                .Where(static c => c.Access != ModelChildAccess.None)
                .Select(ToChild)),
            new EquatableArray<string>(AggregateReferencesOf(symbol)),
            new EquatableArray<string>(shape.Children
                .Where(static c => c.Access == ModelChildAccess.None)
                .Select(static c => c.Name)),
            new EquatableArray<ValueCollectionShape>(
                shape.ValueCollections.Select(c => ToValueCollection(c, converted))),
            WritesOf(symbol, out var declaresWrites),
            declaresWrites,
            DeletableOf(symbol));
    }

    /// <summary>
    ///     False when the aggregate declares <c>public const Deletion Deletes = Deletion.None</c> — a row that is
    ///     corrected by a new one or retired by a state change, never removed, and so gets no <c>Delete</c>.
    /// </summary>
    private static bool DeletableOf(INamedTypeSymbol symbol) =>
        AggregateShape.ConstOf(symbol, "Deletes", "Deletion") != NoDeletion;

    /// <summary>
    ///     What <c>public const ModelWrites Writes</c> says, or <see cref="AllWrites" /> when the aggregate
    ///     does not say — so an entity that declares nothing is unchanged.
    /// </summary>
    /// <remarks>
    ///     A const rather than an attribute or a static property, because C# refuses a non-constant
    ///     initializer: the value is always there to be read, so this can never quietly find nothing and
    ///     emit the whole surface anyway. Carried out as an int, since an ISymbol must not cross an
    ///     incremental-generator step.
    /// </remarks>
    private static int WritesOf(INamedTypeSymbol symbol, out bool declared)
    {
        foreach (var field in symbol.GetMembers("Writes").OfType<IFieldSymbol>())
        {
            if (field is { IsConst: true, ConstantValue: int value } &&
                field.Type is { Name: "ModelWrites", ContainingNamespace: { Name: "Data", ContainingNamespace: { Name: "Rask", ContainingNamespace.IsGlobalNamespace: true } } })
            {
                declared = true;
                return value;
            }
        }

        declared = false;
        return AllWrites;
    }

    private static ValueCollectionShape ToValueCollection(
        ModelValueCollection collection, Dictionary<ModelValueObject, ValueObjectShape> converted) => new(
        collection.Name,
        collection.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        collection.ElementModel is null ? null : ToShape(collection.ElementModel, converted),
        collection.Field is not null,
        collection.Field?.Name,
        collection.Field?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        ?? collection.Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    private static ChildShape ToChild(ModelChild child) => new(
        child.Name,
        child.ChildType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        GeneratedModelShape.ModelFqn(child.ChildType),
        GeneratedModelShape.TryGetIdType(child.ChildType, out var idType) && idType is not null
            ? idType.ToDisplayString(TypeFormat)
            : "global::System.Guid",
        child.Access == ModelChildAccess.Field,
        child.Field?.Name,
        child.Field?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        ?? child.Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    /// <summary>
    ///     The properties that reach another AGGREGATE — one of them, or a collection of them. This is what
    ///     RASK087 refuses, and it is the whole write-side border: an aggregate may hold another's id and
    ///     nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Found here rather than in the shape because it is the one thing the shape deliberately does not
    ///         describe: these are not children, so nothing is generated for them and there is nothing to carry.
    ///     </para>
    ///     <para>
    ///         Each entry is <c>property|target|idType|kind</c>, where kind is <c>one</c> or <c>many</c> — the two
    ///         want different fixes, on opposite sides of the relationship — and the id type is what the
    ///         replacement property would be declared as.
    ///     </para>
    /// </remarks>
    private static IEnumerable<string> AggregateReferencesOf(INamedTypeSymbol symbol)
    {
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer ||
                property.DeclaredAccessibility != Accessibility.Public ||
                property.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
            {
                continue;
            }

            // One of them: a navigation EF would map, which is the traversal the border exists to stop.
            if (property.Type is INamedTypeSymbol single && AggregateShape.IsAggregate(single))
            {
                yield return Reference(property.Name, single, "one");
                continue;
            }

            foreach (var contract in property.Type.AllInterfaces.Concat(
                         property.Type is INamedTypeSymbol named ? [named] : Array.Empty<INamedTypeSymbol>()))
            {
                if (contract.ConstructedFrom.SpecialType != SpecialType.System_Collections_Generic_IEnumerable_T ||
                    contract.TypeArguments[0] is not INamedTypeSymbol element ||
                    !AggregateShape.IsAggregate(element))
                {
                    continue;
                }

                yield return Reference(property.Name, element, "many");
                break;
            }
        }

        static string Reference(string property, INamedTypeSymbol target, string kind) =>
            property + "|" + target.Name + "|"
            + (AggregateShape.TryGetIdType(target, out var id) && id is not null
                ? id.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                : "Guid")
            + "|" + kind;
    }

    // Who can produce the key of a row inserted WITHOUT one — which decides whether Create(model) exists.
    //  * A Guid: this code, as a version-7 Guid (time-ordered, so the primary-key index stays append-only), and
    //    the same inside a strongly-typed id over a Guid.
    //  * An integer: the store's identity column. Rask.Data's key convention leaves integer keys store-generated
    //    and marks every other Entity<TId> key never-generated, so EF produces nothing else.
    //  * Anything else — a string, a strongly-typed id over an integer or a string — has no value the row could
    //    be keyed by that the caller did not choose, so only Create(id, model) is generated for it.
    private static (KeySource Source, string? Factory) KeySourceOf(ITypeSymbol? idType)
    {
        if (idType is null)
        {
            return (KeySource.None, null);
        }

        switch (idType.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return (KeySource.Store, null);
        }

        if (idType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is "global::System.Guid")
        {
            return (KeySource.Guid, "global::System.Guid.CreateVersion7()");
        }

        return ModelRegistryGenerator.StronglyTypedId.For(idType) is
        {
            Problem: null,
            TypeName: { } typeName,
            ValueTypeName: "global::System.Guid",
        }
            ? (KeySource.Guid, "new " + typeName + "(global::System.Guid.CreateVersion7())")
            : (KeySource.Caller, null);
    }

    private static Member ToMember(ModelMember member, int index, Dictionary<ModelValueObject, ValueObjectShape> converted)
    {
        var property = member.Property;
        var valueObject = member.ValueObject is null ? null : ToShape(member.ValueObject, converted);

        return new Member(
            property.Name,
            member.Role,
            ModelTypeOf(property.Type, valueObject),
            member.Nullable,
            valueObject,
            member.Write is { } kind
                ? WriteOf(property, kind, "__" + kind + index.ToString(CultureInfo.InvariantCulture) + "_" + property.Name, byRef: false)
                : null,
            new EquatableArray<string>(ImpliedRequired(member, valueObject, property, static a => "[" + a + "]")
                .Concat(property.GetAttributes().Where(IsCopiedAttribute).Select(RenderAttribute))),
            new EquatableArray<string>(ImpliedRequired(member, valueObject, property, static a => "new " + a)
                .Concat(property.GetAttributes().Where(IsValidation).Select(RenderConstruction))));
    }

    // A column the aggregate declares non-nullable cannot be emptied: the form model's value is nullable only so a
    // form can start blank, so a null there is a field the user cleared — an error, never a silent 0 on create or a
    // silently kept old value on update.
    private const string RequiredAttributeFqn = "global::System.ComponentModel.DataAnnotations.RequiredAttribute";

    private static IEnumerable<string> ImpliedRequired(
        ModelMember member,
        ValueObjectShape? valueObject,
        IPropertySymbol property,
        Func<string, string> render) =>
        member.Role == ModelMemberRole.Value && member.Write is not null && !member.Nullable &&
        valueObject is null or { SingleValue: true } &&
        !property.GetAttributes().Any(static a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal))
            ? [render(RequiredAttributeFqn + "()")]
            : [];

    private static Write WriteOf(IPropertySymbol property, ModelWriteKind kind, string accessorName, bool byRef)
    {
        var declaring = property.ContainingType;

        // A generic declaring type is matched against its open signature (set_Id(!0), not set_Id(Guid)), so
        // the accessor has to be declared in a generic context carrying the same type parameters — and their
        // constraints, or naming `Entity<TId>` inside it is CS0453 / CS8714 for a base that constrains TId.
        if (declaring.IsGenericType)
        {
            var definition = declaring.OriginalDefinition;
            return new Write(
                kind,
                accessorName,
                definition.ToDisplayString(TypeFormat),
                property.OriginalDefinition.Type.ToDisplayString(TypeFormat),
                string.Join(", ", definition.TypeParameters.Select(static t => t.Name)),
                string.Join(", ", declaring.TypeArguments.Select(static t => t.ToDisplayString(TypeFormat))),
                ConstraintClauses(definition.TypeParameters),
                byRef);
        }

        return new Write(
            kind,
            accessorName,
            declaring.ToDisplayString(TypeFormat),
            property.Type.ToDisplayString(TypeFormat),
            null,
            null,
            "",
            byRef);
    }

    // Every constraint, in the order C# requires: the primary one (class, struct, unmanaged, notnull), the
    // types — which may name other type parameters of the same declaration — then new(), then allows ref struct.
    private static string ConstraintClauses(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        var clauses = new StringBuilder();
        foreach (var parameter in typeParameters)
        {
            var parts = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
            {
                parts.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            else if (parameter.HasUnmanagedTypeConstraint)
            {
                parts.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                parts.Add("struct");
            }
            else if (parameter.HasNotNullConstraint)
            {
                parts.Add("notnull");
            }

            parts.AddRange(parameter.ConstraintTypes.Select(static t => t.ToDisplayString(TypeFormat)));

            if (parameter.HasConstructorConstraint)
            {
                parts.Add("new()");
            }

            if (parameter.AllowsRefLikeType)
            {
                parts.Add("allows ref struct");
            }

            if (parts.Count > 0)
            {
                clauses.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", parts));
            }
        }

        return clauses.ToString();
    }

    // Converted once per value object, so every use of one shares the same record.
    private static ValueObjectShape ToShape(ModelValueObject valueObject, Dictionary<ModelValueObject, ValueObjectShape> converted)
    {
        if (converted.TryGetValue(valueObject, out var known))
        {
            return known;
        }

        var members = valueObject.Members.Select(m =>
        {
            var nested = m.ValueObject is null ? null : ToShape(m.ValueObject, converted);
            return new ValueObjectMember(
                m.Property.Name,
                ModelTypeOf(m.Property.Type, nested),
                m.Nullable,
                nested,
                new EquatableArray<string>(m.Property.GetAttributes().Where(IsCopiedAttribute).Select(RenderAttribute)),
                m.Property.Type.ToDisplayString(TypeFormat),
                m.Write is { } kind
                    ? WriteOf(m.Property, kind, "__" + kind + valueObject.ModelName + "_" + m.Property.Name, byRef: valueObject.Type.IsValueType)
                    : null);
        }).ToList();

        var shape = new ValueObjectShape(
            valueObject.ModelName,
            valueObject.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat),
            valueObject.Build,
            new EquatableArray<string>(valueObject.ConstructorOrder?.Select(static p => p.Name) ?? []),
            new EquatableArray<string>(valueObject.Build == ModelValueObjectBuild.AccessorConstructor
                ? valueObject.Constructor.Parameters.Select(static p => p.Type.ToDisplayString(TypeFormat))
                : []),
            valueObject.Constructor.DeclaredAccessibility == Accessibility.Public,
            valueObject.Type.IsValueType,
            valueObject.SingleValue,
            new EquatableArray<ValueObjectMember>(members),
            RuleOf(valueObject));

        converted[valueObject] = shape;
        return shape;
    }

    // ---- emit helpers ---------------------------------------------------------------------------
    // Every property of a model is nullable so a form can start blank; a column that cannot be empty is [Required]
    // on the model (ImpliedRequired), so a null there never reaches a write. A one-value value object is carried as its value; any other as its nested model.
    private static string ModelTypeOf(ITypeSymbol type, ValueObjectShape? valueObject) =>
        valueObject switch
        {
            { SingleValue: true } single => single.Members[0].ModelType,
            { } nested => nested.ModelName + "?",
            _ => NullableDisplay(type),
        };

    private static string NullableDisplay(ITypeSymbol type)
    {
        if (type.IsValueType)
        {
            return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                ? type.ToDisplayString(TypeFormat)
                : type.ToDisplayString(TypeFormat) + "?";
        }

        return type.WithNullableAnnotation(NullableAnnotation.Annotated).ToDisplayString(TypeFormat);
    }

    // The form-facing attributes: DataAnnotations (less the three that only mean something to EF Core) and
    // any ValidationAttribute of the app's own. Schema attributes like [Column] say nothing to a form.
    private static bool IsCopiedAttribute(AttributeData attribute)
    {
        if (attribute.AttributeClass is not { } type)
        {
            return false;
        }

        if (type.ContainingNamespace?.ToDisplayString() is DataAnnotationsNamespace)
        {
            return type.Name is not ("KeyAttribute" or "ConcurrencyCheckAttribute" or "TimestampAttribute");
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() is ValidationAttribute)
            {
                return true;
            }
        }

        return false;
    }

    // What the server can run: [Display], [UIHint] and the other DataAnnotations that describe a field are copied
    // to the form but validate nothing.
    private static bool IsValidation(AttributeData attribute)
    {
        for (var current = attribute.AttributeClass?.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() is ValidationAttribute)
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderAttribute(AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments.Select(Argument)
            .Concat(attribute.NamedArguments.Select(static n => n.Key + " = " + Argument(n.Value)));

        return "[" + attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               "(" + string.Join(", ", arguments) + ")]";
    }

    // The same attribute as an object the server validates with — named arguments become an initializer.
    private static string RenderConstruction(AttributeData attribute)
    {
        var named = attribute.NamedArguments.Select(static n => n.Key + " = " + Argument(n.Value)).ToList();
        return "new " + attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               "(" + string.Join(", ", attribute.ConstructorArguments.Select(Argument)) + ")" +
               (named.Count == 0 ? "" : " { " + string.Join(", ", named) + " }");
    }

    // TypedConstant.ToCSharpString renders an array as a bare `{ a, b }`, which is not an expression and so not
    // an attribute argument: [AllowedValues("draft", "live")] was copied as a syntax error. An array is
    // written as the array creation a params argument is shorthand for.
    private static string Argument(TypedConstant constant) =>
        constant.Kind == TypedConstantKind.Array && !constant.IsNull
            ? "new " + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " +
              string.Join(", ", constant.Values.Select(Argument)) + " }"
            : constant.ToCSharpString();
}

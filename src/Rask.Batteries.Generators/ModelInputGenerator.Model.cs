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

        // A row keyed by several columns gets a model only as a row its holder's form carries; a link is a list of
        // ids on the holder's model, and one a form cannot carry is RASK106 on the holder.
        if (CompositeKeyShape.HasCompositeKey(symbol))
        {
            return CompositeChildForm.Of(symbol) is { Kind: CompositeChildKind.Rows, Problem: null } form
                ? AsKeyedRow(Accepted(symbol, name, location, cancellationToken), symbol, form)
                : Entity.Refused(name, symbol.Name, location, Refusal.CompositeKey, "");
        }

        return Accepted(symbol, name, location, cancellationToken);
    }

    // A row matched by its key's parts rather than an Id: the parts its holder does not give lead the model, each one
    // required, and the generated ones — invisible here, another generator writes them — are written by their setter.
    private static Entity AsKeyedRow(Entity row, INamedTypeSymbol symbol, CompositeChildForm form)
    {
        var parts = form.OwnParts.Select((part, index) =>
            row.Members.FirstOrDefault(m => string.Equals(m.Name, part.Name, StringComparison.Ordinal)) is { } declared
                ? declared with { RequiredByType = declared.Write is not null, KeyPart = true }
                : new Member(
                    part.Name,
                    ModelMemberRole.Value,
                    NullableDisplay(part.Type),
                    false,
                    null,
                    new Write(ModelWriteKind.Setter, "__Part" + index.ToString(CultureInfo.InvariantCulture) + "_" + part.Name,
                        symbol.ToDisplayString(TypeFormat), part.Type.ToDisplayString(TypeFormat), null, null, "", false),
                    new EquatableArray<string>([]),
                    new EquatableArray<string>([]),
                    RequiredByType: true,
                    KeyPart: true)).ToList();

        return row with
        {
            IdTypeName = null,
            KeyWrite = null,
            KeySource = KeySource.None,
            KeyFactory = null,
            KeyIsReference = false,
            KeyedByParts = true,
            Members = new EquatableArray<Member>(parts.Concat(row.Members.Where(m =>
                !form.OwnParts.Any(p => string.Equals(p.Name, m.Name, StringComparison.Ordinal))))),
        };
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
        var (keySource, keyFactory) = KeySourceOf(shape.IdType, ConfiguresKeyNeverGenerated(symbol, cancellationToken));

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
                .Select(ToChild)
                .Concat(Carried(shape, CompositeChildKind.Rows).Select(ToRowChild))),
            new EquatableArray<string>(AggregateReferencesOf(symbol)),
            new EquatableArray<string>(shape.Children
                .Concat(shape.CompositeChildren.Select(static c => c.Child))
                .Where(static c => c.Access == ModelChildAccess.None)
                .Select(static c => c.Name)),
            new EquatableArray<ValueCollectionShape>(
                shape.ValueCollections.Select(c => ToValueCollection(c, converted))),
            WritesOf(symbol),
            DeletableOf(symbol),
            ConfiguresUniqueRule(symbol, cancellationToken))
        {
            CompositeChildren = new EquatableArray<string>(shape.CompositeChildren
                .Where(static c => c.Form.Problem is not null && c.Child.Access != ModelChildAccess.None)
                .Select(static c => c.Child.Name + "|" + c.Child.ChildType.Name + "|" + c.Form.Problem)),
            IdLists = new EquatableArray<IdListShape>(Carried(shape, CompositeChildKind.Ids).Select(ToIdList)),
        };
    }

    // The children keyed by several columns a form carries as `kind`: a list of ids, or of rows.
    private static IEnumerable<ModelCompositeChild> Carried(ModelShape shape, CompositeChildKind kind) =>
        shape.CompositeChildren.Where(c =>
            c.Form.Kind == kind && c.Form.Problem is null && c.Child.Access != ModelChildAccess.None);

    // A row matched by the parts of its key its holder does not give.
    private static ChildShape ToRowChild(ModelCompositeChild row) =>
        ToChild(row.Child) with
        {
            KeyParts = new EquatableArray<KeyMatch>(row.Form.OwnParts.Select(static p =>
                new KeyMatch(p.Name, p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat)))),
        };

    // A link carried as the ids of its one other part: made by its constructor, that part written by its setter.
    private static IdListShape ToIdList(ModelCompositeChild link)
    {
        var part = link.Form.OwnParts[0];
        var name = CompositeChildForm.IdListName(link.Child.Name);
        var declared = part.Aggregate is null
            ? link.Child.ChildType.GetMembers(part.Name).OfType<IPropertySymbol>().First()
            : null;
        var write = declared is not null
            ? WriteOf(declared, GeneratedModelShape.WriteKindOf(declared)!.Value, "__Link_" + name, byRef: false)
            : new Write(ModelWriteKind.Setter, "__Link_" + name, link.Child.ChildType.ToDisplayString(TypeFormat),
                part.Type.ToDisplayString(TypeFormat), null, null, "", false);

        return new IdListShape(
            name,
            ToChild(link.Child),
            part.Name,
            part.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat),
            write);
    }

    /// <summary>
    ///     Whether the aggregate gets a generated <c>Delete</c>: it implements <c>IDeletable</c> or <c>ISoftDelete</c>.
    ///     One that names neither is corrected by a new row or retired by a state change, never removed.
    /// </summary>
    private static bool DeletableOf(INamedTypeSymbol symbol) => AggregateShape.IsDeletable(symbol);

    /// <summary>
    ///     The writes a form model takes — <c>IFormModel</c>, <c>ICreateFormModel</c>, <c>IUpdateFormModel</c> on the aggregate — as
    ///     <see cref="CreateWrite" /> / <see cref="UpdateWrite" /> bits; none when it implements none.
    /// </summary>
    private static int WritesOf(INamedTypeSymbol symbol)
    {
        var traits = AggregateShape.TraitsOf(symbol);

        return ((traits & AggregateShape.CreateModel) != 0 ? CreateWrite : NoWrites) |
               ((traits & AggregateShape.UpdateModel) != 0 ? UpdateWrite : NoWrites);
    }

    private static ValueCollectionShape ToValueCollection(
        ModelValueCollection collection, Dictionary<ModelValueObject, ValueObjectShape> converted) => new(
        collection.Name,
        collection.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        collection.ElementModel is null ? null : ToShape(collection.ElementModel, converted),
        collection.Field is not null,
        collection.Field?.Name,
        collection.Field?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        ?? collection.Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        collection.Field is null ? ReplaceOf(collection.Property, "Values_") : null,
        collection.Field?.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

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
        ?? child.Property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        child.Access == ModelChildAccess.Replace ? ReplaceOf(child.Property, "Children_") : null,
        child.Field?.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

    // How a collection is written back whole — through its setter or its backing field — or null when it cannot be.
    private static Write? ReplaceOf(IPropertySymbol property, string role) =>
        GeneratedModelShape.WriteKindOf(property) is { } kind
            ? WriteOf(property, kind, "__" + kind + role + property.Name, byRef: false)
            : null;

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
        foreach (var property in GeneratedModelShape.Properties(symbol))
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
    //    and marks every other Entity<TId> key never-generated, so EF produces nothing else — unless the
    //    aggregate's own Configure says the key is never generated, and then it is the caller's like any other.
    //  * Anything else — a string, a strongly-typed id over an integer or a string — has no value the row could
    //    be keyed by that the caller did not choose, so only Create(id, model) is generated for it.
    private static (KeySource Source, string? Factory) KeySourceOf(ITypeSymbol? idType, bool neverGenerated)
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
                return (neverGenerated ? KeySource.Caller : KeySource.Store, null);
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

    /// <summary>
    ///     Whether the aggregate's own <c>Configure</c> says its key is assigned by the application:
    ///     <c>builder.Property(x =&gt; x.Id).ValueGeneratedNever()</c>.
    /// </summary>
    /// <remarks>
    ///     Read from the source, as its unique rules are, because the writes are generated before any model is
    ///     built. So it sees the one chain that starts at <c>Property(…Id)</c> in a static <c>Configure</c> on the
    ///     aggregate — any of its partial declarations — and nothing else: not a helper that method calls, an
    ///     <c>IEntityTypeConfiguration</c>, <c>OnModelCreating</c>, or a builder kept in a variable first. A key
    ///     configured there is still inserted as given; the aggregate just keeps the creates of a stored identity.
    /// </remarks>
    private static bool ConfiguresKeyNeverGenerated(INamedTypeSymbol symbol, CancellationToken cancellationToken) =>
        symbol.GetMembers("Configure").OfType<IMethodSymbol>()
            .Where(static method => method.IsStatic)
            .SelectMany(static method => method.DeclaringSyntaxReferences)
            .SelectMany(reference => reference.GetSyntax(cancellationToken).DescendantNodes())
            .OfType<InvocationExpressionSyntax>()
            .Any(static call =>
                call.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ValueGeneratedNever" } never &&
                IsOnTheKey(never.Expression));

    // Walks the chain back from ValueGeneratedNever() to the Property(…) call it configures.
    private static bool IsOnTheKey(ExpressionSyntax receiver)
    {
        while (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call)
        {
            if (string.Equals(access.Name.Identifier.ValueText, "Property", StringComparison.Ordinal))
            {
                return call.ArgumentList.Arguments.Count > 0 && NamesTheKey(call.ArgumentList.Arguments[0].Expression);
            }

            receiver = access.Expression;
        }

        return false;
    }

    // x => x.Id, "Id" or nameof(Order.Id).
    private static bool NamesTheKey(ExpressionSyntax argument)
    {
        var named = argument switch
        {
            LambdaExpressionSyntax { Body: MemberAccessExpressionSyntax member } => member.Name.Identifier.ValueText,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" }, ArgumentList.Arguments: { Count: 1 } of } =>
                of[0].Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    _ => null,
                },
            _ => null,
        };

        return string.Equals(named, "Id", StringComparison.Ordinal);
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
            new EquatableArray<string>(property.GetAttributes().Where(IsCopiedAttribute).Select(RenderAttribute)),
            new EquatableArray<string>(property.GetAttributes().Where(IsValidation).Select(RenderConstruction)),
            IsRequiredByType(member, valueObject, property));
    }

    // A column the aggregate declares non-nullable cannot be emptied: the form model's value is nullable only so a
    // form can start blank, so a null there is a field the user cleared — an error, never a silent 0 on create or a
    // silently kept old value on update. The TYPE says so, as a generated rule with the framework's sentence, and
    // no attribute is written. A value object that states its own rule says it in its own words instead, and a
    // property the app marked [Required] itself keeps that attribute and its message.
    private static bool IsRequiredByType(ModelMember member, ValueObjectShape? valueObject, IPropertySymbol property) =>
        member.Role == ModelMemberRole.Value && member.Write is not null &&
        IsRequiredByType(member.Nullable, valueObject, property);

    // The same for a member of a value object of several values, which is a field of its own on the nested model.
    private static bool IsRequiredByType(bool nullable, ValueObjectShape? valueObject, IPropertySymbol property) =>
        !nullable &&
        valueObject is null or { SingleValue: true, Rule: ValueRule.None } &&
        !property.GetAttributes().Any(static a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal));

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
                    : null,
                new EquatableArray<string>(m.Property.GetAttributes().Where(IsValidation).Select(RenderConstruction)),
                IsRequiredByType(m.Nullable, nested, m.Property));
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
            RuleOf(valueObject),
            new EquatableArray<RuleMiss>(RuleMissesOf(valueObject)));

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

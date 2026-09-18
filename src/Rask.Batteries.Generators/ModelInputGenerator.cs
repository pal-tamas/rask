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
using Microsoft.CodeAnalysis.Text;
using Rask.Cqrs.Generators;
using Rask.Generators.Shared;

namespace Rask.Data.Generators;

/// <summary>
/// Gives every <c>Rask.Data.Aggregate&lt;TId&gt;</c> a form-shaped companion — <c>ProductModel</c> for <c>Product</c> — and
/// the writes that take it: <c>Product.CreateAsync(id, model)</c>, <c>Product.CreateAsync(model)</c> where a key
/// can be produced without the caller, <c>Product.UpdateAsync(id, model)</c>, <c>Product.UpdateAsync(id, apply)</c>
/// and <c>Product.DeleteAsync(id, version)</c>. Every write takes an optional <c>apply</c> (values that do not come
/// from the form) where it builds or edits a row, and an optional <c>db</c> to join a caller's context.
/// </summary>
/// <remarks>
/// <para>
/// The model is a plain mutable class a form binds to, carrying the entity's validation attributes. The
/// writes go through <c>Rask.Data.GeneratedModelWrites</c>, so the change tracker — and with it the audit,
/// soft-delete and domain-event interceptors — sees every one.
/// </para>
/// <para>
/// <b>The model carries no key.</b> It is what a form posts back, so a key on it would be one the client
/// chooses. The id travels beside it — <c>UpdateAsync(id, model)</c> — and a create takes none.
/// </para>
/// <para>
/// <b>An entity keeps its private setters and needs no <c>partial</c>.</b> Values are written through
/// <c>[UnsafeAccessor]</c> declarations: a direct, compile-time-bound call into the private member, with no
/// reflection for the trimmer to break. A member declared on a generic base is reached through a generic
/// accessor class carrying the base's type parameters AND their constraints, because the runtime matches the
/// accessor against the member's open signature and the compiler checks the constraints of every type the
/// accessor names. Value objects with non-public constructors or setters are rebuilt the same way.
/// </para>
/// </remarks>
[Generator]
public sealed class ModelInputGenerator : IIncrementalGenerator
{
    /// <summary>The suffix the generated model takes after the entity's name.</summary>
    internal const string ModelSuffix = GeneratedModelShape.ModelSuffix;

    // Mirrors Rask.Data.ModelWrites. A generator targets netstandard2.0 and never loads the runtime
    // assembly, so the values are restated here and pinned against the enum by a test.
    private const int NoWrites = 0;
    private const int CreateWrite = 1;
    private const int UpdateWrite = 2;
    internal const int AllWrites = CreateWrite | UpdateWrite;

    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";
    private const string ValidationAttribute = "System.ComponentModel.DataAnnotations.ValidationAttribute";
    private const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string UnsafeAccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind";

    private static readonly SymbolDisplayFormat TypeFormat = GeneratedModelShape.TypeFormat;

    // RASK081 said this before the writes were dropped; a retired id is never recycled, so the rule returned as RASK086.
    internal static readonly DiagnosticDescriptor Rask086 = new(
        "RASK086",
        "Aggregate has no parameterless constructor, so CreateAsync is not generated",
        "'{0}' declares a constructor that takes arguments, so it has no parameterless one and no generated "
        + "'{0}.CreateAsync' — from a '{0}Model' or a 'p => …' — exists; declare no constructor and build it in a "
        + "static factory instead, or insert one you built with '{0}.CreateAsync(entity)'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Every generated CreateAsync — and a new form model's defaults — starts from an empty aggregate, "
                     + "which needs a constructor that takes nothing. An aggregate that declares no constructor has "
                     + "one for free; domain creation belongs in a static factory. Everything that works on a row that "
                     + "already exists — the model itself, UpdateAsync and DeleteAsync — is still generated.",
        helpLinkUri: DiagnosticHelp.Link("RASK086"));

    internal static readonly DiagnosticDescriptor Rask087 = new(
        "RASK087",
        "Aggregate reaches across a boundary",
        "'{0}.{1}' is {3}, which is an aggregate of its own — a boundary {0} may not reach across, so Rask "
        + "never loads, carries, saves or deletes it with {0}; {4}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An aggregate is a consistency boundary, and the border is what one aggregate can SEE of "
                     + "another: holding nothing but an id is what makes crossing a boundary by accident "
                     + "impossible. Another AGGREGATE has its own version, its own soft delete and its own "
                     + "reads and writes, so a form post on this one must never add to it and never delete "
                     + "from it. Nothing is lost by holding the id: the generated read face carries the "
                     + "navigation the write model is not allowed to have, so the join you wanted is still one "
                     + "expression — it just cannot be reached from the side that saves.",
        helpLinkUri: DiagnosticHelp.Link("RASK087"));

    internal static readonly DiagnosticDescriptor Rask088 = new(
        "RASK088",
        "Child collection cannot be synced",
        "'{0}.{1}' holds children, but Rask cannot find a collection to write: its type is not an "
        + "ICollection<T> and '{0}' has {2}, so the model can show these children but a save cannot add or "
        + "remove one; expose the collection as ICollection<T>, or keep it in a single backing field",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A child collection on the model is editable: what the form posts is what the aggregate "
                     + "holds afterwards. Writing it needs something to add to and remove from — the property "
                     + "itself when it is an ICollection<T>, or the one field behind it when it hands out a "
                     + "read-only view. With neither, a save would silently keep whatever was stored, which "
                     + "looks exactly like a form that did not submit.",
        helpLinkUri: DiagnosticHelp.Link("RASK088"));

    internal static readonly DiagnosticDescriptor Rask082 = new(
        "RASK082",
        "A type already has the generated model's name",
        "'{1}' already exists beside the entity '{0}', so Rask cannot generate its form model or the "
        + "CreateAsync, UpdateAsync and DeleteAsync that take it; rename the existing type, declare it "
        + "'partial' to extend the generated one",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Every Rask.Data.Aggregate<TId> gets a generated {Entity}Model in its own namespace. A hand-written, "
                     + "non-partial type of that name would collide with it as CS0101, a message that names "
                     + "neither the generator nor the way out — so the generator stands down and says why instead.",
        helpLinkUri: DiagnosticHelp.Link("RASK082"));

    internal static readonly DiagnosticDescriptor Rask083 = new(
        "RASK083",
        "Nested entity gets no generated model",
        "'{0}' is declared inside '{1}', so no '{0}Model' is generated for it; declare the entity at "
        + "namespace level to get one",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The generated model and its writes are emitted beside the entity, as siblings in its "
                     + "namespace. An entity nested in another type has no such place, so it is mapped as usual "
                     + "but gets no form model.",
        helpLinkUri: DiagnosticHelp.Link("RASK083"));

    internal static readonly DiagnosticDescriptor Rask091 = new(
        "RASK091",
        "A child cannot choose its own form writes",
        "'{0}' is a child of another aggregate, so its 'Writes' const is ignored — a child model exists to "
        + "carry the ROOT's form and is generated with it; put the const on the aggregate that holds '{0}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "ModelWrites narrows the form surface of an AGGREGATE. A child has no writes of its own "
                     + "to narrow — it is created, changed and removed through its root — and its model is "
                     + "part of the root's, which is what a form posts. Honouring the const here would leave "
                     + "the root's model holding a list of a child model that was never generated, so it is "
                     + "reported rather than obeyed.",
        helpLinkUri: DiagnosticHelp.Link("RASK091"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, ct) => GetEntity(ctx, ct))
            .Where(static entity => entity is not null)
            .Select(static (entity, _) => entity!);

        context.RegisterSourceOutput(
            entities.Collect().Combine(ReadFacesOnly.Of(context)),
            static (spc, pair) =>
            {
                if (!pair.Right)
                {
                    Emit(spc, pair.Left);
                }
            });
    }

    // ---- discovery ------------------------------------------------------------------------------

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
            WritesOf(symbol, out var declaresWrites),
            declaresWrites);
    }

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

    // Who can produce the key of a row inserted WITHOUT one — which decides whether CreateAsync(model) exists.
    //  * A Guid: this code, as a version-7 Guid (time-ordered, so the primary-key index stays append-only), and
    //    the same inside a strongly-typed id over a Guid.
    //  * An integer: the store's identity column. Rask.Data's key convention leaves integer keys store-generated
    //    and marks every other Entity<TId> key never-generated, so EF produces nothing else.
    //  * Anything else — a string, a strongly-typed id over an integer or a string — has no value the row could
    //    be keyed by that the caller did not choose, so only CreateAsync(id, model) is generated for it.
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

        if (idType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Guid")
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
            new EquatableArray<string>(property.GetAttributes().Where(IsCopiedAttribute).Select(RenderAttribute)));
    }

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
                Constraints(definition.TypeParameters),
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
    private static string Constraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
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
            new EquatableArray<ValueObjectMember>(members));

        converted[valueObject] = shape;
        return shape;
    }

    // ---- emit helpers ---------------------------------------------------------------------------
    // Every property of a model is nullable: a null means "not given" — an update leaves that value as it is, a create
    // keeps the aggregate's default. A one-value value object is carried as its value; any other as its nested model.
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

        if (type.ContainingNamespace?.ToDisplayString() == DataAnnotationsNamespace)
        {
            return type.Name is not ("KeyAttribute" or "ConcurrencyCheckAttribute" or "TimestampAttribute");
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == ValidationAttribute)
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

    // TypedConstant.ToCSharpString renders an array as a bare `{ a, b }`, which is not an expression and so not
    // an attribute argument: [AllowedValues("draft", "live")] was copied as a syntax error. An array is
    // written as the array creation a params argument is shorthand for.
    private static string Argument(TypedConstant constant) =>
        constant.Kind == TypedConstantKind.Array && !constant.IsNull
            ? "new " + constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " { " +
              string.Join(", ", constant.Values.Select(Argument)) + " }"
            : constant.ToCSharpString();

    // ---- emit -----------------------------------------------------------------------------------

    private static void Emit(SourceProductionContext context, ImmutableArray<Entity> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Distinct because a partial entity contributes one candidate per declaration with a base list.
        var entities = candidates.Distinct().OrderBy(static e => e.FullyQualifiedName, StringComparer.Ordinal).ToList();

        // A child model exists to carry its ROOT's form — it is the element type of the list the root's model
        // holds — so it follows the root rather than deciding for itself. A child of a root that wants no form
        // gets no model either; a child held by two roots keeps one as long as either still has a form.
        var childrenWithAForm = new HashSet<string>(
            entities
                .Where(static e => !e.IsChild && e.Writes != NoWrites)
                .SelectMany(static e => e.Children.Select(static c => c.ChildTypeName)),
            StringComparer.Ordinal);

        foreach (var candidate in entities)
        {
            // A child's form surface is the ROOT's, always — its own const is reported by RASK091 and then
            // genuinely ignored, rather than half-obeyed into a root model holding a type nobody generated.
            var entity = candidate.IsChild
                ? candidate with
                {
                    Writes = childrenWithAForm.Contains(candidate.FullyQualifiedName) ? AllWrites : NoWrites,
                }
                : candidate;

            switch (entity.Refusal)
            {
                case Refusal.Nested:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rask083, entity.Location?.ToLocation(), entity.Name, entity.RefusalDetail));
                    continue;
                case Refusal.Clash:
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rask082, entity.Location?.ToLocation(), entity.Name, entity.RefusalDetail));
                    continue;
            }

            if (!entity.Constructible)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask086, entity.Location?.ToLocation(), entity.Name));
            }

            if (entity.IsChild && entity.DeclaresWrites)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rask091, entity.Location?.ToLocation(), entity.Name));
            }

            foreach (var held in entity.AggregateReferences)
            {
                var parts = held.Split('|');
                var (property, target, idType, many) = (parts[0], parts[1], parts[2], parts[3] == "many");

                // The diagnosis is one rule; the fix is not. A single reference becomes an id on THIS side and
                // a collection becomes an id on the OTHER, so telling an author to add '{target}Id' to an
                // aggregate that needs the parent's id instead would name the wrong half of the relationship.
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask087,
                    entity.Location?.ToLocation(),
                    entity.Name,
                    property,
                    target,
                    many ? $"a collection of '{target}'" : $"a reference to '{target}'",
                    many
                        ? $"let each '{target}' hold {entity.Name}'s id and read them back through "
                          + $"{target}.Read, whose navigation those ids infer, or derive '{target}' from "
                          + $"Entity<TId> if they are genuinely part of {entity.Name}"
                        : $"hold its id instead — '{idType} {property}Id' — and read the join through "
                          + $"{entity.Name}.Read, where it is inferred back as '{property}', or derive "
                          + $"'{target}' from Entity<TId> if it is genuinely part of {entity.Name}"));
            }

            foreach (var unsyncable in entity.UnsyncableChildren)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask088,
                    entity.Location?.ToLocation(),
                    entity.Name,
                    unsyncable,
                    "no single field of that collection type"));
            }

            var hint = entity.FullyQualifiedName.Replace("global::", "") + ModelSuffix + ".g.cs";
            context.AddSource(hint, SourceText.From(Render(entity), Encoding.UTF8));
        }
    }

    private static string Render(Entity entity)
    {
        var modelName = entity.Name + ModelSuffix;
        var modelType = (entity.Namespace.Length == 0 ? "global::" : "global::" + entity.Namespace + ".") + modelName;
        var entityType = entity.FullyQualifiedName;
        const string Task = "global::System.Threading.Tasks.Task";
        const string Token = "global::System.Threading.CancellationToken";
        const string Writes = "global::Rask.Data.GeneratedModelWrites";

        // What `public const ModelWrites Writes` narrows. It reaches the FORM surface only: the behaviour
        // writes below take no model, and the read face is generated elsewhere and not negotiable.
        var formCreate = (entity.Writes & CreateWrite) != 0;
        var formUpdate = (entity.Writes & UpdateWrite) != 0;
        var formModel = entity.Writes != NoWrites;

        var s = new StringBuilder();

        // A section is written and then dropped rather than guarded line by line, so there is ONE piece of
        // code deciding what a form write looks like instead of two that have to agree about it.
        void Keep(bool wanted, int from)
        {
            if (!wanted)
            {
                s.Length = from;
            }
        }
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (entity.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(entity.Namespace).AppendLine(";");
            s.AppendLine();
        }

        // ---- the model ----
        var modelMark = s.Length;
        s.Append("/// <summary>The form model for <see cref=\"").Append(entityType)
            .AppendLine("\" />, generated by Rask from its mapped properties.</summary>");
        s.AppendLine("/// <remarks>");
        // Names only the writes this entity actually got — a model without a parameterless constructor has
        // no CreateAsync at all, one whose key only the caller can supply has no id-less one, and one without
        // no id has none to create or update by.
        // A child is created, changed and removed as part of the aggregate that holds it — never off its own
        // type — so it gets the model and the plumbing, and none of the writes.
        var idLessCreate = !entity.IsChild && IdLessCreate(entity);
        var createWithId = !entity.IsChild && CreateWithId(entity);
        var writes = new List<string>();
        if (idLessCreate)
        {
            writes.Add("<c>" + entity.Name + ".CreateAsync(model)</c>");
        }

        if (createWithId)
        {
            writes.Add("<c>" + entity.Name + ".CreateAsync(id, model)</c>");
        }

        if (!entity.IsChild && entity.IdTypeName is not null)
        {
            writes.Add("<c>" + entity.Name + ".UpdateAsync(id, model)</c>");
        }

        s.Append("/// Bind it with <c>Form.Model(model)</c>")
            .Append(writes.Count == 0 ? "" : "; persist it with " + JoinWrites(writes))
            .AppendLine(".");
        if (entity.IsChild)
        {
            s.Append("/// It is a CHILD model: it belongs to the <c>")
                .Append(entity.Name)
                .AppendLine("</c> list on its aggregate's model, and is saved with it.");
            s.AppendLine("/// It carries an <c>Id</c> so a save knows which stored child each row is — a row with none is a new one.");
        }
        else if (entity.IdTypeName is not null)
        {
            s.AppendLine("/// It carries no id: the id is passed beside it, so a posted form cannot point a write at another row.");
        }

        s.AppendLine("/// Every property is nullable: a null clears a property the aggregate declares nullable, and leaves the others as they are.");
        s.AppendLine("/// </remarks>");
        s.Append(entity.Accessibility).Append(" sealed partial class ").AppendLine(modelName);
        s.AppendLine("{");

        if (entity.Constructible)
        {
            s.Append("    /// <summary>A model holding <see cref=\"").Append(entityType)
                .AppendLine("\" />'s own defaults — what its constructor and property initializers set.</summary>");
            s.Append("    public ").Append(modelName).Append("() => ").Append(modelName)
                .AppendLine("Extensions.__Fill(this, " + modelName + "Extensions.__Defaults());");
            s.AppendLine();
            s.AppendLine("    /// <summary>A model with no values yet, filled by the generated <c>ToModel()</c>.</summary>");
            s.Append("    internal ").Append(modelName).AppendLine("(bool blank) => _ = blank;");
            s.AppendLine();
        }

        if (entity.IsChild && entity.IdTypeName is not null)
        {
            s.Append("    /// <summary>Which stored <see cref=\"").Append(entityType)
                .AppendLine("\" /> this row is, or <c>null</c> for one the form just added.</summary>");
            s.AppendLine("    /// <remarks>");
            s.AppendLine("    /// Safe to post, unlike an aggregate's own id: it only ever picks a child of the aggregate being");
            s.AppendLine("    /// saved. An id that matches none of them is treated as a new child rather than followed.");
            s.AppendLine("    /// </remarks>");
            s.Append("    public ").Append(entity.IdTypeName).AppendLine("? Id { get; set; }");
            s.AppendLine();
        }

        foreach (var child in entity.Children)
        {
            s.Append("    /// <summary>The form models of <see cref=\"").Append(entityType).Append('.')
                .Append(child.Name).AppendLine("\" />.</summary>");
            s.AppendLine("    /// <remarks>");
            s.AppendLine("    /// <b>What this list holds is what the aggregate holds after the save.</b> A row with no");
            s.AppendLine("    /// <c>Id</c> is added, a row whose <c>Id</c> matches a stored child updates it, and a stored");
            s.AppendLine("    /// child whose id is in none of these rows is REMOVED — so a form that posts a subset deletes");
            s.AppendLine("    /// the rest, and an empty list deletes them all.");
            s.AppendLine("    /// </remarks>");
            s.Append("    public global::System.Collections.Generic.List<").Append(child.ChildModelName)
                .Append("> ").Append(child.Name).AppendLine(" { get; set; } = [];");
            s.AppendLine();
        }

        foreach (var member in entity.Members)
        {
            s.Append("    /// <summary>The form value of <see cref=\"").Append(entityType).Append('.')
                .Append(member.Name).AppendLine("\" />.</summary>");
            foreach (var attribute in member.Attributes)
            {
                s.Append("    ").AppendLine(attribute);
            }

            s.Append("    public ").Append(member.ModelType).Append(' ').Append(member.Name).AppendLine(" { get; set; }");
            s.AppendLine();
        }

        foreach (var valueObject in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            s.Append("    /// <summary>The form model for <see cref=\"").Append(valueObject.TypeName)
                .AppendLine("\" />.</summary>");
            s.Append("    public sealed partial class ").AppendLine(valueObject.ModelName);
            s.AppendLine("    {");
            foreach (var member in valueObject.Members)
            {
                s.Append("        /// <summary>The form value of <see cref=\"").Append(valueObject.TypeName)
                    .Append('.').Append(member.Name).AppendLine("\" />.</summary>");
                foreach (var attribute in member.Attributes)
                {
                    s.Append("        ").AppendLine(attribute);
                }

                s.Append("        public ").Append(member.ModelType).Append(' ').Append(member.Name).AppendLine(" { get; set; }");
            }

            s.AppendLine("    }");
            s.AppendLine();
        }

        s.AppendLine("}");
        s.AppendLine();

        Keep(formModel, modelMark);

        // ---- the members on the entity ----
        // Every write ends in the same two optional parameters: `apply`, for values that do not come from the form
        // (a timestamp, the signed-in user), run after the model so it has the last word; and `db`, a context to
        // join instead of opening one.
        var applyParameter = "global::System.Action<" + entityType + ">? apply = null, ";
        const string DbParameter = "global::Microsoft.EntityFrameworkCore.DbContext? db = null, ";
        const string ApplyDoc = "        /// <param name=\"apply\">Sets values that do not come from the form, after the model's; <c>null</c> for none.</param>";
        const string DbDoc = "        /// <param name=\"db\">The context to work in, or <c>null</c> to open one. A given context is only STAGED — the caller saves it — and is not disposed.</param>";

        s.Append("/// <summary>The writes Rask generates for <see cref=\"").Append(entityType)
            .Append("\" />, taking its <see cref=\"").Append(modelType).AppendLine("\" />.</summary>");
        s.Append(entity.Accessibility).Append(" static class ").Append(modelName).AppendLine("Extensions");
        s.AppendLine("{");

        s.Append("    extension(").Append(entityType).AppendLine(")");
        s.AppendLine("    {");

        // Creates mirror the updates: `CreateAsync(model, apply?)` beside `UpdateAsync(id, model, apply?)`, and
        // `CreateAsync(apply)` beside `UpdateAsync(id, apply)` for a row with no form behind it.
        if (idLessCreate)
        {
            var createMark = s.Length;
            s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
                .AppendLine("\" /> built from <paramref name=\"model\" />.</summary>");
            s.AppendLine("        /// <param name=\"model\">The values to create it with.</param>");
            s.AppendLine(ApplyDoc);
            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
            s.AppendLine("        /// <returns>The inserted entity, with its key.</returns>");
            AppendKeyRemarks(s, entity);
            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> CreateAsync(")
                .Append(modelType).Append(" model, ").Append(applyParameter).Append(DbParameter).Append(Token)
                .AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
            AppendNewEntity(s, entity, withId: false);
            s.AppendLine("            __Apply(entity, model);");
            s.AppendLine("            apply?.Invoke(entity);");
            s.Append("            return ").Append(Writes).AppendLine(".CreateAsync(entity, db, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();

            Keep(formCreate, createMark);

            s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
                .AppendLine("\" /> whose values <paramref name=\"apply\" /> sets.</summary>");
            s.AppendLine("        /// <param name=\"apply\">Sets the new row's values.</param>");
            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
            s.AppendLine("        /// <returns>The inserted entity, with its key.</returns>");
            AppendKeyRemarks(s, entity);
            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> CreateAsync(global::System.Action<")
                .Append(entityType).Append("> apply, ").Append(DbParameter).Append(Token).AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(apply);");
            AppendNewEntity(s, entity, withId: false);
            s.AppendLine("            apply(entity);");
            s.Append("            return ").Append(Writes).AppendLine(".CreateAsync(entity, db, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();
        }

        if (createWithId)
        {
            var keyedMark = s.Length;
            s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
                .AppendLine("\" /> under <paramref name=\"id\" />, built from <paramref name=\"model\" />.</summary>");
            s.AppendLine("        /// <param name=\"id\">The key of the new row.</param>");
            s.AppendLine("        /// <param name=\"model\">The values to create it with.</param>");
            s.AppendLine(ApplyDoc);
            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
            s.AppendLine("        /// <returns>The inserted entity.</returns>");
            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> CreateAsync(")
                .Append(entity.IdTypeName).Append(" id, ").Append(modelType).Append(" model, ").Append(applyParameter)
                .Append(DbParameter).Append(Token).AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
            AppendNewEntity(s, entity, withId: true);
            s.AppendLine("            __Apply(entity, model);");
            s.AppendLine("            apply?.Invoke(entity);");
            s.Append("            return ").Append(Writes).AppendLine(".CreateAsync(entity, db, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();

            Keep(formCreate, keyedMark);

            s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
                .AppendLine("\" /> under <paramref name=\"id\" />, whose values <paramref name=\"apply\" /> sets.</summary>");
            s.AppendLine("        /// <param name=\"id\">The key of the new row.</param>");
            s.AppendLine("        /// <param name=\"apply\">Sets the new row's values.</param>");
            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
            s.AppendLine("        /// <returns>The inserted entity.</returns>");
            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> CreateAsync(")
                .Append(entity.IdTypeName).Append(" id, global::System.Action<").Append(entityType).Append("> apply, ")
                .Append(DbParameter).Append(Token).AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(apply);");
            AppendNewEntity(s, entity, withId: true);
            s.AppendLine("            apply(entity);");
            s.Append("            return ").Append(Writes).AppendLine(".CreateAsync(entity, db, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();
        }

        if (!entity.IsChild && entity.IdTypeName is { } idType)
        {
            var version = entity.Versioned ? "model.Version" : "null";
            const string NotFoundDoc = "        /// <exception cref=\"global::System.Collections.Generic.KeyNotFoundException\">No row has the id, or it is soft-deleted.</exception>";
            const string ConflictDoc = "        /// <exception cref=\"global::Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException\">The row was saved by someone else since it was read.</exception>";
            const string VersionDoc = "        /// <param name=\"version\">The version last read, to refuse a write to a row saved since; <c>null</c> skips the check.</param>";

            var updateMark = s.Length;
            s.Append("        /// <summary>Writes <paramref name=\"model\" /> onto the stored <see cref=\"").Append(entityType)
                .AppendLine("\" /> with <paramref name=\"id\" /> — only the values that changed.</summary>");
            s.AppendLine("        /// <param name=\"id\">The id of the row to update.</param>");
            s.AppendLine("        /// <param name=\"model\">The edited values" +
                         (entity.Versioned ? ", carrying the version they were read at" : "") + ".</param>");
            s.AppendLine(ApplyDoc);
            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
            s.AppendLine("        /// <returns>The updated entity.</returns>");
            s.AppendLine(NotFoundDoc);
            if (entity.Versioned)
            {
                s.AppendLine(ConflictDoc);
            }

            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> UpdateAsync(")
                .Append(idType).Append(" id, ").Append(modelType).Append(" model, ").Append(applyParameter)
                .Append(DbParameter).Append(Token).AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
            s.Append("            return ").Append(Writes).Append(".UpdateAsync<").Append(entityType)
                .Append(">(id!, ").Append(version)
                .AppendLine(", entity => { __Apply(entity, model); apply?.Invoke(entity); }, db, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();

            Keep(formUpdate, updateMark);

            // The write with no form behind it: `Product.UpdateAsync(id, p => p.ShippedAt = now)`.
            s.Append("        /// <summary>Loads the stored <see cref=\"").Append(entityType)
                .AppendLine("\" /> with <paramref name=\"id\" />, applies <paramref name=\"apply\" /> and saves — only the values that changed.</summary>");
            s.AppendLine("        /// <param name=\"id\">The id of the row to update.</param>");
            s.AppendLine("        /// <param name=\"apply\">Sets the new values on the loaded row.</param>");
            if (entity.Versioned)
            {
                s.AppendLine(VersionDoc);
            }

            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
            s.AppendLine("        /// <returns>The updated entity.</returns>");
            s.AppendLine(NotFoundDoc);
            if (entity.Versioned)
            {
                s.AppendLine(ConflictDoc);
            }

            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> UpdateAsync(")
                .Append(idType).Append(" id, global::System.Action<").Append(entityType).Append("> apply, ");
            if (entity.Versioned)
            {
                s.Append("int? version = null, ");
            }

            s.Append(DbParameter).Append(Token).AppendLine(" cancellationToken = default) =>");
            s.Append("            ").Append(Writes).Append(".UpdateAsync<").Append(entityType).Append(">(id!, ")
                .Append(entity.Versioned ? "version" : "null").AppendLine(", apply, db, cancellationToken);");
            s.AppendLine();

            s.Append("        /// <summary>Deletes the stored <see cref=\"").Append(entityType)
                .AppendLine("\" /> with <paramref name=\"id\" />, through the interceptors.</summary>");
            s.AppendLine("        /// <param name=\"id\">The id of the row to delete.</param>");
            if (entity.Versioned)
            {
                s.AppendLine(VersionDoc);
            }

            s.AppendLine(DbDoc);
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
            s.AppendLine(NotFoundDoc);
            s.Append("        public static ").Append(Task).Append(" DeleteAsync(").Append(idType).Append(" id, ");
            if (entity.Versioned)
            {
                s.Append("int? version = null, ");
            }

            s.Append(DbParameter).Append(Token).AppendLine(" cancellationToken = default) =>");
            s.Append("            ").Append(Writes).Append(".DeleteAsync<").Append(entityType).Append(">(id!, ")
                .Append(entity.Versioned ? "version" : "null").AppendLine(", db, cancellationToken);");
            s.AppendLine();

            var modelAsyncMark = s.Length;

            // The form loop's fill. Deliberately not a read-face query: the read face is flat primitives and
            // the form model keeps value objects nested, so this loads the aggregate and reuses __Fill rather
            // than maintaining a second projection that could drift from it.
            s.Append("        /// <summary>The edit shape of the <see cref=\"").Append(entityType)
                .AppendLine("\" /> with <paramref name=\"id\" />, ready to bind to a form.</summary>");
            s.AppendLine("        /// <param name=\"id\">The id of the row to fill the form from.</param>");
            s.AppendLine("        /// <param name=\"db\">The context to read through, or <c>null</c> to open one. A given context is not disposed.</param>");
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load.</param>");
            s.Append("        /// <returns>The model, or <c>null</c> when no <see cref=\"").Append(entityType)
                .AppendLine("\" /> has that id.</returns>");
            s.AppendLine("        /// <remarks>");
            s.Append("        ///     The aggregate is loaded whole, so its children arrive as child models. A soft-deleted")
                .AppendLine();
            s.AppendLine("        ///     row is not found, exactly as it is not found by a query.");
            s.AppendLine("        /// </remarks>");
            s.Append("        public static async ").Append(Task).Append('<').Append(modelType)
                .Append("?> ModelAsync(").Append(idType).Append(" id, ").Append(DbParameter).Append(Token)
                .AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.Append("            var entity = await ").Append(Writes).Append(".ModelSourceAsync<").Append(entityType)
                .AppendLine(">(id!, db, cancellationToken).ConfigureAwait(false);");
            s.AppendLine("            return entity?.ToModel();");
            s.AppendLine("        }");

            Keep(formModel, modelAsyncMark);
        }

        s.AppendLine("    }");
        s.AppendLine();

        var toModelMark = s.Length;
        s.Append("    extension(").Append(entityType).AppendLine(" entity)");
        s.AppendLine("    {");
        s.Append("        /// <summary>Copies this aggregate into a new <see cref=\"").Append(modelType)
            .AppendLine("\" />, ready to bind to an edit form.</summary>");
        s.Append("        public ").Append(modelType).AppendLine(" ToModel()");
        s.AppendLine("        {");
        s.Append("            var model = new ").Append(modelType).AppendLine(entity.Constructible ? "(blank: true);" : "();");
        s.AppendLine("            __Fill(model, entity);");
        s.AppendLine("            return model;");
        s.AppendLine("        }");
        s.AppendLine("    }");
        s.AppendLine();

        Keep(formModel, toModelMark);

        var plumbingMark = s.Length;

        // ---- plumbing ----
        // A model save writes what the form holds. A null clears a property the aggregate declares nullable — the user
        // emptied that field — and leaves a non-nullable one as it is, since it can only mean the form never set it. A
        // nested value-object model merges what it gives over the value object the aggregate holds.
        s.Append("    internal static void __Apply(").Append(entityType).Append(" entity, ").Append(modelType)
            .AppendLine(" model)");
        s.AppendLine("    {");
        var local = 0;
        foreach (var member in entity.Members.Where(static m => m.Role == ModelMemberRole.Value && m.Write is not null))
        {
            var given = "__v" + (local++).ToString(CultureInfo.InvariantCulture);
            var value = member.ValueObject switch
            {
                null => given,
                { SingleValue: true } single => BuildExpression(single, _ => given),
                { } valueObject => MergeExpression(given, "entity." + member.Name, valueObject, member.Nullable, ref local),
            };

            s.Append("        if (model.").Append(member.Name).Append(" is { } ").Append(given).AppendLine(")");
            s.AppendLine("        {");
            s.Append("            ").AppendLine(Assignment(member.Write!, "entity", member.Name, value));
            s.AppendLine("        }");

            if (member.Nullable)
            {
                s.AppendLine("        else");
                s.AppendLine("        {");
                s.Append("            ").AppendLine(Assignment(member.Write!, "entity", member.Name, "null"));
                s.AppendLine("        }");
            }
        }

        foreach (var child in entity.Children)
        {
            EmitChildSync(s, child);
        }

        s.AppendLine("    }");
        s.AppendLine();

        s.Append("    internal static void __Fill(").Append(modelType).Append(" model, ").Append(entityType).AppendLine(" entity)");
        s.AppendLine("    {");
        foreach (var member in entity.Members)
        {
            var value = member.ValueObject is null
                ? "entity." + member.Name
                : ToModelExpression("entity." + member.Name, member.ValueObject, member.Nullable, modelType);
            s.Append("        model.").Append(member.Name).Append(" = ").Append(value).AppendLine(";");
        }

        foreach (var child in entity.Children)
        {
            s.Append("        model.").Append(child.Name).Append(" = new global::System.Collections.Generic.List<")
                .Append(child.ChildModelName).AppendLine(">();");
            s.Append("        foreach (var __child in entity.").Append(child.Name).AppendLine(")");
            s.AppendLine("        {");
            s.Append("            var __childModel = ").Append(child.ChildModelName)
                .AppendLine("Extensions.__ToModel(__child);");
            s.AppendLine("            __childModel.Id = __child.Id;");
            s.Append("            model.").Append(child.Name).AppendLine(".Add(__childModel);");
            s.AppendLine("        }");
            s.AppendLine();
        }

        s.AppendLine("    }");

        s.AppendLine();

        // The same thing ToModel() does, callable by name: a parent fills its children through this rather than
        // through the extension member, which would have to be in scope where the parent's file is generated.
        s.Append("    internal static ").Append(modelType).Append(" __ToModel(").Append(entityType)
            .AppendLine(" entity)");
        s.AppendLine("    {");
        s.Append("        var model = new ").Append(modelType).AppendLine(entity.Constructible ? "(blank: true);" : "();");
        s.AppendLine("        __Fill(model, entity);");
        s.AppendLine("        return model;");
        s.AppendLine("    }");

        // Everything above reads or writes a MODEL, so it goes with one. Everything below is still needed:
        // a behaviour create constructs the entity through __New and stamps its key, neither of which is a
        // form write.
        Keep(formModel, plumbingMark);

        if (entity.Constructible)
        {
            s.AppendLine();
            s.Append("    internal static ").Append(entityType).AppendLine(" __Defaults() => __New();");
            s.AppendLine();
            s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind).AppendLine(".Constructor)]");
            s.Append("    private static extern ").Append(entityType).AppendLine(" __New();");
        }

        // What a parent's sync creates for a posted row that matches no stored child: a new child carrying the
        // key its own type assigns, never the one the form sent.
        if (entity.IsChild && entity.Constructible)
        {
            s.AppendLine();
            s.Append("    internal static ").Append(entityType).AppendLine(" __NewChild()");
            s.AppendLine("    {");
            AppendNewEntity(s, entity, withId: false);
            s.AppendLine("        return entity;");
            s.AppendLine("    }");
        }

        foreach (var child in entity.Children.Where(static c => c.ThroughField))
        {
            s.AppendLine();
            s.Append("    /// <summary>The field behind <c>").Append(child.Name)
                .AppendLine("</c>, which hands out a read-only view.</summary>");
            s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind)
                .Append(".Field, Name = \"").Append(child.FieldName).AppendLine("\")]");
            s.Append("    private static extern ref ").Append(child.FieldTypeName).Append(" __Field_")
                .Append(child.Name).Append('(').Append(entityType).AppendLine(" entity);");
        }

        if (entity.KeyWrite is { Kind: not ModelWriteKind.Public } key)
        {
            EmitAccessor(s, key, "Id");
        }

        var accessorMark = s.Length;

        foreach (var member in entity.Members)
        {
            if (member.Write is { Kind: not ModelWriteKind.Public } write)
            {
                EmitAccessor(s, write, member.Name);
            }
        }

        foreach (var valueObject in entity.ValueObjects)
        {
            EmitValueObjectBuild(s, valueObject);
        }

        Keep(formModel, accessorMark);

        s.AppendLine("}");
        return s.ToString();
    }

    private static void AppendKeyRemarks(StringBuilder s, Entity entity)
    {
        switch (entity.KeySource)
        {
            case KeySource.Guid:
                s.AppendLine("        /// <remarks>The key is a new version-7 <see cref=\"global::System.Guid\" />, unless the constructor already set one.</remarks>");
                break;
            case KeySource.Store:
                s.AppendLine("        /// <remarks>The key is assigned by the database when the row is inserted.</remarks>");
                break;
        }
    }

    // `var entity = __New();` and its key — the caller's for an id overload, a new version-7 Guid when nothing set one,
    // or nothing at all where the store assigns it.
    private static void AppendNewEntity(StringBuilder s, Entity entity, bool withId)
    {
        if (withId && entity.KeyIsReference)
        {
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(id);");
        }

        s.AppendLine("            var entity = __New();");

        if (withId)
        {
            s.Append("            ").AppendLine(Assignment(entity.KeyWrite!, "entity", "Id", "id"));
        }
        else if (entity.KeySource == KeySource.Guid && entity.KeyWrite is { } generatedKey)
        {
            s.Append("            if (global::System.Collections.Generic.EqualityComparer<").Append(entity.IdTypeName)
                .AppendLine(">.Default.Equals(entity.Id, default!))");
            s.AppendLine("            {");
            s.Append("                ").AppendLine(Assignment(generatedKey, "entity", "Id", entity.KeyFactory!));
            s.AppendLine("            }");
        }
    }

    // CreateAsync(model) exists only where a key can be produced without the caller (see KeySourceOf); the Guid
    // case needs a way into Entity<TId>.Id to put it there.
    private static bool IdLessCreate(Entity entity) =>
        entity.Constructible && entity.KeySource switch
        {
            KeySource.None or KeySource.Store => true,
            KeySource.Guid => entity.KeyWrite is not null,
            _ => false,
        };

    // Not for a key the store generates: an explicit value in an identity column is refused on SQL Server
    // (IDENTITY_INSERT is off) and, on PostgreSQL, accepted without moving the sequence — which then collides.
    private static bool CreateWithId(Entity entity) =>
        entity.Constructible && entity.IdTypeName is not null && entity.KeyWrite is not null &&
        entity.KeySource != KeySource.Store;

    private static string JoinWrites(List<string> writes) =>
        writes.Count == 1
            ? writes[0]
            : string.Join(", ", writes.Take(writes.Count - 1)) + " or " + writes[writes.Count - 1];

    // A value object built through a constructor or members generated code cannot reach directly: the accessors
    // it needs, and for a member-by-member build the helper that constructs it and writes each property.
    private static void EmitValueObjectBuild(StringBuilder s, ValueObjectShape shape)
    {
        switch (shape.Build)
        {
            case ModelValueObjectBuild.AccessorConstructor:
                s.AppendLine();
                s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind).AppendLine(".Constructor)]");
                s.Append("    private static extern ").Append(shape.TypeName).Append(" __New").Append(shape.ModelName).Append('(')
                    .Append(string.Join(", ", shape.ConstructorParameterTypes.Select(static (t, i) =>
                        t + " p" + i.ToString(CultureInfo.InvariantCulture))))
                    .AppendLine(");");
                break;

            case ModelValueObjectBuild.AccessorMembers:
                if (!shape.PublicConstructor)
                {
                    s.AppendLine();
                    s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind).AppendLine(".Constructor)]");
                    s.Append("    private static extern ").Append(shape.TypeName).Append(" __New").Append(shape.ModelName)
                        .AppendLine("();");
                }

                s.AppendLine();
                s.Append("    private static ").Append(shape.TypeName).Append(" __Build").Append(shape.ModelName).Append('(')
                    .Append(string.Join(", ", shape.Members.Select(static (m, i) =>
                        m.ValueTypeName + " p" + i.ToString(CultureInfo.InvariantCulture))))
                    .AppendLine(")");
                s.AppendLine("    {");
                s.Append("        var value = ")
                    .Append(shape.PublicConstructor ? "new " + shape.TypeName + "()" : "__New" + shape.ModelName + "()")
                    .AppendLine(";");
                var index = 0;
                foreach (var member in shape.Members)
                {
                    s.Append("        ").AppendLine(Assignment(
                        member.Write!, "value", member.Name, "p" + index.ToString(CultureInfo.InvariantCulture)));
                    index++;
                }

                s.AppendLine("        return value;");
                s.AppendLine("    }");

                foreach (var member in shape.Members)
                {
                    if (member.Write is { Kind: not ModelWriteKind.Public } write)
                    {
                        EmitAccessor(s, write, member.Name);
                    }
                }

                break;
        }
    }

    /// <summary>
    ///     The child reconcile: after this, the aggregate holds exactly what the posted list holds.
    /// </summary>
    /// <remarks>
    ///     Three rules, in the order they are safe to apply. A stored child whose id nobody posted is removed
    ///     first, so the list shrinks before anything is matched against it. A posted row whose id matches a
    ///     stored child updates that child. A posted row that matches nothing is a new child — including one
    ///     carrying an id that matches nothing, which is never followed: a forged id adds a row rather than
    ///     reaching one.
    /// </remarks>
    private static void EmitChildSync(StringBuilder s, ChildShape child)
    {
        var comparer = "global::System.Collections.Generic.EqualityComparer<" + child.ChildIdTypeName + ">.Default";
        var collection = child.ThroughField ? "__Field_" + child.Name + "(entity)" : "entity." + child.Name;
        var extensions = child.ChildModelName + "Extensions";

        s.AppendLine();
        s.AppendLine("        {");
        s.Append("            var __children = ").Append(collection).AppendLine(";");
        s.AppendLine();
        s.AppendLine("            if (__children is not null)");
        s.AppendLine("            {");
        s.Append("                var __posted = model.").Append(child.Name)
            .Append(" is { } __given ? __given : new global::System.Collections.Generic.List<")
            .Append(child.ChildModelName).AppendLine(">();");
        s.AppendLine();
        s.AppendLine("                foreach (var __stored in global::System.Linq.Enumerable.ToArray(__children))");
        s.AppendLine("                {");
        s.AppendLine("                    var __keep = false;");
        s.AppendLine();
        s.AppendLine("                    foreach (var __row in __posted)");
        s.AppendLine("                    {");
        s.Append("                        if (__row.Id is { } __rowId && ").Append(comparer)
            .AppendLine(".Equals(__rowId, __stored.Id))");
        s.AppendLine("                        {");
        s.AppendLine("                            __keep = true;");
        s.AppendLine("                            break;");
        s.AppendLine("                        }");
        s.AppendLine("                    }");
        s.AppendLine();
        s.AppendLine("                    if (!__keep)");
        s.AppendLine("                    {");
        s.AppendLine("                        __children.Remove(__stored);");
        s.AppendLine("                    }");
        s.AppendLine("                }");
        s.AppendLine();
        s.AppendLine("                foreach (var __row in __posted)");
        s.AppendLine("                {");
        s.Append("                    ").Append(child.ChildTypeName).AppendLine("? __target = null;");
        s.AppendLine();
        s.AppendLine("                    if (__row.Id is { } __rowId)");
        s.AppendLine("                    {");
        s.AppendLine("                        foreach (var __stored in __children)");
        s.AppendLine("                        {");
        s.Append("                            if (").Append(comparer).AppendLine(".Equals(__rowId, __stored.Id))");
        s.AppendLine("                            {");
        s.AppendLine("                                __target = __stored;");
        s.AppendLine("                                break;");
        s.AppendLine("                            }");
        s.AppendLine("                        }");
        s.AppendLine("                    }");
        s.AppendLine();
        s.AppendLine("                    if (__target is null)");
        s.AppendLine("                    {");
        s.Append("                        __target = ").Append(extensions).AppendLine(".__NewChild();");
        s.AppendLine("                        __children.Add(__target);");
        s.AppendLine("                    }");
        s.AppendLine();
        s.Append("                    ").Append(extensions).AppendLine(".__Apply(__target, __row);");
        s.AppendLine("                }");
        s.AppendLine("            }");
        s.AppendLine("        }");
    }

    private static void EmitAccessor(StringBuilder s, Write write, string memberName)
    {
        s.AppendLine();
        var attribute = write.Kind == ModelWriteKind.Setter
            ? "[" + UnsafeAccessor + "(" + UnsafeAccessorKind + ".Method, Name = \"set_" + memberName + "\")]"
            : "[" + UnsafeAccessor + "(" + UnsafeAccessorKind + ".Field, Name = \"<" + memberName + ">k__BackingField\")]";

        // A value type is reached by reference, or the accessor would write into a copy.
        var target = (write.ByRef ? "ref " : "") + write.TargetType;
        var signature = write.Kind == ModelWriteKind.Setter
            ? "void {0}(" + target + " target, " + write.ValueType + " value);"
            : "ref " + write.ValueType + " {0}(" + target + " target);";

        if (write.TypeParameters is null)
        {
            s.Append("    ").AppendLine(attribute);
            s.Append("    private static extern ").AppendLine(string.Format(CultureInfo.InvariantCulture, signature, write.AccessorName));
        }
        else
        {
            s.Append("    private static class ").Append(write.AccessorName).Append('<').Append(write.TypeParameters).Append('>')
                .AppendLine(write.Constraints);
            s.AppendLine("    {");
            s.Append("        ").AppendLine(attribute);
            s.Append("        public static extern ").AppendLine(string.Format(CultureInfo.InvariantCulture, signature, "Invoke"));
            s.AppendLine("    }");
        }
    }

    private static string Assignment(Write write, string receiver, string name, string value)
    {
        var target = (write.ByRef ? "ref " : "") + receiver;
        return write.Kind switch
        {
            ModelWriteKind.Public => receiver + "." + name + " = " + value + ";",
            ModelWriteKind.Setter when write.TypeParameters is null => write.AccessorName + "(" + target + ", " + value + ");",
            ModelWriteKind.Setter => write.AccessorName + "<" + write.TypeArguments + ">.Invoke(" + target + ", " + value + ");",
            _ when write.TypeParameters is null => write.AccessorName + "(" + target + ") = " + value + ";",
            _ => write.AccessorName + "<" + write.TypeArguments + ">.Invoke(" + target + ") = " + value + ";",
        };
    }

    // The value object built from one value per member, in whichever way this value object is built.
    private static string BuildExpression(ValueObjectShape shape, Func<ValueObjectMember, string> value)
    {
        string InConstructorOrder() =>
            string.Join(", ", shape.ConstructorOrder.Select(name => value(shape.Members.First(m => m.Name == name))));

        return shape.Build switch
        {
            ModelValueObjectBuild.Constructor => "new " + shape.TypeName + "(" + InConstructorOrder() + ")",
            ModelValueObjectBuild.Initializer => "new " + shape.TypeName + " { " + string.Join(", ",
                shape.Members.Select(m => m.Name + " = " + value(m))) + " }",
            ModelValueObjectBuild.AccessorConstructor => "__New" + shape.ModelName + "(" + InConstructorOrder() + ")",
            _ => "__Build" + shape.ModelName + "(" + string.Join(", ", shape.Members.Select(value)) + ")",
        };
    }

    // `given` is a non-null nested model; `current` is the value object the aggregate holds now, which may be null (a
    // reference type, or a nullable property). Each member is the model's value when it gives one, else the current one,
    // else the member type's default — so a model that names only Amount still builds a whole Money.
    private static string MergeExpression(string given, string current, ValueObjectShape shape, bool currentNullable, ref int local)
    {
        var currentMayBeNull = currentNullable || !shape.IsValueType;
        var access = currentMayBeNull ? "?." : ".";
        var counter = local;

        string Member(ValueObjectMember member)
        {
            var currentMember = current + access + member.Name;
            var fallback = currentMayBeNull && !member.Nullable ? " ?? default(" + member.ValueTypeName + ")!" : "";

            switch (member.ValueObject)
            {
                case null:
                    return "(" + given + "." + member.Name + " ?? " + currentMember + fallback + ")";

                case { SingleValue: true } single:
                {
                    var inner = "__v" + (counter++).ToString(CultureInfo.InvariantCulture);
                    return "(" + given + "." + member.Name + " is { } " + inner + " ? " +
                           BuildExpression(single, _ => inner) + " : " + currentMember + fallback + ")";
                }

                default:
                {
                    var inner = "__v" + (counter++).ToString(CultureInfo.InvariantCulture);
                    var merged = MergeExpression(inner, currentMember, member.ValueObject, currentMayBeNull || member.Nullable, ref counter);
                    return "(" + given + "." + member.Name + " is { } " + inner + " ? " + merged + " : " + currentMember + fallback + ")";
                }
            }
        }

        var built = BuildExpression(shape, Member);
        local = counter;
        return built;
    }

    // The model's copy of a value object: the value itself for a one-value one, a nested model otherwise.
    private static string ToModelExpression(string source, ValueObjectShape shape, bool nullable, string modelType)
    {
        var mayBeNull = nullable || !shape.IsValueType;

        if (shape.SingleValue)
        {
            return source + (mayBeNull ? "?." : ".") + shape.Members[0].Name;
        }

        var build = "new " + modelType + "." + shape.ModelName + " { " + string.Join(", ",
            shape.Members.Select(m => m.Name + " = " + (m.ValueObject is null
                ? source + "." + m.Name
                : ToModelExpression(source + "." + m.Name, m.ValueObject, m.Nullable, modelType)))) + " }";

        return mayBeNull ? "(" + source + " is null ? null : " + build + ")" : build;
    }

    // ---- the incremental model ------------------------------------------------------------------

    private enum Refusal
    {
        None,
        Nested,
        Clash,
    }

    // Who produces the key of a row CreateAsync(model) inserts. See KeySourceOf.
    private enum KeySource
    {
        None,
        Caller,
        Store,
        Guid,
    }

    private sealed record Entity(
        string FullyQualifiedName,
        string Name,
        string Namespace,
        string Accessibility,
        string? IdTypeName,
        bool Constructible,
        bool Versioned,
        Write? KeyWrite,
        KeySource KeySource,
        string? KeyFactory,
        bool KeyIsReference,
        SymbolLocation? Location,
        Refusal Refusal,
        string? RefusalDetail,
        EquatableArray<Member> Members,
        EquatableArray<ValueObjectShape> ValueObjects,
        bool IsChild,
        EquatableArray<ChildShape> Children,
        EquatableArray<string> AggregateReferences,
        EquatableArray<string> UnsyncableChildren,
        int Writes,
        bool DeclaresWrites)
    {
        public static Entity Refused(string fullyQualifiedName, string name, SymbolLocation? location, Refusal refusal, string detail) =>
            new(fullyQualifiedName, name, "", "public", null, false, false, null, KeySource.None, null, false, location, refusal, detail,
                new EquatableArray<Member>([]), new EquatableArray<ValueObjectShape>([]), false,
                new EquatableArray<ChildShape>([]), new EquatableArray<string>([]), new EquatableArray<string>([]),
                AllWrites, false);
    }

    /// <summary>One child collection, as the generator needs it: what to emit, and what to write it through.</summary>
    private sealed record ChildShape(
        string Name,
        string ChildTypeName,
        string ChildModelName,
        string ChildIdTypeName,
        bool ThroughField,
        string? FieldName,
        string FieldTypeName);

    private sealed record Member(
        string Name,
        ModelMemberRole Role,
        string ModelType,
        bool Nullable,
        ValueObjectShape? ValueObject,
        Write? Write,
        EquatableArray<string> Attributes);

    private sealed record Write(
        ModelWriteKind Kind,
        string AccessorName,
        string TargetType,
        string ValueType,
        string? TypeParameters,
        string? TypeArguments,
        string Constraints,
        bool ByRef);

    private sealed record ValueObjectShape(
        string ModelName,
        string TypeName,
        ModelValueObjectBuild Build,
        EquatableArray<string> ConstructorOrder,
        EquatableArray<string> ConstructorParameterTypes,
        bool PublicConstructor,
        bool IsValueType,
        bool SingleValue,
        EquatableArray<ValueObjectMember> Members);

    private sealed record ValueObjectMember(
        string Name,
        string ModelType,
        bool Nullable,
        ValueObjectShape? ValueObject,
        EquatableArray<string> Attributes,
        string ValueTypeName,
        Write? Write);
}

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
/// Gives every <c>Rask.Data.Model</c> a form-shaped companion — <c>ProductModel</c> for <c>Product</c> — and
/// the writes that take it: <c>Product.CreateAsync(id, model)</c>, <c>Product.CreateAsync(model)</c> where a key
/// can be produced without the caller, <c>Product.UpdateAsync(id, model)</c>, <c>Product.DeleteAsync(id, version)</c>
/// and <c>product.ToModel()</c>.
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

    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";
    private const string ValidationAttribute = "System.ComponentModel.DataAnnotations.ValidationAttribute";
    private const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    private const string UnsafeAccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind";

    private static readonly SymbolDisplayFormat TypeFormat = GeneratedModelShape.TypeFormat;

    internal static readonly DiagnosticDescriptor Rask081 = new(
        "RASK081",
        "Entity has no parameterless constructor, so CreateAsync is not generated",
        "'{0}' has no parameterless constructor, so neither '{0}.CreateAsync({0}Model)' nor "
        + "'{0}.CreateAsync(id, {0}Model)' is generated; add one — it may be private, like the one EF Core "
        + "materializes rows through",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Both generated CreateAsync overloads build the entity from its form model before inserting it, "
                     + "and they need a constructor that takes nothing to start from. Everything that works on a row "
                     + "that already exists — the model itself, ToModel, UpdateAsync and DeleteAsync — is still "
                     + "generated.",
        helpLinkUri: DiagnosticHelp.Link("RASK081"));

    internal static readonly DiagnosticDescriptor Rask082 = new(
        "RASK082",
        "A type already has the generated model's name",
        "'{1}' already exists beside the entity '{0}', so Rask cannot generate its form model or the "
        + "CreateAsync, UpdateAsync and DeleteAsync that take it; rename the existing type, declare it "
        + "'partial' to extend the generated one, or mark '{0}' [SkipModel] to generate none",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Every Rask.Data.Model gets a generated {Entity}Model in its own namespace. A hand-written, "
                     + "non-partial type of that name would collide with it as CS0101, a message that names "
                     + "neither the generator nor the way out — so the generator stands down and says why instead.",
        helpLinkUri: DiagnosticHelp.Link("RASK082"));

    internal static readonly DiagnosticDescriptor Rask083 = new(
        "RASK083",
        "Nested entity gets no generated model",
        "'{0}' is declared inside '{1}', so no '{0}Model' is generated for it; declare the entity at "
        + "namespace level to get one, or mark it [SkipModel] to say that is intended",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "The generated model and its writes are emitted beside the entity, as siblings in its "
                     + "namespace. An entity nested in another type has no such place, so it is mapped as usual "
                     + "but gets no form model.",
        helpLinkUri: DiagnosticHelp.Link("RASK083"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, ct) => GetEntity(ctx, ct))
            .Where(static entity => entity is not null)
            .Select(static (entity, _) => entity!);

        context.RegisterSourceOutput(entities.Collect(), static (spc, all) => Emit(spc, all));
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
            new EquatableArray<ValueObjectShape>(shape.ValueObjects.Select(v => ToShape(v, converted))));
    }

    // Who can produce the key of a row inserted WITHOUT one — which decides whether CreateAsync(model) exists.
    //  * A Guid: this code, as a version-7 Guid (time-ordered, so the primary-key index stays append-only), and
    //    the same inside a strongly-typed id over a Guid.
    //  * An integer: the store's identity column. Rask.Data's key convention leaves integer keys store-generated
    //    and marks every other Model<TId> key never-generated, so EF produces nothing else.
    //  * Anything else — a string, a strongly-typed id over an integer or a string — has no value the row could
    //    be keyed by that the caller did not choose, so only CreateAsync(id, model) is generated for it.
    // An entity on the non-generic Model has no single key to reason about, and keeps CreateAsync(model).
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
            valueObject is null ? property.Type.ToDisplayString(TypeFormat) : valueObject.ModelName + (member.Nullable ? "?" : ""),
            valueObject is null ? Initializer(property.Type) : member.Nullable ? "" : " = new();",
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
                nested is null ? m.Property.Type.ToDisplayString(TypeFormat) : nested.ModelName + (m.Nullable ? "?" : ""),
                nested is null ? Initializer(m.Property.Type) : m.Nullable ? "" : " = new();",
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
            new EquatableArray<ValueObjectMember>(members));

        converted[valueObject] = shape;
        return shape;
    }

    // ---- emit helpers ---------------------------------------------------------------------------
    private static string Initializer(ITypeSymbol type) =>
        !type.IsReferenceType || type.NullableAnnotation == NullableAnnotation.Annotated ? ""
        : type.SpecialType == SpecialType.System_String ? " = \"\";"
        : " = default!;";

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
        foreach (var entity in candidates.Distinct().OrderBy(static e => e.FullyQualifiedName, StringComparer.Ordinal))
        {
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
                context.ReportDiagnostic(Diagnostic.Create(Rask081, entity.Location?.ToLocation(), entity.Name));
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

        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (entity.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(entity.Namespace).AppendLine(";");
            s.AppendLine();
        }

        // ---- the model ----
        s.Append("/// <summary>The form model for <see cref=\"").Append(entityType)
            .AppendLine("\" />, generated by Rask from its mapped properties.</summary>");
        s.AppendLine("/// <remarks>");
        // Names only the writes this entity actually got — a model without a parameterless constructor has
        // no CreateAsync at all, one whose key only the caller can supply has no id-less one, and one without
        // Model<TId> has no id to create or update by.
        var idLessCreate = IdLessCreate(entity);
        var createWithId = CreateWithId(entity);
        var writes = new List<string>();
        if (idLessCreate)
        {
            writes.Add("<c>" + entity.Name + ".CreateAsync(model)</c>");
        }

        if (createWithId)
        {
            writes.Add("<c>" + entity.Name + ".CreateAsync(id, model)</c>");
        }

        if (entity.IdTypeName is not null)
        {
            writes.Add("<c>" + entity.Name + ".UpdateAsync(id, model)</c>");
        }

        s.Append("/// Bind it with <c>Form.Model(model)</c>")
            .Append(writes.Count == 0 ? "" : "; persist it with " + JoinWrites(writes))
            .AppendLine(".");
        if (entity.IdTypeName is not null)
        {
            s.AppendLine("/// It carries no id: the id is passed beside it, so a posted form cannot point a write at another row.");
        }

        s.AppendLine("/// </remarks>");
        s.Append(entity.Accessibility).Append(" sealed partial class ").AppendLine(modelName);
        s.AppendLine("{");

        foreach (var member in entity.Members)
        {
            s.Append("    /// <summary>The form value of <see cref=\"").Append(entityType).Append('.')
                .Append(member.Name).AppendLine("\" />.</summary>");
            foreach (var attribute in member.Attributes)
            {
                s.Append("    ").AppendLine(attribute);
            }

            s.Append("    public ").Append(member.ModelType).Append(' ').Append(member.Name)
                .Append(" { get; set; }").AppendLine(member.Initializer);
            s.AppendLine();
        }

        foreach (var valueObject in entity.ValueObjects)
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

                s.Append("        public ").Append(member.ModelType).Append(' ').Append(member.Name)
                    .Append(" { get; set; }").AppendLine(member.Initializer);
            }

            s.AppendLine("    }");
            s.AppendLine();
        }

        s.AppendLine("}");
        s.AppendLine();

        // ---- the members on the entity ----
        s.Append("/// <summary>The writes Rask generates for <see cref=\"").Append(entityType)
            .Append("\" />, taking its <see cref=\"").Append(modelType).AppendLine("\" />.</summary>");
        s.Append(entity.Accessibility).Append(" static class ").Append(modelName).AppendLine("Extensions");
        s.AppendLine("{");

        s.Append("    extension(").Append(entityType).AppendLine(")");
        s.AppendLine("    {");

        if (idLessCreate)
        {
            s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
                .AppendLine("\" /> built from <paramref name=\"model\" />.</summary>");
            s.AppendLine("        /// <param name=\"model\">The values to create it with.</param>");
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
            s.AppendLine("        /// <returns>The inserted entity, with its key.</returns>");
            switch (entity.KeySource)
            {
                case KeySource.Guid:
                    s.AppendLine("        /// <remarks>The key is a new version-7 <see cref=\"global::System.Guid\" />, unless the constructor already set one.</remarks>");
                    break;
                case KeySource.Store:
                    s.AppendLine("        /// <remarks>The key is assigned by the database when the row is inserted.</remarks>");
                    break;
            }

            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> CreateAsync(")
                .Append(modelType).Append(" model, ").Append(Token).AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
            s.AppendLine("            var entity = __New();");

            if (entity.KeySource == KeySource.Guid && entity.KeyWrite is { } generatedKey)
            {
                s.Append("            if (global::System.Collections.Generic.EqualityComparer<").Append(entity.IdTypeName)
                    .AppendLine(">.Default.Equals(entity.Id, default!))");
                s.AppendLine("            {");
                s.Append("                ").AppendLine(Assignment(generatedKey, "entity", "Id", entity.KeyFactory!));
                s.AppendLine("            }");
            }

            s.AppendLine("            __Apply(entity, model);");
            s.Append("            return ").Append(Writes).AppendLine(".CreateAsync(entity, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();
        }

        if (createWithId)
        {
            s.Append("        /// <summary>Inserts a new <see cref=\"").Append(entityType)
                .AppendLine("\" /> under <paramref name=\"id\" />, built from <paramref name=\"model\" />.</summary>");
            s.AppendLine("        /// <param name=\"id\">The key of the new row.</param>");
            s.AppendLine("        /// <param name=\"model\">The values to create it with.</param>");
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the save.</param>");
            s.AppendLine("        /// <returns>The inserted entity.</returns>");
            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> CreateAsync(")
                .Append(entity.IdTypeName).Append(" id, ").Append(modelType).Append(" model, ").Append(Token)
                .AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            if (entity.KeyIsReference)
            {
                s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(id);");
            }

            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
            s.AppendLine("            var entity = __New();");
            s.Append("            ").AppendLine(Assignment(entity.KeyWrite!, "entity", "Id", "id"));
            s.AppendLine("            __Apply(entity, model);");
            s.Append("            return ").Append(Writes).AppendLine(".CreateAsync(entity, cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();
        }

        if (entity.IdTypeName is { } idType)
        {
            var version = entity.Versioned ? "model.Version" : "null";

            s.Append("        /// <summary>Writes <paramref name=\"model\" /> onto the stored <see cref=\"").Append(entityType)
                .AppendLine("\" /> with <paramref name=\"id\" /> — only the values that changed.</summary>");
            s.AppendLine("        /// <param name=\"id\">The id of the row to update.</param>");
            s.AppendLine("        /// <param name=\"model\">The edited values" +
                         (entity.Versioned ? ", carrying the version they were read at" : "") + ".</param>");
            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
            s.AppendLine("        /// <returns>The updated entity.</returns>");
            s.AppendLine("        /// <exception cref=\"global::System.Collections.Generic.KeyNotFoundException\">No row has the id, or it is soft-deleted.</exception>");
            if (entity.Versioned)
            {
                s.AppendLine("        /// <exception cref=\"global::Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException\">The row was saved by someone else since it was read.</exception>");
            }

            s.Append("        public static ").Append(Task).Append('<').Append(entityType).Append("> UpdateAsync(")
                .Append(idType).Append(" id, ").Append(modelType).Append(" model, ").Append(Token)
                .AppendLine(" cancellationToken = default)");
            s.AppendLine("        {");
            s.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(model);");
            s.Append("            return ").Append(Writes).Append(".UpdateAsync<").Append(entityType)
                .Append(">(id!, ").Append(version).AppendLine(", entity => __Apply(entity, model), cancellationToken);");
            s.AppendLine("        }");
            s.AppendLine();

            s.Append("        /// <summary>Deletes the stored <see cref=\"").Append(entityType)
                .AppendLine("\" /> with <paramref name=\"id\" />, through the interceptors.</summary>");
            s.AppendLine("        /// <param name=\"id\">The id of the row to delete.</param>");
            if (entity.Versioned)
            {
                s.AppendLine("        /// <param name=\"version\">The version last read, to refuse a delete of a row saved since; <c>null</c> deletes whatever version is current.</param>");
            }

            s.AppendLine("        /// <param name=\"cancellationToken\">Cancels the load and the save.</param>");
            s.AppendLine("        /// <exception cref=\"global::System.Collections.Generic.KeyNotFoundException\">No row has the id, or it is soft-deleted.</exception>");
            s.Append("        public static ").Append(Task).Append(" DeleteAsync(").Append(idType).Append(" id, ");
            if (entity.Versioned)
            {
                s.Append("int? version = null, ");
            }

            s.Append(Token).AppendLine(" cancellationToken = default) =>");
            s.Append("            ").Append(Writes).Append(".DeleteAsync<").Append(entityType).Append(">(id!, ")
                .Append(entity.Versioned ? "version" : "null").AppendLine(", cancellationToken);");
        }

        s.AppendLine("    }");
        s.AppendLine();

        s.Append("    extension(").Append(entityType).AppendLine(" entity)");
        s.AppendLine("    {");
        s.Append("        /// <summary>Copies this entity into a new <see cref=\"").Append(modelType)
            .AppendLine("\" />, ready to bind to an edit form.</summary>");
        s.Append("        public ").Append(modelType).Append(" ToModel() => new ").Append(modelType).AppendLine("()");
        s.AppendLine("        {");
        foreach (var member in entity.Members)
        {
            s.Append("            ").Append(member.Name).Append(" = ")
                .Append(member.ValueObject is null
                    ? "entity." + member.Name
                    : ToModelExpression("entity." + member.Name, member.ValueObject, member.Nullable, modelType))
                .AppendLine(",");
        }

        s.AppendLine("        };");
        s.AppendLine("    }");
        s.AppendLine();

        // ---- plumbing ----
        s.Append("    private static void __Apply(").Append(entityType).Append(" entity, ").Append(modelType)
            .AppendLine(" model)");
        s.AppendLine("    {");
        foreach (var member in entity.Members.Where(static m => m.Role == ModelMemberRole.Value && m.Write is not null))
        {
            var value = member.ValueObject is null
                ? "model." + member.Name
                : ToEntityExpression("model." + member.Name, member.ValueObject, member.Nullable);
            s.Append("        ").AppendLine(Assignment(member.Write!, "entity", member.Name, value));
        }

        s.AppendLine("    }");

        if (entity.Constructible)
        {
            s.AppendLine();
            s.Append("    [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessorKind).AppendLine(".Constructor)]");
            s.Append("    private static extern ").Append(entityType).AppendLine(" __New();");
        }

        if (entity.KeyWrite is { Kind: not ModelWriteKind.Public } key)
        {
            EmitAccessor(s, key, "Id");
        }

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

        s.AppendLine("}");
        return s.ToString();
    }

    // CreateAsync(model) exists only where a key can be produced without the caller (see KeySourceOf); the Guid
    // case needs a way into Model<TId>.Id to put it there.
    private static bool IdLessCreate(Entity entity) =>
        entity.Constructible && entity.KeySource switch
        {
            KeySource.None or KeySource.Store => true,
            KeySource.Guid => entity.KeyWrite is not null,
            _ => false,
        };

    private static bool CreateWithId(Entity entity) =>
        entity.Constructible && entity.IdTypeName is not null && entity.KeyWrite is not null;

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

    private static string ToEntityExpression(string source, ValueObjectShape shape, bool nullable)
    {
        string Value(ValueObjectMember member) =>
            member.ValueObject is null
                ? source + "." + member.Name
                : ToEntityExpression(source + "." + member.Name, member.ValueObject, member.Nullable);

        string InConstructorOrder() =>
            string.Join(", ", shape.ConstructorOrder.Select(name => Value(shape.Members.First(m => m.Name == name))));

        var build = shape.Build switch
        {
            ModelValueObjectBuild.Constructor => "new " + shape.TypeName + "(" + InConstructorOrder() + ")",
            ModelValueObjectBuild.Initializer => "new " + shape.TypeName + " { " + string.Join(", ",
                shape.Members.Select(m => m.Name + " = " + Value(m))) + " }",
            ModelValueObjectBuild.AccessorConstructor => "__New" + shape.ModelName + "(" + InConstructorOrder() + ")",
            _ => "__Build" + shape.ModelName + "(" + string.Join(", ", shape.Members.Select(Value)) + ")",
        };

        return nullable ? "(" + source + " is null ? null : " + build + ")" : build;
    }

    private static string ToModelExpression(string source, ValueObjectShape shape, bool nullable, string modelType)
    {
        var build = "new " + modelType + "." + shape.ModelName + " { " + string.Join(", ",
            shape.Members.Select(m => m.Name + " = " + (m.ValueObject is null
                ? source + "." + m.Name
                : ToModelExpression(source + "." + m.Name, m.ValueObject, m.Nullable, modelType)))) + " }";

        return nullable ? "(" + source + " is null ? null : " + build + ")" : build;
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
        EquatableArray<ValueObjectShape> ValueObjects)
    {
        public static Entity Refused(string fullyQualifiedName, string name, SymbolLocation? location, Refusal refusal, string detail) =>
            new(fullyQualifiedName, name, "", "public", null, false, false, null, KeySource.None, null, false, location, refusal, detail,
                new EquatableArray<Member>([]), new EquatableArray<ValueObjectShape>([]));
    }

    private sealed record Member(
        string Name,
        ModelMemberRole Role,
        string ModelType,
        string Initializer,
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
        EquatableArray<ValueObjectMember> Members);

    private sealed record ValueObjectMember(
        string Name,
        string ModelType,
        string Initializer,
        bool Nullable,
        ValueObjectShape? ValueObject,
        EquatableArray<string> Attributes,
        string ValueTypeName,
        Write? Write);
}

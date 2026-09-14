using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// Gives every <c>Rask.Data.Model</c> a form-shaped companion — <c>ProductModel</c> for <c>Product</c>.
/// </summary>
/// <remarks>
/// <para>
/// The model is a plain mutable class a form binds to, carrying the entity's validation attributes, so
/// <c>Form.Model(model)</c> checks input by the entity's own rules. It is only a shape: nothing is generated that
/// reads an entity into it or writes it back. A write is plain EF Core — a handler loads the entity and applies
/// the values it was sent.
/// </para>
/// <para>
/// <b>The model carries no key.</b> It is what a form posts back, so a key on it would be one the client
/// chooses. The id travels beside it.
/// </para>
/// </remarks>
[Generator]
public sealed class ModelInputGenerator : IIncrementalGenerator
{
    /// <summary>The suffix the generated model takes after the entity's name.</summary>
    internal const string ModelSuffix = GeneratedModelShape.ModelSuffix;

    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";
    private const string ValidationAttribute = "System.ComponentModel.DataAnnotations.ValidationAttribute";

    private static readonly SymbolDisplayFormat TypeFormat = GeneratedModelShape.TypeFormat;

    internal static readonly DiagnosticDescriptor Rask082 = new(
        "RASK082",
        "A type already has the generated model's name",
        "'{1}' already exists beside the entity '{0}', so Rask cannot generate its form model; rename the "
        + "existing type, declare it 'partial' to extend the generated one, or mark '{0}' [SkipModel] to "
        + "generate none",
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
        description: "The generated model is emitted beside the entity, as a sibling in its namespace. An entity "
                     + "nested in another type has no such place, so it is mapped as usual but gets no form model.",
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

        return new Entity(
            name,
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            location,
            Refusal.None,
            null,
            new EquatableArray<Member>(shape.Members.Select(m => ToMember(m, converted))),
            new EquatableArray<ValueObjectShape>(shape.ValueObjects.Select(v => ToShape(v, converted))));
    }

    private static Member ToMember(ModelMember member, Dictionary<ModelValueObject, ValueObjectShape> converted)
    {
        var property = member.Property;
        var valueObject = member.ValueObject is null ? null : ToShape(member.ValueObject, converted);

        return new Member(
            property.Name,
            valueObject is null ? property.Type.ToDisplayString(TypeFormat) : valueObject.ModelName + (member.Nullable ? "?" : ""),
            valueObject is null ? Initializer(property.Type) : member.Nullable ? "" : " = new();",
            new EquatableArray<string>(property.GetAttributes().Where(IsCopiedAttribute).Select(RenderAttribute)));
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
            return new Member(
                m.Property.Name,
                nested is null ? m.Property.Type.ToDisplayString(TypeFormat) : nested.ModelName + (m.Nullable ? "?" : ""),
                nested is null ? Initializer(m.Property.Type) : m.Nullable ? "" : " = new();",
                new EquatableArray<string>(m.Property.GetAttributes().Where(IsCopiedAttribute).Select(RenderAttribute)));
        }).ToList();

        var shape = new ValueObjectShape(
            valueObject.ModelName,
            valueObject.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat),
            new EquatableArray<Member>(members));

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

            var hint = entity.FullyQualifiedName.Replace("global::", "") + ModelSuffix + ".g.cs";
            context.AddSource(hint, SourceText.From(Render(entity), Encoding.UTF8));
        }
    }

    private static string Render(Entity entity)
    {
        var modelName = entity.Name + ModelSuffix;
        var entityType = entity.FullyQualifiedName;

        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (entity.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(entity.Namespace).AppendLine(";");
            s.AppendLine();
        }

        s.Append("/// <summary>The form model for <see cref=\"").Append(entityType)
            .AppendLine("\" />, generated by Rask from its mapped properties.</summary>");
        s.AppendLine("/// <remarks>");
        s.AppendLine("/// Bind it with <c>Form.Model(model)</c>; it validates by the entity's own attributes. It carries no id,");
        s.AppendLine("/// so a posted form cannot point a write at another row.");
        s.AppendLine("/// </remarks>");
        s.Append(entity.Accessibility).Append(" sealed partial class ").AppendLine(modelName);
        s.AppendLine("{");

        foreach (var member in entity.Members)
        {
            s.Append("    /// <summary>The form value of <see cref=\"").Append(entityType).Append('.')
                .Append(member.Name).AppendLine("\" />.</summary>");
            AppendProperty(s, "    ", member);
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
                AppendProperty(s, "        ", member);
            }

            s.AppendLine("    }");
            s.AppendLine();
        }

        s.AppendLine("}");
        return s.ToString();
    }

    private static void AppendProperty(StringBuilder s, string indent, Member member)
    {
        foreach (var attribute in member.Attributes)
        {
            s.Append(indent).AppendLine(attribute);
        }

        s.Append(indent).Append("public ").Append(member.ModelType).Append(' ').Append(member.Name)
            .Append(" { get; set; }").AppendLine(member.Initializer);
    }

    // ---- the incremental model ------------------------------------------------------------------

    private enum Refusal
    {
        None,
        Nested,
        Clash,
    }

    private sealed record Entity(
        string FullyQualifiedName,
        string Name,
        string Namespace,
        string Accessibility,
        SymbolLocation? Location,
        Refusal Refusal,
        string? RefusalDetail,
        EquatableArray<Member> Members,
        EquatableArray<ValueObjectShape> ValueObjects)
    {
        public static Entity Refused(string fullyQualifiedName, string name, SymbolLocation? location, Refusal refusal, string detail) =>
            new(fullyQualifiedName, name, "", "public", location, refusal, detail,
                new EquatableArray<Member>([]), new EquatableArray<ValueObjectShape>([]));
    }

    private sealed record Member(
        string Name,
        string ModelType,
        string Initializer,
        EquatableArray<string> Attributes);

    private sealed record ValueObjectShape(
        string ModelName,
        string TypeName,
        EquatableArray<Member> Members);
}

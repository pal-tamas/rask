using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// What each field of a generated model is, told to the controls bound to it (RaskValidation.RegisterFieldShapes): a
// value object's declared rules (`X.__Field`), an enum's members and their labels, another row's id. A plain string,
// number, date or bool says what it is through its own type and is not registered.
public sealed partial class ModelInputGenerator
{
    private const string FieldShapeType = "global::Rask.Core.Forms.FieldShape";
    private const string FieldKindType = "global::Rask.Core.Forms.FieldKind";
    private const string EndsOf = "__EndsOf";

    /// <summary>One model type's fields that have a shape to tell, each with the C# that makes it.</summary>
    private sealed record ShapedModel(string Type, string Key, List<(string Property, string Shape)> Fields);

    // The C# of a member's shape, read where its type is in hand: a value object declared from a base record
    // answers its own `__Field`; an enum lists its members. Null for any other type.
    private static string? ShapeOf(ITypeSymbol type, ValueObjectShape? valueObject)
    {
        if (valueObject is { ValueBase: "Text" or "Number" or "Integer" or "Day" or "Moment" or "Range" })
        {
            return valueObject.TypeName + ".__Field";
        }

        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

        return held is INamedTypeSymbol { TypeKind: TypeKind.Enum } choice ? ChoiceOf(choice) : null;
    }

    // An enum's members in declaration order, each labelled by its [Display(Name)] else its name: written out here so
    // nothing reflects over the enum when a control lists them.
    private static string ChoiceOf(INamedTypeSymbol choice)
    {
        var type = choice.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var options = choice.GetMembers().OfType<IFieldSymbol>()
            .Where(static f => f.IsConst)
            .Select(f => "new global::Rask.Core.Forms.FieldOption(" + type + "." + f.Name + ", " + ValidationAttributeSource.NameOf(f) + ")");

        return "new " + FieldShapeType + "(" + FieldKindType + ".Choice, Options: new global::Rask.Core.Forms.FieldOption[] { "
               + string.Join(", ", options) + " })";
    }

    // The aggregates' strongly-typed ids — what a field holding another row's id is declared as. A Guid or an int key is
    // the framework's own type and tells nothing.
    private static EquatableArray<string> TypedIdsOf(List<Entity> entities) =>
        new(entities
            .Select(static e => e.IdTypeName)
            .OfType<string>()
            .Where(static id => id.StartsWith("global::", StringComparison.Ordinal) && !id.StartsWith("global::System.", StringComparison.Ordinal))
            .Select(static id => id.TrimEnd('?'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal));

    // The typed ids one root alone is keyed by: each gets `id.Load(…)` for that root. One that two roots share gets
    // none, since `id.Load` would not say which.
    private static HashSet<string> LoadableIdsOf(List<Entity> entities, EquatableArray<string> typedIds) =>
        new(entities
                .Where(static e => !e.IsChild && e.Refusal == Refusal.None && e.IdTypeName is not null)
                .GroupBy(static e => e.IdTypeName!.TrimEnd('?'), StringComparer.Ordinal)
                .Where(g => g.Count() == 1 && typedIds.Contains(g.Key, StringComparer.Ordinal))
                .Select(static g => g.Key),
            StringComparer.Ordinal);

    // The root model and each nested value-object model with at least one field that has a shape.
    private static IEnumerable<ShapedModel> ShapedModels(Entity entity)
    {
        var root = entity.Members
            .Where(static m => m.Role == ModelMemberRole.Value)
            .Select(m => (m.Name, Shape: MemberShape(entity, m.Shape, m.ValueObject)))
            .Where(static f => f.Shape is not null)
            .Select(static f => (f.Name, f.Shape!))
            .ToList();

        if (root.Count > 0)
        {
            yield return new ShapedModel(FormModelType(entity), "Root_", root);
        }

        foreach (var nested in entity.ValueObjects.Where(static v => !v.SingleValue))
        {
            var ends = RangeEndNames(nested).ToList();
            var fields = nested.Members
                .Select(m => (m.Name, Shape: MemberShape(entity, m.Shape, m.ValueObject) ??
                                             (ends.Contains(m.Name, StringComparer.Ordinal) ? EndsOf + "(" + nested.TypeName + ".__Field)" : null)))
                .Where(static f => f.Shape is not null)
                .Select(static f => (f.Name, f.Shape!))
                .ToList();

            if (fields.Count > 0)
            {
                yield return new ShapedModel(FormModelType(entity) + "." + nested.ModelName, nested.ModelName + "_", fields);
            }
        }
    }

    // A value object that is an aggregate's id is another row's id, whatever it holds.
    private static string? MemberShape(Entity entity, string? shape, ValueObjectShape? valueObject) =>
        valueObject is { SingleValue: true } held && entity.TypedIds.Contains(held.TypeName, StringComparer.Ordinal)
            ? "new " + FieldShapeType + "(" + FieldKindType + ".Id)"
            : shape;

    private static void AppendShapeRegistrations(StringBuilder s, List<ShapedModel> shaped)
    {
        foreach (var model in shaped)
        {
            s.Append("        ").Append(RaskValidation).Append(".RegisterFieldShapes(typeof(").Append(model.Type)
                .Append("), ").Append(model.Key).AppendLine("ShapeOf);");
        }
    }

    // A switch per model type, asked once per field and its answer kept by Core.
    private static void AppendShapes(StringBuilder s, List<ShapedModel> shaped)
    {
        foreach (var model in shaped)
        {
            s.AppendLine();
            s.Append("    private static ").Append(FieldShapeType).Append("? ").Append(model.Key).AppendLine("ShapeOf(string property) =>");
            s.AppendLine("        property switch");
            s.AppendLine("        {");
            foreach (var (property, shape) in model.Fields)
            {
                s.Append("            ").Append(SymbolDisplay.FormatLiteral(property, quote: true)).Append(" => ").Append(shape).AppendLine(",");
            }

            s.AppendLine("            _ => null,");
            s.AppendLine("        };");
        }

        AppendWithin(s, shaped);
        if (shaped.Exists(static m => m.Fields.Exists(static f => f.Shape.StartsWith(EndsOf, StringComparison.Ordinal))))
        {
            s.AppendLine();
            s.AppendLine("    // One end of a range: of the ends' kind, within the range's bounds; nothing for ends of no kind a control knows.");
            s.Append("    private static ").Append(FieldShapeType).Append("? ").Append(EndsOf).Append('(').Append(FieldShapeType).AppendLine(" range) =>");
            s.AppendLine("        range.Ends is { } kind ? new(kind, Min: range.Min, Max: range.Max) : null;");
        }
    }
}

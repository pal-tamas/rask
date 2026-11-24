using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// A value object of several values as a parameter — `Range`, `record Opening(DateOnly? Date, decimal? Quantity) : Value`
// — is a group of fields on the operation's form, one per member (`f.Opening.Date`), each told what the value object's
// stated rules and its members' attributes say, and made through its Create, a refusal under `Opening.Quantity`.
public sealed partial class OperationGenerator
{
    private const string EndOf = "__EndOf";

    /// <summary>The fields of a value object of several values: a nested class of the form's model.</summary>
    /// <param name="Model">The nested class: <c>OpeningFields</c>.</param>
    /// <param name="Type">The value object.</param>
    /// <param name="Members">Its members, in the order its Create takes them.</param>
    /// <param name="Range">Whether it is a <c>Range</c>: its two ends are told the range's kind and bounds.</param>
    private sealed record FormGroup(string Model, string Type, EquatableArray<FormField> Members, bool Range);

    private static FormField? GroupFieldOf(IParameterSymbol parameter, INamedTypeSymbol aggregate, string input)
    {
        var property = Capitalised(parameter.Name);
        var stored = parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is INamedTypeSymbol type &&
                     PropertyNamed(aggregate, property) is { } kept && Same(kept.Type, type)
            ? kept
            : null;
        return GroupOf(property, parameter.Type, parameter.NullableAnnotation == NullableAnnotation.Annotated, stored, "aggregate", input, property + "Fields");
    }

    /// <summary>
    ///     A value object of several values as a group of fields named <paramref name="property" />: a parameter's, or a row
    ///     member's (<c>f.Rows[i].Start.Date</c>), filled from <paramref name="stored" /> on <paramref name="owner" />.
    /// </summary>
    private static FormField? GroupOf(
        string property, ITypeSymbol declared, bool optional, IPropertySymbol? stored, string owner, string input, string model)
    {
        if (declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is not INamedTypeSymbol type ||
            AggregateShape.DeclaredValueBaseOf(type) is not ("Value" or "Range") ||
            OneValueOf(type) is not null ||
            PrimaryParameters(type) is not { Length: > 1 } positional)
        {
            return null;
        }

        var value = "__" + property;
        var stated = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var range = string.Equals(AggregateShape.DeclaredValueBaseOf(type), "Range", System.StringComparison.Ordinal);
        var members = new System.Collections.Generic.List<FormField>();
        foreach (var member in positional)
        {
            var held = PropertyNamed(type, member.Name);
            var optionalMember = member.NullableAnnotation == NullableAnnotation.Annotated ||
                                 member.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            if (held is null || MemberField(member.Name, member.Type, optionalMember, held, value, lift: true) is not { } field)
            {
                return null;
            }

            var shape = range && field.ValueObject is null ? EndOf + "(" + stated + ".__Field)" : StatedShape(field, member.Type, stated, held);
            members.Add(field with { Shape = shape });
        }

        var fills = members.Where(static m => m.Fill is not null).Select(static m => m.Property + " = " + m.Fill).ToList();
        var fill = stored is null
            ? null
            : owner + "." + stored.Name + " is { } " + value + " ? new " + input + "." + model + " { " + string.Join(", ", fills) + " } : new()";

        return new FormField(property, model, null, optional, Required: false, Text: false, fill)
        {
            Group = new FormGroup(model, stated, new EquatableArray<FormField>(members), range),
            // A range's field is the range: `Ui.Field.Bind(() => f.Km)` draws both ends under one label.
            Shape = range ? stated + ".__Field" : null,
        };
    }

    // The parameters of a record's primary constructor, which its generated Create takes in their order.
    private static IParameterSymbol[]? PrimaryParameters(INamedTypeSymbol type) =>
        type.InstanceConstructors
            .FirstOrDefault(static c => c.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is RecordDeclarationSyntax))
            ?.Parameters.ToArray();

    // `Opening.Create(date, quantity, "Opening")` from the group's fields: nothing for an optional one left empty, a
    // required member left empty refused under its path, and Create's own refusals under `Opening.Member`.
    private static void AppendGroupMade(StringBuilder s, string input, Operation operation, FormField field)
    {
        var group = field.Group!;
        s.AppendLine();
        s.Append("    private static ").Append(group.Type).Append("? ").Append(operation.Name).Append(field.Property).Append("Made(")
            .Append(input).Append('.').Append(group.Model).Append(" given, string at, ").Append(FailureListFqn).AppendLine(" failures)");
        s.AppendLine("    {");
        if (field.Optional)
        {
            s.Append("        if (").Append(string.Join(" && ", group.Members.Select(static m => Empty(m, "given." + m.Property)))).AppendLine(")");
            s.AppendLine("        {");
            s.AppendLine("            return null;");
            s.AppendLine("        }");
            s.AppendLine();
        }

        s.AppendLine("        var __before = failures.Count;");
        foreach (var member in group.Members)
        {
            s.Append("        var __m_").Append(member.Property).Append(" = ")
                .Append(MadeFrom(member, "given." + member.Property, "at + " + SymbolDisplay.FormatLiteral("." + member.Property, quote: true)))
                .AppendLine(";");
        }

        s.Append("        return failures.Count == __before ? global::Rask.Data.GeneratedOperations.Given(() => ").Append(group.Type)
            .Append(".Create(").Append(string.Join(", ", group.Members.Select(static m => "__m_" + m.Property))).AppendLine(", at), failures) : null;");
        s.AppendLine("    }");
    }

    // One end of a range: of the ends' kind, within the range's bounds; nothing for ends of no kind a control knows.
    private static void AppendEndOf(StringBuilder s)
    {
        s.AppendLine();
        s.Append("    private static ").Append(FieldShapeFqn).Append("? ").Append(EndOf).Append('(').Append(FieldShapeFqn).AppendLine(" range) =>");
        s.AppendLine("        range.Ends is { } kind ? new(kind, Min: range.Min, Max: range.Max) : null;");
    }
}

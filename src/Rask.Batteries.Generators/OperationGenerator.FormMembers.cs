using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// One value a field of an operation's form holds — a parameter, a member of a value object of several values, a member
// of a row — and how it is made back into what the operation takes: held as it is, or a value object of one value held
// as that value and made through its Create, every refusal under the field's path.
public sealed partial class OperationGenerator
{
    private const string FieldShapeFqn = "global::Rask.Core.Forms.FieldShape";
    private const string FieldKindFqn = "global::Rask.Core.Forms.FieldKind";

    /// <summary>A value a field holds, or null for a type no field holds.</summary>
    /// <param name="property">The field's name.</param>
    /// <param name="type">What the operation takes for it.</param>
    /// <param name="optional">Whether it may be null.</param>
    /// <param name="held">The owner's property of that name, read to fill the field; null for none.</param>
    /// <param name="owner">The C# the owner is read off: <c>aggregate</c>, a row's child, a value object.</param>
    /// <param name="lift">
    ///     Whether a value type other than a <c>bool</c> is held as its nullable twin, so it starts
    ///     empty — a new row, an empty group, a parameter nothing prefills — and a field left so is refused as required
    ///     rather than taken as zero.
    /// </param>
    private static FormField? MemberField(string property, ITypeSymbol type, bool optional, IPropertySymbol? held, string owner, bool lift)
    {
        if (type is INamedTypeSymbol named && OneValueOf(named) is { } inner)
        {
            var text = inner.SpecialType == SpecialType.System_String;
            var valueObject = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new FormField(
                property,
                text ? "string?" : inner.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(TypeFormat) + "?",
                valueObject,
                optional,
                !optional && !(text && AggregateShape.AllowsEmptyText(named)),
                text,
                AggregateShape.IsPassword(named) ? null : FillOf(held, owner, named, inner))
            {
                Shape = DeclaresShape(named) ? valueObject + ".__Field" : null,
            };
        }

        if (!IsFormValue(type))
        {
            return null;
        }

        // A bool is never empty — an unticked box is false — so it is held as it is.
        var lifted = lift && !optional && type.IsValueType && type.SpecialType != SpecialType.System_Boolean && type.TypeKind != TypeKind.Error;
        var declared = type.ToDisplayString(TypeFormat);
        // A plain value is the domain's to refuse (its Refuse, its attributes), told by the dry run: no required rule of
        // the form's own, unless it is held lifted and so may be left empty.
        return new FormField(
            property,
            lifted ? declared + "?" : declared,
            null,
            optional,
            Required: lifted,
            type.SpecialType == SpecialType.System_String,
            FillOf(held, owner, type, null))
        {
            Lifted = lifted,
        };
    }

    // What the owner holds alike, read for the field: the same type, or a value object's one value (`.Value`).
    private static string? FillOf(IPropertySymbol? held, string owner, ITypeSymbol type, ITypeSymbol? inner)
    {
        if (held is null)
        {
            return null;
        }

        var path = owner + "." + held.Name;
        if (inner is not null && Same(held.Type, type))
        {
            var optional = held.NullableAnnotation == NullableAnnotation.Annotated ||
                           held.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            return path + (optional ? "?.Value" : ".Value");
        }

        var taken = inner ?? type;
        var bare = taken is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : taken;
        return Same(held.Type, taken) || Same(held.Type, bare) ? path : null;
    }

    private static bool Same(ITypeSymbol left, ITypeSymbol right) =>
        SymbolEqualityComparer.Default.Equals(
            left.WithNullableAnnotation(NullableAnnotation.None),
            right.WithNullableAnnotation(NullableAnnotation.None));

    // An instance property of that name, declared on the type or a base of it: a child's Id is its Entity base's.
    private static IPropertySymbol? PropertyNamed(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(static p => !p.IsStatic && !p.IsIndexer) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // What a plain member's field is told: its kind, and what the rules stated for it on `owner` add (FluentValidation's
    // RuleFor), tightened by what its attributes say. A value object of one value tells its own (`X.__Field`).
    private static string? StatedShape(FormField field, ITypeSymbol type, string? owner, IPropertySymbol? annotated)
    {
        if (field.Shape is { } own)
        {
            return own;
        }

        var shaped = owner is not null && FieldShapeHints.KnownKindOf(type) is { } kind
            ? "global::Rask.Core.Forms.StatedRules.Shape(typeof(" + owner + "), " + SymbolDisplay.FormatLiteral(field.Property, quote: true) +
              ", new " + FieldShapeFqn + "(" + FieldKindFqn + "." + kind + "))"
            : null;
        return annotated is null ? shaped : FieldShapeHints.Annotated(annotated, shaped);
    }

    /// <summary>The C# that makes what the operation takes from what the field holds, a refusal added to <c>failures</c>.</summary>
    /// <param name="field">The field.</param>
    /// <param name="given">The C# of what it holds: <c>input.Name</c>, <c>__row.Tonnes</c>.</param>
    /// <param name="name">The C# of its path, under which a refusal is shown: <c>"Name"</c>, <c>__at + ".Tonnes"</c>.</param>
    private static string MadeFrom(FormField field, string given, string name)
    {
        if (field.ValueObject is not { } valueObject)
        {
            return field.Lifted
                ? "global::Rask.Data.GeneratedOperations.Given(() => " + given + " is { } __given ? __given : throw " + Missing(name) + ", failures)"
                : given;
        }

        string make;
        if (field.Optional)
        {
            make = valueObject + ".CreateOptional(" + given + ", " + name + ")";
        }
        else if (field.Text)
        {
            make = valueObject + ".Create(" + given + " ?? \"\", " + name + ")";
        }
        else
        {
            make = given + " is { } __given ? " + valueObject + ".Create(__given, " + name + ") : throw " + Missing(name);
        }

        return "global::Rask.Data.GeneratedOperations.Given(() => " + make + ", failures)" + (field.Optional ? "" : "!");
    }

    // A refusal for a field left empty that may not be.
    private static string Missing(string name) =>
        "new global::Rask.Cqrs.RaskValidationException(new " + FailureFqn + "[] { new " + FailureFqn + "(global::Rask.Rules.Required, new[] { " + name + " }) })";

    // Whether a field holds nothing: blank text, or null.
    private static string Empty(FormField field, string given)
    {
        if (field.Text)
        {
            return "string.IsNullOrWhiteSpace(" + given + ")";
        }

        return field.PropertyType.EndsWith("?", System.StringComparison.Ordinal) ? given + " is null" : "false";
    }
}

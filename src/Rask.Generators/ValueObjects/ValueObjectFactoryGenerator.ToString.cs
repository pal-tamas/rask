using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

// `ToString()` for a value object of one member declared straight from Value — `FuelingChip(string Value) : Value`: the
// value it holds, as the one-value bases (Text, Number, Whole, Day, Moment) print theirs, rather than the record's
// `FuelingChip { Value = … }`. A number or a date is printed culture-invariant, so the same value reads the same in a
// log, a key or a file name on every machine. One the record declares itself is left alone.
public sealed partial class ValueObjectFactoryGenerator
{
    private static void AppendToString(StringBuilder s, ValueObject value)
    {
        if (!string.Equals(value.ValueBase, "Value", System.StringComparison.Ordinal) || value.Parameters.Count != 1 || value.OwnToString)
        {
            return;
        }

        var held = value.Parameters[0];
        s.AppendLine();
        s.Append("    /// <summary>The <c>").Append(held.Name).AppendLine("</c> it holds, as text.</summary>");
        s.AppendLine("    /// <returns>The value it holds, a number culture-invariant and a date as ISO 8601; empty for none.</returns>");
        s.Append("    public override string ToString() => ").Append(Printed(held)).AppendLine(";");
    }

    // A text as it is; anything else through ToString — IFormattable's, culture-invariant, where it has one. Null (a
    // nullable member, or a reference whose ToString says null) prints as empty.
    private static string Printed(Parameter held)
    {
        if (held.IsText)
        {
            return held.Name + (held.Nullable ? " ?? \"\"" : "");
        }

        var format = held.Iso is { } iso ? "\"" + iso + "\"" : "null";
        var call = held.Formattable ? ".ToString(" + format + ", global::System.Globalization.CultureInfo.InvariantCulture)" : ".ToString()";
        if (held.IsValueType && !held.Nullable)
        {
            return held.Name + call;
        }

        return held.Name + "?" + call + " ?? \"\"";
    }

    // A date as ISO 8601, as Day and Moment print theirs: `2026-01-31`, `2026-01-31T14:05:00`.
    private static string? IsoFormat(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;
        return held.ToDisplayString() switch
        {
            "System.DateOnly" => "yyyy-MM-dd",
            "System.DateTime" => "yyyy-MM-ddTHH:mm:ss",
            _ => null,
        };
    }

    // A ToString() of its own, declared in any part of the record.
    private static bool DeclaresToString(INamedTypeSymbol type) =>
        type.GetMembers("ToString").OfType<IMethodSymbol>().Any(static m => m is { Parameters.Length: 0, IsImplicitlyDeclared: false });

    // IFormattable, itself or under Nullable<T>: printed through ToString(null, culture). An enum is IFormattable too,
    // but its provider is obsolete and never read: it prints its member's name through ToString().
    private static bool IsFormattable(ITypeSymbol type)
    {
        var held = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;
        return held.TypeKind != TypeKind.Enum && held.AllInterfaces.Any(static i => string.Equals(i.ToDisplayString(), "System.IFormattable", System.StringComparison.Ordinal));
    }
}

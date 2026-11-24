using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators;

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
        s.AppendLine("    /// <returns>The value it holds, a number or a date written culture-invariant; empty for none.</returns>");
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

        var call = held.Formattable ? ".ToString(null, global::System.Globalization.CultureInfo.InvariantCulture)" : ".ToString()";
        if (held.IsValueType && !held.Nullable)
        {
            return held.Name + call;
        }

        return held.Name + "?" + call + " ?? \"\"";
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

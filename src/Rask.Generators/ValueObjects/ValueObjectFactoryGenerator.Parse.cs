using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

// `Parse(text, format?)` and `TryParse` for a value object declared from Moment or Day — `FuelingMoment(DateTime Value) :
// Moment(Value)`: the text read culture-invariant onto the app's wall clock (Clock.Zone; an offset converted from, none
// taken as the wall's), then made through Create, so its rules are asked as they are for any other value. One the record
// declares itself is left alone, and so is one over a nullable member, which has no single value to read text into.
public sealed partial class ValueObjectFactoryGenerator
{
    private static void AppendParse(StringBuilder s, ValueObject value)
    {
        if (value.OwnParse || !value.OneValue || value.Parameters[0].Nullable)
        {
            return;
        }

        var (read, what) = ParsedAs(value);
        if (read is null)
        {
            return;
        }

        var name = value.Name;
        s.AppendLine();
        s.Append("    /// <summary>The ").Append(what).Append(" <paramref name=\"text\" /> writes, as a <see cref=\"").Append(name)
            .AppendLine("\" /> — read culture-invariant onto the app's wall clock (<c>Clock.Zone</c>), once its rules accept it.</summary>");
        s.AppendLine("    /// <param name=\"text\">ISO 8601 (<c>2026-11-23T08:35:00</c>; a moment with <c>Z</c> or an offset is converted to the wall clock), or as <paramref name=\"format\" /> writes it.</param>");
        s.AppendLine("    /// <param name=\"format\">The exact format it is written in (<c>yyyy.MM.dd. HH:mm</c>), culture-invariant; null for ISO 8601.</param>");
        AppendFieldParameter(s);
        s.AppendLine("    /// <returns>The value, its rules met.</returns>");
        s.Append("    /// <exception cref=\"global::System.FormatException\">The text is no ").Append(what).AppendLine(".</exception>");
        s.AppendLine("    /// <exception cref=\"global::Rask.Cqrs.RaskValidationException\">A rule refuses it.</exception>");
        s.Append("    public static ").Append(name).AppendLine(" Parse(string text, string? format = null, string? field = null) =>");
        s.Append("        Create(").Append(Rules).Append(".Parse").Append(read).AppendLine("(text, format), field);");

        s.AppendLine();
        s.Append("    /// <summary>The ").Append(what)
            .AppendLine(" <paramref name=\"text\" /> writes, as <see cref=\"Parse\" /> reads it; false when it is none or a rule refuses it.</summary>");
        s.AppendLine("    /// <param name=\"text\">The text, ISO 8601.</param>");
        s.AppendLine("    /// <param name=\"result\">The value when the text is one its rules accept; otherwise null.</param>");
        s.AppendLine("    /// <returns>Whether it was read.</returns>");
        s.Append("    public static bool TryParse(string? text, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ").Append(name)
            .AppendLine("? result) => TryParse(text, null, out result);");

        s.AppendLine();
        s.Append("    /// <summary>The ").Append(what)
            .AppendLine(" <paramref name=\"text\" /> writes in <paramref name=\"format\" />; false when it is none or a rule refuses it.</summary>");
        s.AppendLine("    /// <param name=\"text\">The text.</param>");
        s.AppendLine("    /// <param name=\"format\">The exact format it is written in, culture-invariant; null for ISO 8601.</param>");
        s.AppendLine("    /// <param name=\"result\">The value when the text is one its rules accept; otherwise null.</param>");
        s.AppendLine("    /// <returns>Whether it was read.</returns>");
        s.Append("    public static bool TryParse(string? text, string? format, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ")
            .Append(name).AppendLine("? result)");
        s.AppendLine("    {");
        s.AppendLine("        result = null;");
        s.Append("        return ").Append(Rules).Append(".TryParse").Append(read)
            .AppendLine("(text, format, out var __read) && TryCreate(__read, out result, out _);");
        s.AppendLine("    }");
    }

    // What a value is read from text as: a Moment over a DateTime, a Day over a DateOnly; nothing for any other.
    private static (string? Read, string? What) ParsedAs(ValueObject value) =>
        (value.ValueBase, value.Parameters[0].Iso) switch
        {
            ("Moment", "yyyy-MM-ddTHH:mm:ss") => ("Moment", "moment"),
            ("Day", "yyyy-MM-dd") => ("Day", "day"),
            _ => (null, null),
        };

    // A Parse or TryParse of its own, declared in any part of the record.
    private static bool DeclaresParse(INamedTypeSymbol type) =>
        type.GetMembers("Parse").Concat(type.GetMembers("TryParse")).OfType<IMethodSymbol>().Any(static m => !m.IsImplicitlyDeclared);
}

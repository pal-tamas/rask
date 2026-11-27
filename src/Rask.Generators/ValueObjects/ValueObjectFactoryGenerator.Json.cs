using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

// How System.Text.Json writes a value object declared from Moment — `FuelingMoment(DateTime Value) : Moment(Value)`: as
// the UTC instant it holds (Rask.Values.MomentJsonConverter), not as an object whose wall-clock Value collapses the
// hour the clock shows twice in autumn. System.Text.Json reads no [JsonConverter] off a base type, so each record is
// given its own, naming a nested subclass that makes it — a plain `new`, no reflection, so it trims and runs AOT. One
// the record names itself is left alone, and so is a generic record or one over a nullable member.
public sealed partial class ValueObjectFactoryGenerator
{
    private const string JsonConverterName = "__Json";

    private static bool HasJson(ValueObject value) =>
        !value.OwnJson && value.OneValue && !value.Generic && !value.Parameters[0].Nullable &&
        string.Equals(value.ValueBase, "Moment", System.StringComparison.Ordinal) &&
        string.Equals(value.Parameters[0].Iso, "yyyy-MM-ddTHH:mm:ss", System.StringComparison.Ordinal);

    // The attribute, on the record's own generated part.
    private static void AppendJsonAttribute(StringBuilder s, ValueObject value)
    {
        if (HasJson(value))
        {
            s.Append("[global::System.Text.Json.Serialization.JsonConverter(typeof(").Append(value.Name).Append('.')
                .Append(JsonConverterName).AppendLine("))]");
        }
    }

    // The converter it names, nested in the record so it can make it whatever the record's accessibility.
    private static void AppendJsonConverter(StringBuilder s, ValueObject value)
    {
        if (!HasJson(value))
        {
            return;
        }

        var name = value.Name;
        s.AppendLine();
        s.Append("    /// <summary>Writes and reads a <see cref=\"").Append(name)
            .AppendLine("\" /> as the UTC instant it holds, for System.Text.Json.</summary>");
        s.Append("    internal sealed class ").Append(JsonConverterName).Append(" : global::Rask.Values.MomentJsonConverter<").Append(name)
            .AppendLine(">");
        s.AppendLine("    {");
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override ").Append(name).Append(" Make(global::System.DateTime moment) => new ").Append(name)
            .AppendLine("(moment);");
        s.AppendLine("    }");
    }

    // A [JsonConverter] of its own, declared in any part of the record.
    private static bool DeclaresJson(INamedTypeSymbol type) =>
        type.GetAttributes().Any(static a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), "System.Text.Json.Serialization.JsonConverterAttribute", System.StringComparison.Ordinal));
}

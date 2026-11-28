using System.Globalization;
using System.Text;
using Rask.Generators.Shared;

namespace Rask.Generators;

// How System.Text.Json writes a value object of one value — `DriverName(string Value) : Text(Value)`, `Litres(decimal
// Value) : Number(Value)`, `Rank(int Value) : Value`: as the value it holds (Rask.Values.ValueJsonConverter), `"Ada"`
// rather than `{"Value":"Ada"}`, as Rask's own wires carry it; one declared from Moment as the UTC instant it holds
// (Rask.Values.MomentJsonConverter), so the hour the clock shows twice in autumn survives. System.Text.Json reads no
// [JsonConverter] off a base type, so each record is given its own, naming a nested subclass that makes it — a plain
// `new`, no reflection, so it trims and runs AOT. Which records get one is ValueObjectJson's to say, for the API client's
// codec agrees with it: never a password, a generic record, one over a nullable member, or one that names its own.
public sealed partial class ValueObjectFactoryGenerator
{
    private static bool HasJson(ValueObject value) => value.Json is not null && value.OneValue && !value.Generic;

    // The attribute, on the record's own generated part.
    private static void AppendJsonAttribute(StringBuilder s, ValueObject value)
    {
        if (HasJson(value))
        {
            s.Append("[global::System.Text.Json.Serialization.JsonConverter(typeof(").Append(value.Name).Append('.')
                .Append(ValueObjectJson.ConverterName).AppendLine("))]");
        }
    }

    // The converter it names, nested in the record so it can make it whatever the record's accessibility.
    private static void AppendJsonConverter(StringBuilder s, ValueObject value)
    {
        if (!HasJson(value))
        {
            return;
        }

        if (value.Json!.Moment)
        {
            AppendMomentConverter(s, value.Name);
            return;
        }

        var json = value.Json;
        var name = value.Name;
        s.AppendLine();
        s.Append("    /// <summary>Writes and reads a <see cref=\"").Append(name)
            .AppendLine("\" /> as the value it holds, for System.Text.Json.</summary>");
        s.Append("    internal sealed class ").Append(ValueObjectJson.ConverterName).Append(" : global::Rask.Values.ValueJsonConverter<")
            .Append(name).Append(", ").Append(json.HeldType).AppendLine(">");
        s.AppendLine("    {");
        s.AppendLine("        /// <summary>Names the member the value is held in.</summary>");
        s.Append("        public ").Append(ValueObjectJson.ConverterName).Append("() : base(\"").Append(json.Member).AppendLine("\") { }");
        s.AppendLine();
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override ").Append(json.HeldType)
            .Append(" ReadHeld(ref global::System.Text.Json.Utf8JsonReader reader, string property) => ").Append(json.Read).AppendLine(";");
        s.AppendLine();
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override void WriteHeld(global::System.Text.Json.Utf8JsonWriter writer, ").Append(name).Append(" value) => ")
            .Append(string.Format(CultureInfo.InvariantCulture, json.Write, "value." + json.Member)).AppendLine(";");
        s.AppendLine();
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override ").Append(name).Append(" Make(").Append(json.HeldType).Append(" held) => new ").Append(name)
            .AppendLine("(held);");
        s.AppendLine("    }");
    }

    private static void AppendMomentConverter(StringBuilder s, string name)
    {
        s.AppendLine();
        s.Append("    /// <summary>Writes and reads a <see cref=\"").Append(name)
            .AppendLine("\" /> as the UTC instant it holds, for System.Text.Json.</summary>");
        s.Append("    internal sealed class ").Append(ValueObjectJson.ConverterName).Append(" : global::Rask.Values.MomentJsonConverter<").Append(name)
            .AppendLine(">");
        s.AppendLine("    {");
        s.AppendLine("        /// <inheritdoc />");
        s.Append("        protected override ").Append(name).Append(" Make(global::System.DateTime moment) => new ").Append(name)
            .AppendLine("(moment);");
        s.AppendLine("    }");
    }
}

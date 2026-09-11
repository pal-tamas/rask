using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.External.PackageIslands;

/// <summary>
///     Writes the C# a resolved package island needs: its generated props, their writers and bridges, and
///     the enums, records and unions declared beside it.
/// </summary>
/// <remarks>
///     <para>
///         Separate from <see cref="PropsWriterEmitter" />, which walks <c>WireType</c> trees built from
///         Roslyn symbols. A generated enum or record does not exist as a symbol in the pass that generates it,
///         so its writer is emitted straight from the snapshot's mapped types instead of pretending to be one.
///     </para>
///     <para>
///         Every name and literal that came from the snapshot goes through
///         <see cref="SymbolDisplay.FormatLiteral(string, bool)" /> or <see cref="PackageIslandNaming" />, never
///         into the output verbatim: the snapshot was extracted from whatever a package shipped.
///     </para>
/// </remarks>
internal sealed class PackageIslandEmitter
{
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string ValueKind = "global::System.Text.Json.JsonValueKind";

    private readonly PackageIsland _island;
    private readonly string _access;
    private readonly Dictionary<string, GeneratedType> _typesByFqn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _writers = new(StringComparer.Ordinal);
    private readonly StringBuilder _writerMethods = new();
    private int _next;

    public PackageIslandEmitter(IslandFacts facts, PackageIsland island)
    {
        _island = island;
        _access = facts.IsPublic ? "public" : "internal";

        var prefix = facts.Namespace is null ? "global::" : "global::" + facts.Namespace + ".";
        foreach (var type in island.Types)
        {
            _typesByFqn[prefix + type.Name] = type;
        }
    }

    /// <summary>The writer methods emitted so far, ready to drop into the island's partial.</summary>
    public string WriterMethods => _writerMethods.ToString();

    /// <summary>The declaration of one generated prop.</summary>
    public string Declaration(PackageProp prop, bool needsNew)
    {
        var sb = new StringBuilder();
        sb.Append(PackageIslandNaming.Summary(prop.Doc, $"Sent to the package as `{prop.Wire}`.", prop.Default, "    "));
        sb.Append("    public ");
        if (needsNew)
        {
            sb.Append("new ");
        }

        if (prop.IsRequired)
        {
            sb.Append("required ");
        }

        sb.Append(prop.ChainTypeFqn).Append(' ').Append(prop.ClrName).AppendLine(" { get; set; }");
        return sb.ToString();
    }

    /// <summary>The statements that write one generated value prop into <c>writer</c>.</summary>
    public string WriteValue(PackageProp prop)
    {
        var type = prop.Type!;
        var key = Literal(prop.Wire);
        var access = "this." + prop.ClrName;
        var sb = new StringBuilder();

        if (!prop.IsRequired)
        {
            // Omitted, not written as null: an unset prop has to leave the package's own default in place,
            // which a JSON null would override.
            sb.AppendLine($"        if ({access} is not null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            writer.WritePropertyName({key});");
            sb.AppendLine($"            {EnsureWriter(type)}(writer, {Unwrap(access, type)});");
            sb.AppendLine("        }");
            return sb.ToString();
        }

        sb.AppendLine($"        writer.WritePropertyName({key});");
        if (prop.Nullable)
        {
            sb.AppendLine($"        if ({access} is null) {{ writer.WriteNullValue(); }}");
            sb.AppendLine($"        else {{ {EnsureWriter(type)}(writer, {Unwrap(access, type)}); }}");
        }
        else
        {
            sb.AppendLine($"        {EnsureWriter(type)}(writer, {access});");
        }

        return sb.ToString();
    }

    /// <summary>The statements that write one callback prop's handler reference into <c>writer</c>.</summary>
    /// <param name="clrName">The property holding the callback.</param>
    /// <param name="wire">The JSON key.</param>
    /// <param name="argIndex">The argument position the client forwards, or -1 for none.</param>
    /// <param name="registered">
    ///     The expression registered as the handler: a bridge that reads the argument, the delegate a
    ///     <c>Callback</c> carries, or a hand-declared delegate itself.
    /// </param>
    public static string WriteCallback(string clrName, string wire, int argIndex, string registered)
    {
        var access = "this." + clrName;
        var sb = new StringBuilder();
        sb.AppendLine($"        if ({access} is not null)");
        sb.AppendLine("        {");
        sb.AppendLine($"            writer.WritePropertyName({Literal(wire)});");
        sb.AppendLine("            writer.WriteStartObject();");
        sb.AppendLine($"            writer.WriteString(\"$h\", global::Rask.External.ExternalHandlers.Register(this, {registered}));");

        // Always present on a package island, even when empty. Without it the client forwards every
        // argument, and a package's first argument is usually a DOM or synthetic event — which carries
        // `view: window`, so JSON.stringify throws and the click never reaches C#.
        sb.AppendLine("            writer.WriteStartArray(\"$a\");");
        if (argIndex >= 0)
        {
            sb.AppendLine($"            writer.WriteNumberValue({argIndex.ToString(CultureInfo.InvariantCulture)});");
        }

        sb.AppendLine("            writer.WriteEndArray();");
        sb.AppendLine("            writer.WriteEndObject();");
        sb.AppendLine("        }");
        return sb.ToString();
    }

    /// <summary>The bridge that reads a callback prop's forwarded argument and invokes it.</summary>
    public string Bridge(PackageProp prop)
    {
        var callback = prop.Callback!;
        var argType = callback.ArgType!;
        var invoke = $"this.{prop.ClrName}!.Value.Invoke(__v) ?? {Task}.CompletedTask";

        var sb = new StringBuilder();
        sb.AppendLine($"    /// <summary>Feeds the package's argument to <c>{prop.ClrName}</c> from the dispatched frame.</summary>");
        sb.AppendLine($"    private global::System.Func<global::System.Text.Json.JsonElement, {Task}> __Arg{prop.ClrName} => __p =>");
        sb.AppendLine("    {");
        sb.AppendLine($"        if (__p.ValueKind != {ValueKind}.Object");
        sb.AppendLine("            || !__p.TryGetProperty(\"args\", out var __a)");
        sb.AppendLine($"            || __a.ValueKind != {ValueKind}.Array");
        sb.AppendLine("            || __a.GetArrayLength() == 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            return {Task}.CompletedTask;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var __e = __a[0];");

        var fqn = callback.ArgNullable ? argType.NullableFqn : argType.Fqn;
        if (callback.ArgNullable)
        {
            sb.AppendLine($"        if (__e.ValueKind == {ValueKind}.Null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            {fqn} __n = null;");
            sb.AppendLine($"            return this.{prop.ClrName}!.Value.Invoke(__n) ?? {Task}.CompletedTask;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // A value of the wrong kind is dropped rather than coerced. It can only come from a snapshot that no
        // longer matches the package, and handing C# a default it never sent is worse than not calling it.
        switch (argType.Kind)
        {
            case "string":
                sb.AppendLine($"        if (__e.ValueKind != {ValueKind}.String) {{ return {Task}.CompletedTask; }}");
                sb.AppendLine($"        {fqn} __v = __e.GetString()!;");
                break;

            case "number":
                sb.AppendLine($"        if (__e.ValueKind != {ValueKind}.Number) {{ return {Task}.CompletedTask; }}");
                sb.AppendLine($"        {fqn} __v = __e.GetDouble();");
                break;

            case "boolean":
                sb.AppendLine($"        if (__e.ValueKind is not ({ValueKind}.True or {ValueKind}.False)) {{ return {Task}.CompletedTask; }}");
                sb.AppendLine($"        {fqn} __v = __e.GetBoolean();");
                break;

            case "enum":
            {
                var generated = _typesByFqn[argType.Fqn];
                sb.AppendLine($"        {fqn} __v;");
                var first = true;
                foreach (var member in generated.EnumMembers)
                {
                    sb.Append(first ? "        if (" : "        else if (")
                        .Append(Matches(member.Literal))
                        .Append(") { __v = ").Append(argType.Fqn).Append('.').Append(member.Member).AppendLine("; }");
                    first = false;
                }

                // An unknown literal is not invoked: a stale snapshot must not hand C# the wrong member.
                sb.AppendLine($"        else {{ return {Task}.CompletedTask; }}");
                break;
            }
        }

        sb.AppendLine();
        sb.AppendLine($"        return {invoke};");
        sb.AppendLine("    };");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>The generated types, at namespace level, with the island's accessibility.</summary>
    public string Types()
    {
        var sb = new StringBuilder();
        foreach (var type in _island.Types)
        {
            sb.AppendLine();
            sb.AppendLine("/// <summary>");
            sb.Append("///     ").AppendLine(type.Summary);
            sb.AppendLine("/// </summary>");

            switch (type.Kind)
            {
                case "enum":
                    sb.Append(_access).Append(" enum ").AppendLine(type.Name);
                    sb.AppendLine("{");
                    foreach (var member in type.EnumMembers)
                    {
                        var shown = member.Literal.IsNumber || member.Literal.IsBoolean
                            ? member.Literal.Text
                            : "\"" + member.Literal.Text + "\"";
                        sb.Append("    /// <summary>Sent as <c>")
                            .Append(PackageIslandNaming.Escape(PackageIslandNaming.SingleLine(shown)))
                            .AppendLine("</c>.</summary>");
                        sb.Append("    ").Append(member.Member).AppendLine(",");
                        sb.AppendLine();
                    }

                    sb.AppendLine("}");
                    break;

                case "record":
                    sb.Append(_access).Append(" sealed record ").AppendLine(type.Name);
                    sb.AppendLine("{");
                    foreach (var member in type.RecordMembers)
                    {
                        sb.Append(PackageIslandNaming.Summary(member.Doc, $"Sent to the package as `{member.Wire}`.", null, "    "));
                        sb.Append("    public ");
                        if (member.Required)
                        {
                            sb.Append("required ");
                        }

                        sb.Append(member.Required && !member.Nullable ? member.Type.Fqn : member.Type.NullableFqn)
                            .Append(' ').Append(member.ClrName).AppendLine(" { get; init; }");
                        sb.AppendLine();
                    }

                    sb.AppendLine("}");
                    break;

                case "union":
                    sb.Append(_access).Append(" readonly record struct ").AppendLine(type.Name);
                    sb.AppendLine("{");
                    sb.AppendLine("    private readonly string? _text;");
                    sb.AppendLine("    private readonly double _number;");
                    sb.AppendLine("    private readonly bool _isNumber;");
                    sb.AppendLine();
                    sb.AppendLine($"    private {type.Name}(string? text, double number, bool isNumber)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        _text = text;");
                    sb.AppendLine("        _number = number;");
                    sb.AppendLine("        _isNumber = isNumber;");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    /// <summary>The value as a string.</summary>");
                    sb.AppendLine($"    public static implicit operator {type.Name}(string text) => new(text, 0, false);");
                    sb.AppendLine();
                    sb.AppendLine("    /// <summary>The value as a number.</summary>");
                    sb.AppendLine($"    public static implicit operator {type.Name}(double number) => new(null, number, true);");
                    sb.AppendLine();
                    sb.AppendLine($"    internal void __Write({Writer} writer)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        if (_isNumber) { writer.WriteNumberValue(_number); }");
                    sb.AppendLine("        else if (_text is null) { writer.WriteNullValue(); }");
                    sb.AppendLine("        else { writer.WriteStringValue(_text); }");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>The name of the writer method for <paramref name="type" />, emitting it the first time.</summary>
    private string EnsureWriter(CsType type)
    {
        var key = type.Kind + "|" + type.Fqn;
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        // Registered before the body is emitted, so a type that reaches itself resolves to the same method.
        var id = "PW" + _next++.ToString(CultureInfo.InvariantCulture);
        _writers[key] = id;

        var body = new StringBuilder();
        switch (type.Kind)
        {
            case "string":
                body.AppendLine("        writer.WriteStringValue(value);");
                break;

            case "number":
                body.AppendLine("        writer.WriteNumberValue(value);");
                break;

            case "boolean":
                body.AppendLine("        writer.WriteBooleanValue(value);");
                break;

            case "date":
                // Tagged rather than a bare string, so the client revives exactly this value into a Date and
                // never guesses at a string that merely looks like a timestamp.
                body.AppendLine("        writer.WriteStartObject();");
                body.AppendLine("        writer.WriteString(\"$d\", value);");
                body.AppendLine("        writer.WriteEndObject();");
                break;

            case "union":
                body.AppendLine("        value.__Write(writer);");
                break;

            case "enum":
            {
                body.AppendLine("        switch (value)");
                body.AppendLine("        {");
                foreach (var member in _typesByFqn[type.Fqn].EnumMembers)
                {
                    body.Append("            case ").Append(type.Fqn).Append('.').Append(member.Member).Append(": ")
                        .Append(WriteLiteral(member.Literal)).AppendLine(" break;");
                }

                body.AppendLine("            default: writer.WriteNullValue(); break;");
                body.AppendLine("        }");
                break;
            }

            case "list":
            {
                var element = EnsureWriter(type.Element!);
                body.AppendLine("        writer.WriteStartArray();");
                body.AppendLine("        foreach (var item in value)");
                body.AppendLine("        {");
                body.Append(WriteMaybeNull("item", type.Element!, type.ElementNullable, element, "            "));
                body.AppendLine("        }");
                body.AppendLine("        writer.WriteEndArray();");
                break;
            }

            case "map":
            {
                var element = EnsureWriter(type.Element!);
                body.AppendLine("        writer.WriteStartObject();");
                body.AppendLine("        foreach (var pair in value)");
                body.AppendLine("        {");
                body.AppendLine("            writer.WritePropertyName(pair.Key);");
                body.Append(WriteMaybeNull("pair.Value", type.Element!, type.ElementNullable, element, "            "));
                body.AppendLine("        }");
                body.AppendLine("        writer.WriteEndObject();");
                break;
            }

            case "record":
            {
                body.AppendLine("        writer.WriteStartObject();");
                foreach (var member in _typesByFqn[type.Fqn].RecordMembers)
                {
                    var memberWriter = EnsureWriter(member.Type);
                    var access = "value." + member.ClrName;
                    if (!member.Required)
                    {
                        body.AppendLine($"        if ({access} is not null)");
                        body.AppendLine("        {");
                        body.AppendLine($"            writer.WritePropertyName({Literal(member.Wire)});");
                        body.AppendLine($"            {memberWriter}(writer, {Unwrap(access, member.Type)});");
                        body.AppendLine("        }");
                    }
                    else
                    {
                        body.AppendLine($"        writer.WritePropertyName({Literal(member.Wire)});");
                        body.Append(WriteMaybeNull(access, member.Type, member.Nullable, memberWriter, "        "));
                    }
                }

                body.AppendLine("        writer.WriteEndObject();");
                break;
            }
        }

        _writerMethods.AppendLine($"    private static void {id}({Writer} writer, {type.Fqn} value)");
        _writerMethods.AppendLine("    {");
        _writerMethods.Append(body);
        _writerMethods.AppendLine("    }");
        _writerMethods.AppendLine();
        return id;
    }

    private static string WriteMaybeNull(string access, CsType type, bool nullable, string writer, string indent)
    {
        if (!nullable)
        {
            return $"{indent}{writer}(writer, {access});\n";
        }

        return $"{indent}if ({access} is null) {{ writer.WriteNullValue(); }}\n"
               + $"{indent}else {{ {writer}(writer, {Unwrap(access, type)}); }}\n";
    }

    // .Value for a Nullable<T>; a nullable reference needs only the null-forgiving operator once the null
    // branch has run.
    private static string Unwrap(string access, CsType type) =>
        type.IsValueType ? access + ".Value" : access + "!";

    private static string WriteLiteral(SnapshotLiteral literal)
    {
        if (literal.IsBoolean)
        {
            return $"writer.WriteBooleanValue({literal.Text});";
        }

        if (literal.IsNumber)
        {
            return $"writer.WriteNumberValue({Number(literal.Text)});";
        }

        return $"writer.WriteStringValue({Literal(literal.Text)});";
    }

    private static string Matches(SnapshotLiteral literal)
    {
        if (literal.IsBoolean)
        {
            return $"__e.ValueKind == {ValueKind}.{(literal.Text == "true" ? "True" : "False")}";
        }

        if (literal.IsNumber)
        {
            return $"__e.ValueKind == {ValueKind}.Number && __e.GetDouble() == {Number(literal.Text)}";
        }

        return $"__e.ValueKind == {ValueKind}.String && __e.ValueEquals({Literal(literal.Text)})";
    }

    // Round-tripped through double so the emitted literal is always valid C#, whatever spelling the JSON
    // used for the number.
    private static string Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value.ToString("R", CultureInfo.InvariantCulture) + "d"
            : "0d";

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}

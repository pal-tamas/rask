using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rask.Cqrs.Generators;

/// <summary>
///     Turns a <see cref="WireType" /> tree into the C# that reads and writes it.
/// </summary>
/// <remarks>
///     <para>
///         Every shape gets a matched pair of static methods — <c>W{n}</c> writes a value, <c>R{n}</c>
///         reads one — and composite shapes call the pair belonging to their parts. Uniformity is the
///         point: there is one calling convention (a writer is handed the value, a reader is positioned
///         on it and hands back the value), so nesting a list of maps of records inside another record
///         needs no special case anywhere.
///     </para>
///     <para>
///         The pair is registered under the shape's name <em>before</em> its body is emitted, so a type
///         that refers to itself through a collection resolves to the same pair rather than recursing
///         forever. Direct self-reference is rejected earlier, by the classifier.
///     </para>
/// </remarks>
internal sealed class WireCodecEmitter
{
    private readonly StringBuilder _methods = new();
    private readonly Dictionary<string, string> _emitted = new();
    private int _next;

    /// <summary>The generated methods, ready to drop into the codec class.</summary>
    public string Methods => _methods.ToString();

    /// <summary>
    ///     Returns the name of the method pair for <paramref name="type" />, emitting it if this is the
    ///     first time the shape has been asked for.
    /// </summary>
    /// <param name="type">The shape to encode.</param>
    /// <returns>The suffix shared by the <c>W</c> and <c>R</c> methods.</returns>
    public string Ensure(WireType type)
    {
        var key = Key(type);
        if (_emitted.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var id = _next++.ToString(CultureInfo.InvariantCulture);
        _emitted[key] = id;

        // Bodies are built into locals first: emitting a nested shape appends to _methods too, and
        // interleaving the two would produce a method inside a method.
        var write = new StringBuilder();
        var read = new StringBuilder();
        EmitBodies(type, write, read);

        _methods.AppendLine($"    private static void W{id}({Writer} writer, {type.Fqn} value, {FileList} files)");
        _methods.AppendLine("    {");
        _methods.Append(write);
        _methods.AppendLine("    }");
        _methods.AppendLine();
        _methods.AppendLine($"    private static {type.Fqn} R{id}(ref {Reader} reader, {FileListRead} files, string property)");
        _methods.AppendLine("    {");
        _methods.Append(read);
        _methods.AppendLine("    }");
        _methods.AppendLine();

        return id;
    }

    private void EmitBodies(WireType type, StringBuilder write, StringBuilder read)
    {
        switch (type.Kind)
        {
            case WireKind.Scalar:
                write.AppendLine($"        {Format(type.WriteExpression!, "value")};");
                read.AppendLine($"        return {type.ReadExpression}(ref reader, property);");
                break;

            case WireKind.Enum:
                write.AppendLine("        writer.WriteNumberValue((long)value);");
                read.AppendLine($"        return ({type.Fqn})global::Rask.Cqrs.WireJson.ReadInt64(ref reader, property);");
                break;

            case WireKind.Bytes:
                // WriteBase64StringValue takes a span, and a null array widens to an empty one — which would
                // turn "absent" into "zero bytes" silently. The explicit branch keeps null meaning null.
                write.AppendLine("        if (value is null) { writer.WriteNullValue(); return; }");
                write.AppendLine("        writer.WriteBase64StringValue(value);");
                read.AppendLine("        return global::Rask.Cqrs.WireJson.ReadBytes(ref reader, property);");
                break;

            case WireKind.File:
                // The file itself leaves the JSON entirely; what stays behind is its index in the multipart
                // body, and -1 for "there wasn't one".
                // A RaskFile in, a RaskFile out - the message never mentions the wire type. The conversion
                // is emitted HERE, in the consumer's compilation, because this is the one place that sees
                // both Rask.Core and Rask.Cqrs; neither package has to reference the other for it.
                //
                // The file's own Size is passed as the read ceiling. RaskFile.OpenReadStream defaults to
                // 512 KB to stop an unbounded read of a browser-supplied file, but the size is already
                // known here, so the ceiling can be the file itself rather than a guess that truncates
                // anything larger.
                write.AppendLine("        if (value is null) { writer.WriteNumberValue(-1); return; }");
                write.AppendLine(
                    "        files.Add(global::Rask.Cqrs.RemoteFile.FromStream("
                    + "value.Name, value.ContentType, value.Size, "
                    + "__ct => value.OpenReadStream(value.Size, __ct), "
                    + "value.LastModified));");
                write.AppendLine("        writer.WriteNumberValue(files.Count - 1);");
                read.AppendLine(
                    "        var __wire = global::Rask.Cqrs.WireJson.ResolveFile("
                    + "files, global::Rask.Cqrs.WireJson.ReadInt32(ref reader, property), property);");
                read.AppendLine("        return __wire is null ? null : new __RaskCqrsUploadedFile(__wire);");
                break;

            case WireKind.Nullable:
            {
                var inner = Ensure(type.Inner!);
                write.AppendLine("        if (!value.HasValue) { writer.WriteNullValue(); return; }");
                write.AppendLine($"        W{inner}(writer, value.Value, files);");
                read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
                read.AppendLine($"        return R{inner}(ref reader, files, property);");
                break;
            }

            case WireKind.Sequence:
            {
                var element = Ensure(type.Inner!);
                write.AppendLine("        if (value is null) { writer.WriteNullValue(); return; }");
                write.AppendLine("        writer.WriteStartArray();");
                write.AppendLine("        foreach (var item in value)");
                write.AppendLine("        {");
                write.AppendLine($"            W{element}(writer, item, files);");
                write.AppendLine("        }");
                write.AppendLine();
                write.AppendLine("        writer.WriteEndArray();");

                read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
                read.AppendLine("        global::Rask.Cqrs.WireJson.ExpectStartArray(ref reader, property);");
                read.AppendLine($"        var items = new global::System.Collections.Generic.List<{type.Inner!.Fqn}>();");
                read.AppendLine($"        while (reader.Read() && reader.TokenType != {TokenType}.EndArray)");
                read.AppendLine("        {");
                read.AppendLine($"            items.Add(R{element}(ref reader, files, property));");
                read.AppendLine("        }");
                read.AppendLine();
                read.AppendLine(type.Sequence == SequenceShape.Array
                    ? "        return items.ToArray();"
                    : "        return items;");
                break;
            }

            case WireKind.Dictionary:
            {
                var value = Ensure(type.Inner!);
                write.AppendLine("        if (value is null) { writer.WriteNullValue(); return; }");
                write.AppendLine("        writer.WriteStartObject();");
                write.AppendLine("        foreach (var pair in value)");
                write.AppendLine("        {");
                write.AppendLine("            writer.WritePropertyName(pair.Key);");
                write.AppendLine($"            W{value}(writer, pair.Value, files);");
                write.AppendLine("        }");
                write.AppendLine();
                write.AppendLine("        writer.WriteEndObject();");

                read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
                read.AppendLine("        global::Rask.Cqrs.WireJson.ExpectStartObject(ref reader, property);");
                read.AppendLine(
                    "        var map = new global::System.Collections.Generic.Dictionary<global::System.String, "
                    + $"{type.Inner!.Fqn}>();");
                read.AppendLine($"        while (reader.Read() && reader.TokenType != {TokenType}.EndObject)");
                read.AppendLine("        {");
                read.AppendLine("            var key = reader.GetString();");
                read.AppendLine("            reader.Read();");
                read.AppendLine($"            map[key] = R{value}(ref reader, files, key);");
                read.AppendLine("        }");
                read.AppendLine();
                read.AppendLine("        return map;");
                break;
            }

            case WireKind.Object:
                EmitObject(type, write, read);
                break;
        }
    }

    private void EmitObject(WireType type, StringBuilder write, StringBuilder read)
    {
        var members = new List<(WireMember Member, string Id)>();
        foreach (var member in type.Members)
        {
            members.Add((member, Ensure(member.Type)));
        }

        if (type.Symbol?.IsReferenceType == true)
        {
            write.AppendLine("        if (value is null) { writer.WriteNullValue(); return; }");
        }

        write.AppendLine("        writer.WriteStartObject();");
        foreach (var (member, id) in members)
        {
            write.AppendLine($"        writer.WritePropertyName({Literal(member.WireName)});");
            write.AppendLine($"        W{id}(writer, value.{member.ClrName}, files);");
        }

        write.AppendLine("        writer.WriteEndObject();");

        if (type.Symbol?.IsReferenceType == true)
        {
            read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
        }

        read.AppendLine("        global::Rask.Cqrs.WireJson.ExpectStartObject(ref reader, property);");
        foreach (var (member, _) in members)
        {
            read.AppendLine($"        {member.Type.Fqn} v_{member.ClrName} = default;");
        }

        read.AppendLine($"        while (reader.Read() && reader.TokenType != {TokenType}.EndObject)");
        read.AppendLine("        {");
        read.AppendLine("            var name = reader.GetString();");
        read.AppendLine("            reader.Read();");
        read.AppendLine("            switch (name)");
        read.AppendLine("            {");
        foreach (var (member, id) in members)
        {
            read.AppendLine($"                case {Literal(member.WireName)}:");
            read.AppendLine($"                    v_{member.ClrName} = R{id}(ref reader, files, {Literal(member.WireName)});");
            read.AppendLine("                    break;");
        }

        // An unknown property is skipped rather than rejected: that is what lets a sender add a field
        // without breaking a receiver compiled before it existed.
        read.AppendLine("                default:");
        read.AppendLine("                    global::Rask.Cqrs.WireJson.SkipValue(ref reader);");
        read.AppendLine("                    break;");
        read.AppendLine("            }");
        read.AppendLine("        }");
        read.AppendLine();

        if (type.ConstructorParameters is { Count: > 0 } parameters)
        {
            var arguments = new List<string>();
            foreach (var parameter in parameters)
            {
                var match = type.Members.Find(m =>
                    string.Equals(m.ClrName, parameter, System.StringComparison.OrdinalIgnoreCase));
                arguments.Add(match is null ? "default" : $"v_{match.ClrName}");
            }

            read.AppendLine($"        return new {type.Fqn}({string.Join(", ", arguments)});");
            return;
        }

        read.AppendLine($"        return new {type.Fqn}");
        read.AppendLine("        {");
        foreach (var (member, _) in members)
        {
            read.AppendLine($"            {member.ClrName} = v_{member.ClrName},");
        }

        read.AppendLine("        };");
    }

    // The shape's identity for reuse. Two properties of the same type share one pair; the kind is in the
    // key because a byte[] and a List<byte> would otherwise collide on nothing but their element type.
    private static string Key(WireType type) => type.Kind + "|" + type.Fqn;

    private static string Format(string template, string value) => template.Replace("{0}", value);

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string Reader = "global::System.Text.Json.Utf8JsonReader";
    private const string TokenType = "global::System.Text.Json.JsonTokenType";
    private const string FileList = "global::System.Collections.Generic.IList<global::Rask.Cqrs.RemoteFile>";
    private const string FileListRead = "global::System.Collections.Generic.IReadOnlyList<global::Rask.Cqrs.RemoteFile>";
}

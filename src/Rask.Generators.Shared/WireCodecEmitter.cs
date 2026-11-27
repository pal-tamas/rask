using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Rask.Generators.Shared;

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
    private readonly bool _checksValues;
    private readonly string _required;
    private readonly StringBuilder _methods = new();
    private readonly Dictionary<string, string> _emitted = new(StringComparer.Ordinal);
    private int _next;

    /// <summary>Creates an emitter.</summary>
    /// <param name="checksValues">
    ///     Whether every reader takes a <c>refusals</c> collector beside its reader: given one — a server reading what a
    ///     client sent — a value object is made through its <c>Create</c> with its rules asked, and each refusal is
    ///     recorded under the field it was read at; given null — a browser reading what the server answered — it is made
    ///     inside a loading scope, as a row is loaded. The CQRS codecs check; the API client, whose values are no
    ///     value objects, does not.
    /// </param>
    /// <param name="required">
    ///     The C# expression a checking reader words a missing required member with — the sentence a form's required
    ///     field says, from the app's catalog where the compilation has one.
    /// </param>
    public WireCodecEmitter(bool checksValues = false, string required = EnglishRequired)
    {
        _checksValues = checksValues;
        _required = required;
    }

    /// <summary>The required sentence as a literal, for a compilation with no catalog to read it from.</summary>
    public const string EnglishRequired = "\"This field is required.\"";

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
        _methods.AppendLine($"    private static {type.Fqn} R{id}(ref {Reader} reader, {FileListRead} files, string property{(_checksValues ? $", {Refusals} refusals" : "")})");
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
                read.AppendLine($"        return ({type.Fqn})global::Rask.Wire.WireJson.ReadInt64(ref reader, property);");
                break;

            case WireKind.Bytes:
                EmitBytes(write, read);
                break;

            case WireKind.File:
                EmitFile(write, read);
                break;

            case WireKind.Nullable:
            {
                var inner = Ensure(type.Inner!);
                write.AppendLine("        if (!value.HasValue) { writer.WriteNullValue(); return; }");
                write.AppendLine($"        W{inner}(writer, value.Value, files);");
                read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
                read.AppendLine($"        return {Read(inner, "property")};");
                break;
            }

            case WireKind.Sequence:
                EmitSequence(type, write, read);
                break;

            case WireKind.Dictionary:
                EmitDictionary(type, write, read);
                break;

            case WireKind.OneValue:
                EmitOneValue(type, write, read);
                break;

            case WireKind.Object:
                EmitObject(type, write, read);
                break;
        }
    }

    // The value it holds, bare: "ABC-123", not {"value":"ABC-123"}.
    private void EmitOneValue(WireType type, StringBuilder write, StringBuilder read)
    {
        if (type.IsReferenceType)
        {
            write.AppendLine("        if (value is null) { writer.WriteNullValue(); return; }");
            read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
        }

        if (type.Moment)
        {
            // The instant, with its Z: the wall clock names the hour it shows twice in autumn only once. Read, a Z (or an
            // offset) is that instant and a bare time — what a page loaded before the change still sends — a wall-clock
            // time of Clock.Zone, both of which the moment's constructor takes as they are.
            write.AppendLine("        writer.WriteStringValue(value.Instant.UtcDateTime);");
            read.AppendLine("        var held = global::Rask.Wire.WireJson.ReadMoment(ref reader, property);");
        }
        else
        {
            var inner = Ensure(type.Inner!);
            write.AppendLine($"        W{inner}(writer, value.{type.Members[0].ClrName}, files);");
            read.AppendLine($"        var held = {Read(inner, "property")};");
        }

        EmitBuild(type, type.ValueObject ? $"{type.Fqn}.Create(held)" : $"new {type.Fqn}(held)", read);
    }

    // A value object is made through its generated Create. Read by the server from what a client sent, its rules are
    // asked — no handler is ever given a value they refuse — and a refusal is recorded under the field it was read at,
    // so the read goes on and one 400 names every field. Read by a browser from what the server answered (no
    // collector), it is made inside a loading scope, as a row loaded from the database is: a rule tightened since the
    // value was stored must not make the answer unreadable. Anything else is returned as `made` makes it.
    private void EmitBuild(WireType type, string made, StringBuilder read)
    {
        if (!type.ValueObject)
        {
            read.AppendLine($"        return {made};");
            return;
        }

        if (!_checksValues)
        {
            read.AppendLine($"        using var __loading = {Rules}.Loading();");
            read.AppendLine($"        return {made};");
            return;
        }

        read.AppendLine("        if (refusals is null)");
        read.AppendLine("        {");
        read.AppendLine($"            using var __loading = {Rules}.Loading();");
        read.AppendLine($"            return {made};");
        read.AppendLine("        }");
        read.AppendLine();
        read.AppendLine("        try");
        read.AppendLine("        {");
        read.AppendLine($"            return {made};");
        read.AppendLine("        }");
        read.AppendLine("        catch (global::Rask.Cqrs.RaskValidationException __refusal)");
        read.AppendLine("        {");
        read.AppendLine("            refusals.Refuse(__refusal.Failures);");
        read.AppendLine("            return default;");
        read.AppendLine("        }");
    }

    // The call that reads one part, handing on the collector when there is one.
    private string Read(string id, string property) =>
        $"R{id}(ref reader, files, {property}{(_checksValues ? ", refusals" : "")})";

    // The read of one part, with the collector stepped into it, so a refusal inside is recorded under its field.
    private void Into(StringBuilder read, string indent, string step, string call)
    {
        if (!_checksValues)
        {
            read.AppendLine($"{indent}{call}");
            return;
        }

        read.AppendLine($"{indent}refusals?.{step};");
        read.AppendLine($"{indent}{call}");
        read.AppendLine($"{indent}refusals?.Leave();");
    }

    // Base64 in the JSON, unless the sender asked for binary: a transport that can carry the bytes beside the
    // JSON hands the codec its file list as a Collection<RemoteFile> rather than a List — the off-thread transport
    // (Rask.Cqrs, [OffThread]) does, so a workbook reaches a Web Worker as an ArrayBuffer instead of a string a
    // third larger that the page had to build. What stays in the JSON is the index, as for a file, and the reader
    // takes either form, so a sender that never asks keeps the wire it always had.
    //
    // WriteBase64StringValue takes a span, and a null array widens to an empty one — which would turn "absent"
    // into "zero bytes" silently. The explicit branch keeps null meaning null.
    private static void EmitBytes(StringBuilder write, StringBuilder read)
    {
        write.AppendLine("        if (value is null) { writer.WriteNullValue(); return; }");
        write.AppendLine(
            "        if (files is global::System.Collections.ObjectModel.Collection<global::Rask.Wire.RemoteFile>)");
        write.AppendLine("        {");
        write.AppendLine("            files.Add(global::Rask.Wire.RemoteFile.FromBytes(\"bytes\", null, value));");
        write.AppendLine("            writer.WriteNumberValue(files.Count - 1);");
        write.AppendLine("            return;");
        write.AppendLine("        }");
        write.AppendLine();
        write.AppendLine("        writer.WriteBase64StringValue(value);");

        read.AppendLine($"        if (reader.TokenType == {TokenType}.Number)");
        read.AppendLine("        {");
        read.AppendLine(
            "            var __carried = global::Rask.Wire.WireJson.ResolveFile("
            + "files, global::Rask.Wire.WireJson.ReadInt32(ref reader, property), property);");
        read.AppendLine("            if (__carried is null) return null;");
        read.AppendLine("            using var __stream = __carried.OpenReadStream();");
        read.AppendLine("            if (__carried.Size >= 0 && __carried.Size <= global::System.Array.MaxLength)");
        read.AppendLine("            {");
        read.AppendLine("                var __bytes = new byte[__carried.Size];");
        read.AppendLine("                __stream.ReadExactly(__bytes);");
        read.AppendLine("                return __bytes;");
        read.AppendLine("            }");
        read.AppendLine();
        read.AppendLine("            using var __copy = new global::System.IO.MemoryStream();");
        read.AppendLine("            __stream.CopyTo(__copy);");
        read.AppendLine("            return __copy.ToArray();");
        read.AppendLine("        }");
        read.AppendLine();
        read.AppendLine("        return global::Rask.Wire.WireJson.ReadBytes(ref reader, property);");
    }

    // The file itself leaves the JSON entirely; what stays behind is its index in the multipart
    // body, and -1 for "there wasn't one".
    // An IRaskFile in, an IRaskFile out - the message never mentions the wire type. The conversion
    // is emitted HERE, in the consumer's compilation, because this is the one place that sees
    // both Rask.Core and Rask.Cqrs; neither package has to reference the other for it.
    //
    // The file's own Size is passed as the read ceiling. IRaskFile.OpenReadStream defaults to
    // 512 KB to stop an unbounded read of a browser-supplied file, but the size is already
    // known here, so the ceiling can be the file itself rather than a guess that truncates
    // anything larger.
    private static void EmitFile(StringBuilder write, StringBuilder read)
    {
        write.AppendLine("        if (value is null) { writer.WriteNumberValue(-1); return; }");
        write.AppendLine(
            "        files.Add(global::Rask.Wire.RemoteFile.FromStream("
            + "value.Name, value.ContentType, value.Size, "
            + "__ct => value.OpenReadStream(value.Size, __ct), "
            + "value.LastModified));");
        write.AppendLine("        writer.WriteNumberValue(files.Count - 1);");
        read.AppendLine(
            "        var __wire = global::Rask.Wire.WireJson.ResolveFile("
            + "files, global::Rask.Wire.WireJson.ReadInt32(ref reader, property), property);");
        read.AppendLine("        return __wire is null ? null : new __RaskCqrsUploadedFile(__wire);");
    }

    private void EmitSequence(WireType type, StringBuilder write, StringBuilder read)
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
        read.AppendLine("        global::Rask.Wire.WireJson.ExpectStartArray(ref reader, property);");
        read.AppendLine($"        var items = new global::System.Collections.Generic.List<{type.Inner!.Fqn}>();");
        read.AppendLine($"        while (reader.Read() && reader.TokenType != {TokenType}.EndArray)");
        read.AppendLine("        {");
        Into(read, "            ", "EnterItem(items.Count)", $"items.Add({Read(element, "property")});");
        read.AppendLine("        }");
        read.AppendLine();
        read.AppendLine(type.Sequence == SequenceShape.Array
            ? "        return items.ToArray();"
            : "        return items;");
    }

    private void EmitDictionary(WireType type, StringBuilder write, StringBuilder read)
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
        read.AppendLine("        global::Rask.Wire.WireJson.ExpectStartObject(ref reader, property);");
        read.AppendLine(
            "        var map = new global::System.Collections.Generic.Dictionary<global::System.String, "
            + $"{type.Inner!.Fqn}>();");
        read.AppendLine($"        while (reader.Read() && reader.TokenType != {TokenType}.EndObject)");
        read.AppendLine("        {");
        read.AppendLine("            var key = reader.GetString();");
        read.AppendLine("            reader.Read();");
        Into(read, "            ", "EnterItem(key)", $"map[key] = {Read(value, "key")};");
        read.AppendLine("        }");
        read.AppendLine();
        read.AppendLine("        return map;");
    }

    private void EmitObject(WireType type, StringBuilder write, StringBuilder read)
    {
        var members = type.Members.ConvertAll(member => (Member: member, Id: Ensure(member.Type)));

        if (type.IsReferenceType)
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

        EmitObjectRead(type, members, read);
    }

    private void EmitObjectRead(WireType type, List<(WireMember Member, string Id)> members, StringBuilder read)
    {
        if (type.IsReferenceType)
        {
            read.AppendLine($"        if (reader.TokenType == {TokenType}.Null) return null;");
        }

        read.AppendLine("        global::Rask.Wire.WireJson.ExpectStartObject(ref reader, property);");
        if (_checksValues)
        {
            read.AppendLine("        var __refusedBefore = refusals?.Count ?? 0;");
        }

        foreach (var (member, _) in members)
        {
            read.AppendLine($"        {member.Type.Fqn} v_{member.ClrName} = default;");
            if (Checks(member))
            {
                read.AppendLine($"        var r_{member.ClrName} = false;");
            }
        }

        read.AppendLine($"        while (reader.Read() && reader.TokenType != {TokenType}.EndObject)");
        read.AppendLine("        {");
        read.AppendLine("            var name = reader.GetString();");
        read.AppendLine("            reader.Read();");
        read.AppendLine("            switch (name)");
        read.AppendLine("            {");
        foreach (var (member, id) in members)
        {
            EmitMemberCase(member, id, read);
        }

        // An unknown property is skipped rather than rejected: that is what lets a sender add a field
        // without breaking a receiver compiled before it existed.
        read.AppendLine("                default:");
        read.AppendLine("                    global::Rask.Wire.WireJson.SkipValue(ref reader);");
        read.AppendLine("                    break;");
        read.AppendLine("            }");
        read.AppendLine("        }");
        read.AppendLine();

        // A part refused leaves its whole unbuilt: the message is answered with the refusals, never handed on, and no
        // constructor meets the null a refused part was read as. A required member left out or sent null is one.
        if (_checksValues)
        {
            EmitRequired(members, read);
            read.AppendLine("        if (refusals is not null && refusals.Count > __refusedBefore) return default;");
        }

        EmitConstruction(type, members, read);
    }

    // Each member declared without `?` that is still null once the object is read — absent, or sent as null — is
    // refused as a form's empty required field is, under its own name. Only where a collector checks the read.
    private void EmitRequired(List<(WireMember Member, string Id)> members, StringBuilder read)
    {
        var required = members.Where(static m => m.Member.Required).ToList();
        if (required.Count == 0)
        {
            return;
        }

        read.AppendLine("        if (refusals is not null)");
        read.AppendLine("        {");
        foreach (var (member, _) in required)
        {
            read.AppendLine($"            if (v_{member.ClrName} is null && !r_{member.ClrName}) refusals.Require({Literal(member.ClrName)}, {_required});");
        }

        read.AppendLine("        }");
        read.AppendLine();
    }

    private void EmitMemberCase(WireMember member, string id, StringBuilder read)
    {
        read.AppendLine($"                case {Literal(member.WireName)}:");
        var call = $"v_{member.ClrName} = {Read(id, Literal(member.WireName))};";
        if (Checks(member))
        {
            // Refused inside is said there: a required member read as null because its own parts were refused is not
            // also "required".
            call = $"var __n_{member.ClrName} = refusals?.Count ?? 0; {call} "
                   + $"r_{member.ClrName} = refusals is not null && refusals.Count > __n_{member.ClrName};";
        }

        Into(read, "                    ", $"Enter({Literal(member.ClrName)})", call);
        read.AppendLine("                    break;");
    }

    // A required member a checking reader tracks: whether its own read was refused.
    private bool Checks(WireMember member) => _checksValues && member.Required;

    private void EmitConstruction(WireType type, List<(WireMember Member, string Id)> members, StringBuilder read)
    {
        if (type.ConstructorParameters is { Count: > 0 } parameters)
        {
            var arguments = parameters.Select(parameter =>
            {
                var match = type.Members.Find(m =>
                    string.Equals(m.ClrName, parameter, StringComparison.OrdinalIgnoreCase));
                return match is null ? "default" : $"v_{match.ClrName}";
            });

            var list = string.Join(", ", arguments);
            EmitBuild(type, type.ValueObject ? $"{type.Fqn}.Create({list})" : $"new {type.Fqn}({list})", read);
            return;
        }

        var made = new StringBuilder();
        made.AppendLine($"new {type.Fqn}");
        made.AppendLine("        {");
        foreach (var (member, _) in members)
        {
            made.AppendLine($"            {member.ClrName} = v_{member.ClrName},");
        }

        made.Append("        }");
        EmitBuild(type, made.ToString(), read);
    }

    // The shape's identity for reuse. Two properties of the same type share one pair; the kind is in the
    // key because a byte[] and a List<byte> would otherwise collide on nothing but their element type.
    private static string Key(WireType type) => type.Kind + "|" + type.Fqn;

    private static string Format(string template, string value) => template.Replace("{0}", value);

    // Roslyn's own escaping: a wire name can come from the user's [JsonPropertyName], and a hand escape that knew
    // only \ and " let a line break end the literal (CS1010).
    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    private const string Rules = "global::Rask.Values.GeneratedValueRules";
    private const string Refusals = "global::Rask.Wire.WireRefusals";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string Reader = "global::System.Text.Json.Utf8JsonReader";
    private const string TokenType = "global::System.Text.Json.JsonTokenType";
    private const string FileList = "global::System.Collections.Generic.IList<global::Rask.Wire.RemoteFile>";
    private const string FileListRead = "global::System.Collections.Generic.IReadOnlyList<global::Rask.Wire.RemoteFile>";
}

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Generators.External;

/// <summary>
///     Turns a <see cref="WireType" /> tree into the C# that writes it as the island's props JSON.
/// </summary>
/// <remarks>
///     <para>
///         A third backend over the shared wire model, and deliberately not the CQRS codec emitter:
///         island props are <b>write-only</b> and never carry a file. There is no reader to generate —
///         nothing ever sends props back — and no multipart index to reserve, so reusing the codec
///         would mean threading a file list the island has no concept of through every method.
///         Sharing the classifier is what keeps the two honest; sharing the emitter would only couple
///         islands to <c>Rask.Cqrs</c>.
///     </para>
///     <para>
///         Reflection-free by construction, which is what lets an island survive trimming and AOT: the
///         property names and the walk are decided at compile time, and nothing here inspects a type at
///         runtime.
///     </para>
/// </remarks>
/// <param name="stringEnums">
///     Whether an enum is written as its member's name rather than its number. True for a package island,
///     whose TypeScript declares string literals: a C# enum the author declares on one has to arrive as
///     <c>"contained"</c>, not <c>2</c>. Everywhere else the number stays, so a rename cannot change the wire.
/// </param>
internal sealed class PropsWriterEmitter(bool stringEnums = false)
{
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string StringEnumMemberName = "System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute";

    private readonly StringBuilder _methods = new();
    private readonly Dictionary<string, string> _emitted = new();
    private int _next;

    /// <summary>The generated writers, ready to drop into the island's partial.</summary>
    public string Methods => _methods.ToString();

    /// <summary>
    ///     Returns the id of the writer for <paramref name="type" />, emitting it the first time that
    ///     shape is asked for.
    /// </summary>
    /// <remarks>
    ///     The method is registered under its key <em>before</em> its body is emitted, so a type that
    ///     reaches itself through a collection resolves to the same method instead of recursing until
    ///     the generator runs out of stack.
    /// </remarks>
    public string Ensure(WireType type)
    {
        var key = Key(type);
        if (_emitted.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var id = _next++.ToString(CultureInfo.InvariantCulture);
        _emitted[key] = id;

        var body = new StringBuilder();
        WriteBody(body, type, "value");

        _methods.AppendLine(
            $"    private static void WP{id}({Writer} writer, {type.Fqn} value)");
        _methods.AppendLine("    {");
        _methods.Append(body);
        _methods.AppendLine("    }");
        _methods.AppendLine();

        return id;
    }

    /// <summary>Emits the statements that write <paramref name="access" /> at its shape.</summary>
    private void WriteBody(StringBuilder sb, WireType type, string access)
    {
        switch (type.Kind)
        {
            case WireKind.Scalar:
                sb.AppendLine($"        {Format(type.WriteExpression!, access)};");
                break;

            case WireKind.Enum when stringEnums && type.Symbol is { } symbol:
                WriteStringEnum(sb, type, symbol, access);
                break;

            case WireKind.Enum:
                // The underlying number, never the name: an enum member renamed in C# must not change
                // what the browser receives.
                sb.AppendLine($"        writer.WriteNumberValue((long){access});");
                break;

            case WireKind.Bytes:
                // Guarded rather than passed straight through: a null byte[] converts to an empty span,
                // so the browser would receive "" where the C# said null — a value the front end cannot
                // distinguish from genuinely empty data.
                sb.AppendLine($"        if ({access} is null) {{ writer.WriteNullValue(); }}");
                sb.AppendLine($"        else {{ writer.WriteBase64StringValue({access}); }}");
                break;

            case WireKind.Nullable:
            {
                var inner = Ensure(type.Inner!);
                sb.AppendLine($"        if ({access} is null) {{ writer.WriteNullValue(); }}");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                // .Value for a Nullable<T>; a nullable reference type needs no unwrap and the compiler
                // is happy either way once the null branch has run.
                var unwrapped = type.Inner!.Kind is WireKind.Object or WireKind.Sequence
                                              or WireKind.Dictionary or WireKind.Bytes
                    ? access
                    : $"{access}.Value";
                sb.AppendLine($"            WP{inner}(writer, {unwrapped}!);");
                sb.AppendLine("        }");
                break;
            }

            case WireKind.Sequence:
            {
                var inner = Ensure(type.Inner!);
                sb.AppendLine($"        if ({access} is null) {{ writer.WriteNullValue(); }}");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine("            writer.WriteStartArray();");
                sb.AppendLine($"            foreach (var item in {access})");
                sb.AppendLine("            {");
                sb.AppendLine($"                WP{inner}(writer, item!);");
                sb.AppendLine("            }");
                sb.AppendLine("            writer.WriteEndArray();");
                sb.AppendLine("        }");
                break;
            }

            case WireKind.Dictionary:
            {
                var inner = Ensure(type.Inner!);
                sb.AppendLine($"        if ({access} is null) {{ writer.WriteNullValue(); }}");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine("            writer.WriteStartObject();");
                sb.AppendLine($"            foreach (var pair in {access})");
                sb.AppendLine("            {");
                sb.AppendLine("                writer.WritePropertyName(pair.Key);");
                sb.AppendLine($"                WP{inner}(writer, pair.Value!);");
                sb.AppendLine("            }");
                sb.AppendLine("            writer.WriteEndObject();");
                sb.AppendLine("        }");
                break;
            }

            case WireKind.Object:
            {
                sb.AppendLine($"        if ({access} is null) {{ writer.WriteNullValue(); return; }}");
                sb.AppendLine("        writer.WriteStartObject();");
                foreach (var member in type.Members)
                {
                    var inner = Ensure(member.Type);
                    // Escaped, not quoted by hand: a [JsonPropertyName] can hold a quote or a backslash, and
                    // pasted into a literal either one ends the string and starts code.
                    sb.AppendLine($"        writer.WritePropertyName({Literal(member.WireName)});");
                    sb.AppendLine($"        WP{inner}(writer, {access}.{member.ClrName}!);");
                }

                sb.AppendLine("        writer.WriteEndObject();");
                break;
            }

            default:
                // Unreachable: the caller rejects an unsupported shape with a diagnostic before asking
                // for a writer. Emitting null rather than throwing keeps a generator bug from becoming
                // a compile error in code the author did not write.
                sb.AppendLine("        writer.WriteNullValue();");
                break;
        }
    }

    /// <summary>Writes an enum as the string its member stands for.</summary>
    /// <remarks>
    ///     <c>[JsonStringEnumMemberName]</c> wins where present, so a C# member can match a TypeScript literal
    ///     it cannot be spelled as (<c>x-large</c>); otherwise the camelCase member name, matching how every
    ///     prop name is written. A value that names no member — a cast from a number — falls back to that
    ///     number rather than to a string the package would not recognise.
    /// </remarks>
    private static void WriteStringEnum(StringBuilder sb, WireType type, INamedTypeSymbol symbol, string access)
    {
        sb.AppendLine($"        switch ({access})");
        sb.AppendLine("        {");

        // One case per distinct VALUE: two members aliasing one number would otherwise be a duplicate case
        // label, which does not compile.
        var seen = new HashSet<object>();
        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.HasConstantValue || field.ConstantValue is null || !seen.Add(field.ConstantValue))
            {
                continue;
            }

            var wire = field.GetAttributes()
                           .Where(a => a.AttributeClass?.ToDisplayString() == StringEnumMemberName)
                           .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
                           .FirstOrDefault(v => v is not null)
                       ?? CamelCase(field.Name);

            var member = SyntaxFacts.GetKeywordKind(field.Name) != SyntaxKind.None ? "@" + field.Name : field.Name;
            sb.AppendLine($"            case {type.Fqn}.{member}: writer.WriteStringValue({Literal(wire)}); break;");
        }

        sb.AppendLine($"            default: writer.WriteNumberValue((long){access}); break;");
        sb.AppendLine("        }");
    }

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string Format(string expression, string access) =>
        expression.Replace("{0}", access);

    /// <summary>
    ///     Keys a shape by the fully qualified name the writer is emitted against.
    /// </summary>
    /// <remarks>
    ///     Nullable and its underlying type are distinct keys on purpose — they generate different
    ///     bodies — so the FQN, which already differs, is enough.
    /// </remarks>
    private static string Key(WireType type) => type.Kind + "|" + type.Fqn;
}

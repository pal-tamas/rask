using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Cqrs.Generators;

/// <summary>
///     Turns a <see cref="WireType" /> tree into the TypeScript that describes it.
/// </summary>
/// <remarks>
///     <para>
///         The second backend over the same model <see cref="WireCodecEmitter" /> uses, and
///         deliberately so: the property names, the enum encoding and the null handling are decided
///         once, by <see cref="WireShape" />, and both emitters read them. A TypeScript type derived
///         any other way would drift from the codec, and a drift here is a runtime wire bug rather
///         than a compile error.
///     </para>
///     <para>
///         Named shapes are registered <em>before</em> their body is emitted, so a type that refers
///         to itself through a collection resolves to the same name instead of recursing for ever —
///         the same guard the codec emitter needs for the same reason.
///     </para>
/// </remarks>
internal sealed class TypeScriptEmitter
{
    /// <summary>
    ///     The wire forms that are not instants, and the TypeScript alias each gets.
    /// </summary>
    /// <remarks>
    ///     These stay strings on purpose. A calendar date is not a point in time: JavaScript parses
    ///     <c>"2026-08-25"</c> as UTC midnight, so anyone west of UTC renders it as the 24th — the
    ///     off-by-one-day this repo already documents in the Gantt sample. A time of day and a
    ///     duration are not instants either, and a <c>Date</c> round-tripped back would be rejected
    ///     outright by the reader on the other side.
    /// </remarks>
    private static readonly Dictionary<string, string> StringAliases = new()
    {
        ["global::System.Guid"] = "Guid",
        ["global::System.DateOnly"] = "DateOnly",
        ["global::System.TimeOnly"] = "TimeOnly",
        ["global::System.TimeSpan"] = "Duration",
    };

    /// <summary>The numeric CLR scalars. Everything here is a JSON number both ways.</summary>
    private static readonly HashSet<string> Numbers = new()
    {
        "global::System.Byte", "global::System.SByte",
        "global::System.Int16", "global::System.UInt16",
        "global::System.Int32", "global::System.UInt32",
        "global::System.Int64", "global::System.UInt64",
        "global::System.Single", "global::System.Double", "global::System.Decimal",
    };

    private readonly StringBuilder _declarations = new();
    private readonly Dictionary<string, string> _named = new();
    private readonly Dictionary<string, ShapeDescriptor> _shapes = new();

    /// <summary>The emitted interfaces and enums, in the order they were first needed.</summary>
    public string Declarations => _declarations.ToString();

    /// <summary>
    ///     The date-bearing paths of every named shape, for the client to revive against.
    /// </summary>
    /// <remarks>
    ///     This is what lets the runtime turn exactly the right strings into <c>Date</c> objects. The
    ///     usual approach — a <c>JSON.parse</c> reviver that regex-tests every string — converts a
    ///     product code or an ETag that merely looks like a timestamp, silently. Nothing has to be
    ///     guessed here: the C# type said so.
    /// </remarks>
    public IReadOnlyDictionary<string, ShapeDescriptor> Shapes => _shapes;

    /// <summary>
    ///     The TypeScript type expression for <paramref name="type" />, emitting any named
    ///     declarations it needs on the way.
    /// </summary>
    public string Ensure(WireType type)
    {
        switch (type.Kind)
        {
            case WireKind.Nullable:
                return Ensure(type.Inner!) + " | null";

            case WireKind.Sequence:
                return Wrap(Ensure(type.Inner!)) + "[]";

            case WireKind.Dictionary:
                return "Record<string, " + Ensure(type.Inner!) + ">";

            case WireKind.Bytes:
                // base64, per WireJson. A string on the wire and a string here; turning it into a
                // Uint8Array would hide a decode the caller may not want on every payload.
                return "Base64";

            case WireKind.File:
                return "File | Blob";

            case WireKind.Enum:
                return EnsureEnum(type);

            case WireKind.Object:
                return EnsureObject(type);

            case WireKind.Scalar:
                return Scalar(type);

            default:
                // Unreachable: RASK053 has already failed the build for an unsupported shape, so a
                // contract carrying one never reaches an emitter. Throwing beats emitting `any`,
                // which would silently hand the front end a type that means nothing.
                throw new System.InvalidOperationException(
                    $"'{type.Fqn}' has no wire encoding and should have been reported as RASK053.");
        }
    }

    /// <summary>Parenthesises a union so <c>(A | null)[]</c> does not read as <c>A | null[]</c>.</summary>
    private static string Wrap(string expression) =>
        expression.Contains(" | ") ? "(" + expression + ")" : expression;

    private static string Scalar(WireType type)
    {
        if (type.Fqn == "global::System.Boolean")
        {
            return "boolean";
        }

        if (Numbers.Contains(type.Fqn))
        {
            // Int64 and Decimal included, and that is a real caveat rather than an oversight: the
            // codec writes them as JSON numbers, so JSON.parse yields a double either way. `bigint`
            // would be a lie about what arrives.
            return "number";
        }

        if (type.Fqn is "global::System.String" or "global::System.Char" or "global::System.Uri")
        {
            return "string";
        }

        if (StringAliases.TryGetValue(type.Fqn, out var alias))
        {
            return alias;
        }

        // The instants. Both are ISO-8601 on the wire and both are points in time, which is exactly
        // what a JS Date is.
        return type.Fqn is "global::System.DateTime" or "global::System.DateTimeOffset"
            ? "Date"
            : "unknown";
    }

    /// <summary>True when a shape is an instant, and so needs reviving into a <c>Date</c>.</summary>
    private static bool IsInstant(WireType type) =>
        type.Kind == WireKind.Scalar
        && type.Fqn is "global::System.DateTime" or "global::System.DateTimeOffset";

    private string EnsureEnum(WireType type)
    {
        if (_named.TryGetValue(type.Fqn, out var existing))
        {
            return existing;
        }

        // Without the symbol there are no member names to emit, so the honest fallback is the wire
        // form itself: a number. Better a correct number than an invented enum.
        if (type.Symbol is not { } symbol)
        {
            return "number";
        }

        var name = Unique(symbol.Name);
        _named[type.Fqn] = name;

        var members = symbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue)
            .Select(f => "  " + f.Name + " = "
                         + System.Convert.ToInt64(f.ConstantValue, CultureInfo.InvariantCulture)
                             .ToString(CultureInfo.InvariantCulture) + ",");

        _declarations.AppendLine("/** Numeric on the wire: a rename is safe, a renumber is not. */");
        _declarations.AppendLine("export enum " + name + " {");
        foreach (var member in members)
        {
            _declarations.AppendLine(member);
        }

        _declarations.AppendLine("}");
        _declarations.AppendLine();
        return name;
    }

    private string EnsureObject(WireType type)
    {
        if (_named.TryGetValue(type.Fqn, out var existing))
        {
            return existing;
        }

        var name = Unique(type.Symbol?.Name ?? "Anonymous");

        // Registered before the body is walked: a type that reaches itself through a list resolves
        // to this name rather than recursing until the stack gives out.
        _named[type.Fqn] = name;

        var body = new StringBuilder();
        var instants = new List<string>();
        var nested = new Dictionary<string, NestedShape>();

        foreach (var member in type.Members)
        {
            var memberType = member.Type;
            var emitted = Ensure(memberType);

            // A nullable value type is already a union via WireKind.Nullable; this adds the
            // reference case, which the shape alone cannot express.
            if (member.Nullable && !emitted.EndsWith(" | null", System.StringComparison.Ordinal))
            {
                emitted += " | null";
            }

            body.AppendLine("  " + member.WireName + ": " + emitted + ";");
            Describe(member.WireName, memberType, instants, nested);
        }

        _declarations.AppendLine("export interface " + name + " {");
        _declarations.Append(body);
        _declarations.AppendLine("}");
        _declarations.AppendLine();

        _shapes[name] = new ShapeDescriptor(instants, nested);
        return name;
    }

    /// <summary>
    ///     Records how one property must be revived: directly, or by walking into a named shape.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Unwraps nullables, sequences and dictionaries, because a date inside a list is still a
    ///         date.
    ///     </para>
    ///     <para>
    ///         An instant needs no container count: the runtime walks down until it finds strings, and
    ///         a string is unmistakable. A nested shape does, because <c>Dictionary&lt;string, T&gt;</c>
    ///         and <c>T</c> both arrive as plain objects and nothing in the JSON tells them apart —
    ///         guessing there would either revive the dictionary's own keys as if they were the
    ///         shape's properties, or skip the values entirely.
    ///     </para>
    /// </remarks>
    private void Describe(
        string wireName,
        WireType type,
        List<string> instants,
        Dictionary<string, NestedShape> nested)
    {
        var inner = type;
        var depth = 0;
        while (inner.Kind is WireKind.Nullable or WireKind.Sequence or WireKind.Dictionary)
        {
            // A nullable is not a container: `Order?` still arrives as one object, not a list of them.
            if (inner.Kind != WireKind.Nullable)
            {
                depth++;
            }

            inner = inner.Inner!;
        }

        if (IsInstant(inner))
        {
            instants.Add(wireName);
            return;
        }

        if (inner.Kind == WireKind.Object)
        {
            nested[wireName] = new NestedShape(Ensure(inner), depth);
        }
    }

    private string Unique(string preferred)
    {
        if (!_named.ContainsValue(preferred))
        {
            return preferred;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = preferred + suffix.ToString(CultureInfo.InvariantCulture);
            if (!_named.ContainsValue(candidate))
            {
                return candidate;
            }
        }
    }
}

/// <summary>A named shape reached through some number of arrays or dictionaries.</summary>
/// <param name="Name">The shape's TypeScript name.</param>
/// <param name="Depth">
///     How many containers stand between the property and the shape: 0 for a plain object, 1 for a
///     list or a dictionary of them, 2 for a list of lists.
/// </param>
internal sealed record NestedShape(string Name, int Depth);

/// <summary>Which properties of one shape carry dates, and which lead to another shape.</summary>
internal sealed class ShapeDescriptor(IReadOnlyList<string> instants, IReadOnlyDictionary<string, NestedShape> nested)
{
    /// <summary>Properties that are instants, and so become <c>Date</c>.</summary>
    public IReadOnlyList<string> Instants { get; } = instants;

    /// <summary>Properties whose value is another named shape, by that shape's TypeScript name.</summary>
    public IReadOnlyDictionary<string, NestedShape> Nested { get; } = nested;

    /// <summary>Whether this shape, as written, needs the runtime to touch it at all.</summary>
    public bool IsEmpty => Instants.Count == 0 && Nested.Count == 0;
}

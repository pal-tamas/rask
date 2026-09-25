namespace Rask.Generators.Shared;

/// <summary>One property of an <see cref="WireKind.Object" />, as it appears on the wire.</summary>
internal sealed class WireMember(string clrName, string wireName, WireType type, bool nullable = false)
{
    /// <summary>
    ///     Whether this property may be null on the wire, for a <em>reference</em> type.
    /// </summary>
    /// <remarks>
    ///     A nullable value type is already its own shape (<see cref="WireKind.Nullable" />); this
    ///     covers the reference case, which the classifier otherwise cannot see. The codec does not
    ///     need it — it writes JSON null for a null reference either way — but a consumer that
    ///     generates types from this model does: saying <c>string</c> where <c>string | null</c> can
    ///     arrive is a promise the wire does not keep.
    ///     <para>
    ///         Only an explicit <c>?</c> counts. In a project with nullable contexts switched off
    ///         every reference is technically nullable, and saying so would put <c>| null</c> on every
    ///         string in the file — noise that reads as a broken generator rather than as a warning.
    ///         Rask projects enable nullable contexts, and so do the scaffolded templates, so the
    ///         annotation is a reliable statement of what the author meant.
    ///     </para>
    /// </remarks>
    public bool Nullable { get; } = nullable;

    /// <summary>The C# property name, used to read the value off an instance.</summary>
    public string ClrName { get; } = clrName;

    /// <summary>The JSON property name — camelCase, or whatever <c>[JsonPropertyName]</c> pinned.</summary>
    public string WireName { get; } = wireName;

    /// <summary>The property's shape.</summary>
    public WireType Type { get; } = type;
}

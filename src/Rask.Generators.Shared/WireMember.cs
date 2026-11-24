namespace Rask.Generators.Shared;

/// <summary>One property of an <see cref="WireKind.Object" />, as it appears on the wire.</summary>
internal sealed class WireMember(string clrName, string wireName, WireType type, bool nullable = false, bool required = false)
    : System.IEquatable<WireMember>
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

    /// <summary>
    ///     Whether the property is a reference declared without <c>?</c> — <c>DriverName Driver</c>, <c>string Note</c> —
    ///     so a checking reader refuses a message that leaves it out or sends it null, as a form refuses an empty
    ///     required field. Only an explicit annotation counts, as for <see cref="Nullable" />: a project with nullable
    ///     contexts switched off declares nothing, and nothing is required of it.
    /// </summary>
    public bool Required { get; } = required;

    /// <summary>The C# property name, used to read the value off an instance.</summary>
    public string ClrName { get; } = clrName;

    /// <summary>The JSON property name — camelCase, or whatever <c>[JsonPropertyName]</c> pinned.</summary>
    public string WireName { get; } = wireName;

    /// <summary>The property's shape.</summary>
    public WireType Type { get; } = type;

    /// <inheritdoc />
    public bool Equals(WireMember? other) =>
        other is not null
        && Nullable == other.Nullable
        && Required == other.Required
        && string.Equals(ClrName, other.ClrName, System.StringComparison.Ordinal)
        && string.Equals(WireName, other.WireName, System.StringComparison.Ordinal)
        && Type.Equals(other.Type);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as WireMember);

    /// <inheritdoc />
    public override int GetHashCode() => System.StringComparer.Ordinal.GetHashCode(WireName);
}

namespace Rask.Core.Live;

/// <summary>
///     The key an UNKEYED component written inside a keyed row is claimed under (#1309): the row's key and the
///     child's place among the row's children of its type.
/// </summary>
/// <remarks>
///     A key of its own type, so it can never equal a key an app writes. Equal by value, so a row written again
///     finds what it held even where the object itself was not kept; <see cref="KeyedSiblings" /> keeps it, so
///     a render boxes nothing.
/// </remarks>
internal sealed class ScopedKey(object scope, int ordinal) : IEquatable<ScopedKey>
{
    public object Scope { get; } = scope;

    public int Ordinal { get; } = ordinal;

    public bool Equals(ScopedKey? other) =>
        other is not null && (ReferenceEquals(this, other) || (Ordinal == other.Ordinal && Scope.Equals(other.Scope)));

    public override bool Equals(object? obj) => Equals(obj as ScopedKey);

    public override int GetHashCode() => HashCode.Combine(Scope, Ordinal);

    public override string ToString() => $"{Scope}#{Ordinal}";
}

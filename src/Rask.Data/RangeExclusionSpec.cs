namespace Rask.Data;

/// <summary>
/// A declared "no two rows may overlap" rule over a half-open <c>[Lo, Hi)</c> range, as carried on the entity
/// type by <see cref="RangeExclusionBuilderExtensions.HasNonOverlappingRange{TEntity}"/>.
/// </summary>
/// <remarks>
/// The spec names <em>properties</em>, not columns: the provider resolves them against the mapped table, so a
/// <c>HasColumnName</c> rename is honoured. It round-trips through <see cref="Serialize"/> /
/// <see cref="TryParse"/> because EF Core scaffolds annotation values into migrations and model snapshots as
/// string literals — which is also why the format carries a version tag.
/// </remarks>
/// <param name="Lo">Property holding the inclusive lower bound.</param>
/// <param name="Hi">Property holding the exclusive upper bound.</param>
/// <param name="PartitionBy">Properties that scope the rule (e.g. RoomId). Empty means table-wide.</param>
/// <param name="IgnoreSoftDeleted">Whether soft-deleted rows are excluded, so a deleted row frees its slot.</param>
public sealed record RangeExclusionSpec(
    string Lo,
    string Hi,
    IReadOnlyList<string> PartitionBy,
    bool IgnoreSoftDeleted = false)
{
    /// <summary>The annotation the spec is stored under on the entity type.</summary>
    public const string AnnotationName = "Rask:RangeExclusion";

    // Fields hold C# property names, which can never contain either separator.
    private const char FieldSeparator = '|';
    private const char ListSeparator = ',';
    private const string Version = "v1";
    private const int FieldCount = 5;

    /// <summary>Renders the spec as the annotation's string value.</summary>
    /// <returns>A value <see cref="TryParse"/> can read back.</returns>
    public string Serialize() => string.Join(
        FieldSeparator,
        Version,
        Lo,
        Hi,
        string.Join(ListSeparator, PartitionBy),
        IgnoreSoftDeleted ? "1" : "0");

    /// <summary>Compares two specs by content.</summary>
    /// <param name="other">The spec to compare with.</param>
    /// <returns><see langword="true"/> when both declare the same rule.</returns>
    /// <remarks>
    /// The compiler-generated equality would compare <see cref="PartitionBy"/> by reference, so two specs
    /// parsed from the same annotation would report unequal.
    /// </remarks>
    public bool Equals(RangeExclusionSpec? other)
        => other is not null
            && Lo == other.Lo
            && Hi == other.Hi
            && IgnoreSoftDeleted == other.IgnoreSoftDeleted
            && PartitionBy.SequenceEqual(other.PartitionBy, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Lo, StringComparer.Ordinal);
        hash.Add(Hi, StringComparer.Ordinal);
        hash.Add(IgnoreSoftDeleted);

        foreach (var property in PartitionBy)
        {
            hash.Add(property, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Reads back a value produced by <see cref="Serialize"/>.</summary>
    /// <param name="value">The annotation value, or <c>null</c> when the entity declares no rule.</param>
    /// <param name="spec">The parsed spec when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a well-formed spec.</returns>
    public static bool TryParse(object? value, out RangeExclusionSpec spec)
    {
        spec = null!;

        if (value is not string text)
        {
            return false;
        }

        var parts = text.Split(FieldSeparator);
        if (parts.Length != FieldCount || parts[0] != Version)
        {
            return false;
        }

        spec = new RangeExclusionSpec(
            parts[1],
            parts[2],
            parts[3].Length == 0 ? [] : parts[3].Split(ListSeparator),
            parts[4] == "1");
        return true;
    }
}

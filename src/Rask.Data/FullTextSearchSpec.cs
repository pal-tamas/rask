namespace Rask.Data;

/// <summary>
/// The full-text index an entity declares through
/// <see cref="FullTextSearchBuilderExtensions.HasFullTextSearch{TEntity}"/>, as carried on the entity type.
/// </summary>
/// <remarks>
/// The spec names <em>properties</em>, not columns: the provider resolves them against the mapped table, so a
/// <c>HasColumnName</c> rename is honoured. It round-trips through <see cref="Serialize"/> /
/// <see cref="TryParse"/> because EF Core scaffolds annotation values into migrations and model snapshots as
/// string literals — which is also why the format carries a version tag.
/// </remarks>
/// <param name="Properties">The indexed properties, in the order they were declared.</param>
/// <param name="Tokenizer">How the indexed text is split into searchable terms.</param>
internal sealed record FullTextSearchSpec(IReadOnlyList<string> Properties, FullTextTokenizer Tokenizer)
{
    /// <summary>The annotation the spec is stored under — on the entity type, and on its table for migrations.</summary>
    public const string AnnotationName = "Rask:FullTextSearch";

    // Fields hold C# property names and enum names, which can never contain either separator.
    private const char FieldSeparator = '|';
    private const char ListSeparator = ',';
    private const string Version = "v1";
    private const int FieldCount = 3;

    /// <summary>Renders the spec as the annotation's string value.</summary>
    public string Serialize() => string.Join(
        FieldSeparator,
        Version,
        string.Join(ListSeparator, Properties),
        Tokenizer.ToString());

    /// <summary>Compares two specs by content rather than by list reference.</summary>
    public bool Equals(FullTextSearchSpec? other)
        => other is not null
            && Tokenizer == other.Tokenizer
            && Properties.SequenceEqual(other.Properties, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tokenizer);

        foreach (var property in Properties)
        {
            hash.Add(property, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Reads back a value produced by <see cref="Serialize"/>.</summary>
    /// <param name="value">The annotation value, or <c>null</c> when the entity declares no index.</param>
    /// <param name="spec">The parsed spec when this returns <see langword="true"/>.</param>
    public static bool TryParse(object? value, out FullTextSearchSpec spec)
    {
        spec = null!;

        if (value is not string text)
        {
            return false;
        }

        var parts = text.Split(FieldSeparator);
        if (parts.Length != FieldCount
            || parts[0] != Version
            || parts[1].Length == 0
            || !Enum.TryParse<FullTextTokenizer>(parts[2], ignoreCase: false, out var tokenizer)
            || !Enum.IsDefined(tokenizer))
        {
            return false;
        }

        spec = new FullTextSearchSpec(parts[1].Split(ListSeparator), tokenizer);
        return true;
    }
}

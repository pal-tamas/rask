namespace Rask.Data;

/// <summary>
/// The JSON paths an entity asks to have indexed, as <see cref="JsonIndexBuilderExtensions.HasJsonIndex{TEntity}"/>
/// stores them: each path the chain of C# members from the entity to the value, <c>Meta.Address.City</c>.
/// </summary>
/// <remarks>
/// Member names, not JSON names or SQL: the provider resolves those from the model when it writes the DDL, so a
/// renamed JSON property (<c>HasJsonPropertyName</c>) or a renamed container column changes the index with it.
/// </remarks>
internal sealed record JsonIndexSpec(IReadOnlyList<IReadOnlyList<string>> Paths)
{
    public const string AnnotationName = "Rask:JsonIndex";

    // Member names are C# identifiers, which can never contain any of the separators.
    private const string Version = "v1";
    private const char FieldSeparator = '|';
    private const char PathSeparator = ';';
    private const char MemberSeparator = '.';

    public string Serialize() =>
        Version + FieldSeparator + string.Join(PathSeparator, Paths.Select(p => string.Join(MemberSeparator, p)));

    /// <summary>This spec with <paramref name="path"/> added, unless it is already there.</summary>
    public JsonIndexSpec With(IReadOnlyList<string> path) =>
        Paths.Any(p => p.SequenceEqual(path, StringComparer.Ordinal)) ? this : new JsonIndexSpec([.. Paths, path]);

    public static bool TryParse(object? value, out JsonIndexSpec spec)
    {
        spec = null!;
        if (value is not string text)
        {
            return false;
        }

        var parts = text.Split(FieldSeparator);
        if (parts.Length != 2 || parts[0] != Version)
        {
            return false;
        }

        spec = new JsonIndexSpec(parts[1].Length == 0
            ? []
            : parts[1].Split(PathSeparator).Select(p => (IReadOnlyList<string>)p.Split(MemberSeparator)).ToArray());
        return true;
    }
}

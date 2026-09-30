namespace Rask.Generators.Shared;

/// <summary>Names as the generated JavaScript and TypeScript side spells them.</summary>
internal static class Identifiers
{
    /// <summary>The name with its first letter lower-cased: <c>DisplayName</c> → <c>displayName</c>.</summary>
    public static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);
}

namespace Rask.Cli.Scaffolding;

/// <summary>One field of a generated entity: its property <see cref="Name"/>, C# <see cref="CsType"/>,
/// and the property <see cref="Initializer"/> (only non-null where the type needs one, e.g. string).</summary>
internal sealed record FieldSpec(string Name, string CsType, string? Initializer);

/// <summary>
/// Parses the <c>--fields "Name:string,Price:decimal,InStock:bool"</c> spec into <see cref="FieldSpec"/>s.
/// Only types the Rask form binder and EF/SQLite both handle are accepted, so the generated entity and
/// its bound inputs compile.
/// </summary>
internal static class FieldSpecParser
{
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["string"] = "string",
        ["text"] = "string",
        ["int"] = "int",
        ["integer"] = "int",
        ["number"] = "int",
        ["long"] = "long",
        ["decimal"] = "decimal",
        ["money"] = "decimal",
        ["double"] = "double",
        ["float"] = "double",
        ["bool"] = "bool",
        ["boolean"] = "bool",
        ["datetime"] = "DateTime",
        ["date"] = "DateTime",
        ["guid"] = "Guid",
    };

    public static IReadOnlyCollection<string> SupportedTypes { get; } =
        TypeAliases.Values.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    public static bool TryParse(string spec, out IReadOnlyList<FieldSpec> fields, out string? error)
    {
        var parsed = new List<FieldSpec>();
        fields = parsed;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                error = $"Field '{raw}' must be in 'name:type' form (e.g. Price:decimal).";
                return false;
            }

            var (name, type) = (parts[0], parts[1]);
            if (!Identifiers.IsValidTypeName(name))
            {
                error = $"'{name}' is not a valid property name.";
                return false;
            }

            if (name.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                error = "'Id' is added automatically — omit it from --fields.";
                return false;
            }

            if (!seen.Add(name))
            {
                error = $"Duplicate field '{name}' — each field name must be unique.";
                return false;
            }

            if (!TypeAliases.TryGetValue(type, out var csType))
            {
                error = $"Unknown field type '{type}'. Supported: {string.Join(", ", SupportedTypes)}.";
                return false;
            }

            parsed.Add(new FieldSpec(name, csType, csType == "string" ? "= \"\"" : null));
        }

        if (parsed.Count == 0)
        {
            error = "At least one field is required, e.g. --fields \"Name:string,Price:decimal\".";
            return false;
        }

        error = null;
        return true;
    }
}

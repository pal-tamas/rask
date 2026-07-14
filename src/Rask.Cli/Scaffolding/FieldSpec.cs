namespace Rask.Cli.Scaffolding;

/// <summary>
/// One field of a generated entity. <see cref="CsType"/> is the base C# type (e.g. <c>string</c>);
/// <see cref="IsNullable"/> makes the property optional (<c>string?</c>); <see cref="MaxLength"/> is the
/// string length constraint (defaulted for strings, null for other types).
/// </summary>
internal sealed record FieldSpec(string Name, string CsType, bool IsNullable, int? MaxLength)
{
    public bool IsString => CsType == "string";

    /// <summary>The declared property type, e.g. <c>string</c> or <c>int?</c>.</summary>
    public string PropertyType => IsNullable ? CsType + "?" : CsType;

    /// <summary>Only a required (non-nullable) string needs an initializer (<c>= "";</c>).</summary>
    public string? Initializer => IsString && !IsNullable ? "= \"\"" : null;
}

/// <summary>
/// Parses the <c>--fields "Name:string,Price:decimal,Note:string?(500)"</c> spec into
/// <see cref="FieldSpec"/>s. A type may carry a trailing <c>?</c> (optional/nullable) and, for strings,
/// a <c>(length)</c>. Only types the Rask form binder and EF/SQLite both handle are accepted, so the
/// generated entity and its bound inputs compile.
/// </summary>
internal static class FieldSpecParser
{
    /// <summary>The max length applied to a string field when the spec doesn't give one.</summary>
    public const int DefaultStringMaxLength = 200;

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

            var name = parts[0];
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

            if (!TryParseType(parts[1], out var csType, out var nullable, out var maxLength, out error))
            {
                return false;
            }

            parsed.Add(new FieldSpec(name, csType, nullable, maxLength));
        }

        if (parsed.Count == 0)
        {
            error = "At least one field is required, e.g. --fields \"Name:string,Price:decimal\".";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseType(string token, out string csType, out bool nullable, out int? maxLength, out string? error)
    {
        csType = "";
        nullable = false;
        maxLength = null;
        error = null;

        // Optional trailing "(length)".
        var open = token.IndexOf('(');
        if (open >= 0)
        {
            var close = token.IndexOf(')');
            if (close < open || close != token.Length - 1)
            {
                error = $"Malformed length in '{token}' — use e.g. string(100).";
                return false;
            }

            if (!int.TryParse(token[(open + 1)..close], out var length) || length <= 0)
            {
                error = $"Invalid length in '{token}' — it must be a positive integer.";
                return false;
            }

            maxLength = length;
            token = token[..open];
        }

        // Optional trailing "?" (nullable / optional).
        token = token.Trim();
        if (token.EndsWith('?'))
        {
            nullable = true;
            token = token[..^1].Trim();
        }

        if (!TypeAliases.TryGetValue(token, out var mapped))
        {
            error = $"Unknown field type '{token}'. Supported: {string.Join(", ", SupportedTypes)}.";
            return false;
        }

        csType = mapped;

        if (maxLength is not null && csType != "string")
        {
            error = "A (length) only applies to string fields.";
            return false;
        }

        if (csType == "string" && maxLength is null)
        {
            maxLength = DefaultStringMaxLength;
        }

        return true;
    }
}

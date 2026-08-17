using System.Globalization;
using System.Text;

namespace Rask.Cli.Scaffolding;

/// <summary>Helpers for turning user input into valid C# identifiers, namespaces, and route paths.</summary>
internal static class Identifiers
{
    // The C# reserved keywords — none may be used bare as a type name (they'd need an '@' prefix,
    // which the scaffolder deliberately doesn't emit). Contextual keywords (var, nameof, …) are legal
    // identifiers and are intentionally absent.
    private static readonly HashSet<string> ReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>
    /// True if <paramref name="value"/> is usable as a namespace: every dot-separated segment is a valid,
    /// non-keyword C# identifier. A generated project's name becomes its root namespace (and csproj name), so
    /// this gates <c>rask new</c> — <c>Shop</c> and <c>Contoso.Shop</c> pass; <c>my-app</c>, <c>9Lives</c>,
    /// and a trailing dot don't (they'd emit <c>namespace my-app;</c> and never compile).
    /// </summary>
    public static bool IsValidNamespaceName(string value) =>
        !string.IsNullOrEmpty(value) && value.Split('.').All(IsValidTypeName);

    /// <summary>True if <paramref name="value"/> is a valid, non-keyword C# identifier (a usable type name).</summary>
    public static bool IsValidTypeName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return !ReservedKeywords.Contains(value);
    }

    /// <summary>A camelCase parameter name for a PascalCase field, <c>@</c>-escaped if it lands on a keyword.</summary>
    public static string ToCamelCase(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        var camel = char.ToLowerInvariant(name[0]) + name[1..];
        return ReservedKeywords.Contains(camel) ? "@" + camel : camel;
    }

    /// <summary>
    /// True if <paramref name="route"/> is safe to embed in a <c>Route</c> override — no quote,
    /// backslash, or control character that would break the string literal.
    /// </summary>
    public static bool IsValidRoutePath(string route)
    {
        foreach (var c in route)
        {
            if (c is '"' or '\\' || char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Turn a path segment (a directory name) into a valid namespace part: keep letters/digits, drop
    /// everything else, and prefix an underscore if it starts with a digit. Returns null for a segment
    /// that yields nothing usable (e.g. ".").
    /// </summary>
    public static string? ToNamespacePart(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
        }

        if (builder.Length == 0)
        {
            return null;
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    /// <summary>A default kebab-case route path for a feature name, e.g. "ProductList" → "/product-list".</summary>
    public static string ToRoutePath(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        builder.Append('/');
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && !char.IsUpper(name[i - 1]))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

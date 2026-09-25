using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Rask.Generators.Json;

/// <summary>One value of a parsed JSON document, with the position it started at.</summary>
internal sealed class JsonNode(JsonKind kind, int line, int column)
{
    private List<KeyValuePair<string, JsonNode>>? _members;
    private List<JsonNode>? _items;

    public JsonKind Kind { get; } = kind;

    /// <summary>The 1-based line the value starts on.</summary>
    public int Line { get; } = line;

    /// <summary>The 1-based column the value starts at.</summary>
    public int Column { get; } = column;

    /// <summary>A string's unescaped text, or a number's raw text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>An object's members, in document order.</summary>
    public List<KeyValuePair<string, JsonNode>> Members => _members ??= new List<KeyValuePair<string, JsonNode>>();

    /// <summary>An array's items, in document order.</summary>
    public List<JsonNode> Items => _items ??= new List<JsonNode>();

    /// <summary>The first member named <paramref name="name" />, or null.</summary>
    public JsonNode? this[string name]
    {
        get
        {
            if (Kind != JsonKind.Object || _members is null)
            {
                return null;
            }

            return _members.FirstOrDefault(member => string.Equals(member.Key, name, System.StringComparison.Ordinal)).Value;
        }
    }

    /// <summary>The text of a string value, or null for any other kind.</summary>
    public string? AsString() => Kind == JsonKind.String ? Text : null;

    /// <summary>The value of a boolean, or null for any other kind.</summary>
    public bool? AsBoolean() => Kind switch
    {
        JsonKind.True => true,
        JsonKind.False => false,
        _ => null,
    };

    /// <summary>The value of a number, when it is one.</summary>
    public bool TryGetNumber(out double value)
    {
        value = 0;
        return Kind == JsonKind.Number
               && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

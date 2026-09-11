using System.Collections.Generic;
using System.Globalization;

namespace Rask.Generators.Json;

/// <summary>The kind of a <see cref="JsonNode" />.</summary>
internal enum JsonKind
{
    Object,
    Array,
    String,
    Number,
    True,
    False,
    Null,
}

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

            foreach (var member in _members)
            {
                if (string.Equals(member.Key, name, System.StringComparison.Ordinal))
                {
                    return member.Value;
                }
            }

            return null;
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

/// <summary>The outcome of <see cref="JsonLite.Parse" />: a root, or the first defect and where it is.</summary>
internal readonly struct JsonParseResult(JsonNode? root, string? defect, int line, int column)
{
    public JsonNode? Root { get; } = root;

    public string? Defect { get; } = defect;

    public int Line { get; } = line;

    public int Column { get; } = column;
}

/// <summary>
///     A small, general JSON reader for analyzer-time input that is not a translation catalog.
/// </summary>
/// <remarks>
///     Never throws. A generator that throws takes the whole compilation down with a stack trace that
///     names nothing the author wrote, so malformed input is a defect with a line and column instead —
///     including input nested deep enough to exhaust the stack, which is refused at a fixed depth.
/// </remarks>
internal static class JsonLite
{
    private const int MaxDepth = 64;

    public static JsonParseResult Parse(string text)
    {
        var parser = new Parser(new JsonScanner(text));
        var root = parser.ReadValue(0);
        if (parser.Defect is null)
        {
            parser.Scanner.SkipWhitespace();
            if (!parser.Scanner.AtEnd)
            {
                parser.Fail("trailing content after the document");
            }
        }

        return parser.Defect is null
            ? new JsonParseResult(root, null, 0, 0)
            : new JsonParseResult(null, parser.Defect, parser.DefectLine, parser.DefectColumn);
    }

    private sealed class Parser(JsonScanner scanner)
    {
        public JsonScanner Scanner { get; } = scanner;

        public string? Defect { get; private set; }

        public int DefectLine { get; private set; }

        public int DefectColumn { get; private set; }

        public void Fail(string reason)
        {
            if (Defect is not null)
            {
                return;
            }

            Defect = reason;
            DefectLine = Scanner.Line;
            DefectColumn = Scanner.Column;
        }

        public JsonNode? ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                Fail($"nested more than {MaxDepth} levels deep");
                return null;
            }

            Scanner.SkipWhitespace();
            var line = Scanner.Line;
            var column = Scanner.Column;

            switch (Scanner.Peek())
            {
                case '{':
                    return ReadObject(depth, line, column);
                case '[':
                    return ReadArray(depth, line, column);
                case '"':
                    if (!Scanner.TryReadString(out var text, out var defect))
                    {
                        Fail(defect!);
                        return null;
                    }

                    return new JsonNode(JsonKind.String, line, column) { Text = text };
                case 't':
                    return ReadWord("true", JsonKind.True, line, column);
                case 'f':
                    return ReadWord("false", JsonKind.False, line, column);
                case 'n':
                    return ReadWord("null", JsonKind.Null, line, column);
                case '\0' when Scanner.AtEnd:
                    Fail("unexpected end of the document");
                    return null;
                default:
                    return ReadNumber(line, column);
            }
        }

        private JsonNode? ReadObject(int depth, int line, int column)
        {
            var node = new JsonNode(JsonKind.Object, line, column);
            Scanner.Advance(); // {
            Scanner.SkipWhitespace();
            if (Scanner.TryConsume('}'))
            {
                return node;
            }

            while (true)
            {
                Scanner.SkipWhitespace();
                if (Scanner.Peek() != '"')
                {
                    Fail("expected a quoted member name");
                    return null;
                }

                if (!Scanner.TryReadString(out var name, out var defect))
                {
                    Fail(defect!);
                    return null;
                }

                Scanner.SkipWhitespace();
                if (!Scanner.TryConsume(':'))
                {
                    Fail($"expected ':' after the member name '{name}'");
                    return null;
                }

                var value = ReadValue(depth + 1);
                if (value is null)
                {
                    return null;
                }

                node.Members.Add(new KeyValuePair<string, JsonNode>(name, value));

                Scanner.SkipWhitespace();
                if (Scanner.TryConsume(','))
                {
                    continue;
                }

                if (Scanner.TryConsume('}'))
                {
                    return node;
                }

                Fail($"expected ',' or '}}' after the value of '{name}'");
                return null;
            }
        }

        private JsonNode? ReadArray(int depth, int line, int column)
        {
            var node = new JsonNode(JsonKind.Array, line, column);
            Scanner.Advance(); // [
            Scanner.SkipWhitespace();
            if (Scanner.TryConsume(']'))
            {
                return node;
            }

            while (true)
            {
                var item = ReadValue(depth + 1);
                if (item is null)
                {
                    return null;
                }

                node.Items.Add(item);

                Scanner.SkipWhitespace();
                if (Scanner.TryConsume(','))
                {
                    continue;
                }

                if (Scanner.TryConsume(']'))
                {
                    return node;
                }

                Fail("expected ',' or ']' in an array");
                return null;
            }
        }

        private JsonNode? ReadWord(string word, JsonKind kind, int line, int column)
        {
            var text = Scanner.Text;
            if (string.CompareOrdinal(text, Scanner.Position, word, 0, word.Length) != 0)
            {
                Fail("expected a value");
                return null;
            }

            for (var i = 0; i < word.Length; i++)
            {
                Scanner.Advance();
            }

            return new JsonNode(kind, line, column);
        }

        private JsonNode? ReadNumber(int line, int column)
        {
            var text = Scanner.Text;
            var start = Scanner.Position;

            if (Scanner.Peek() == '-')
            {
                Scanner.Advance();
            }

            if (!ConsumeDigits())
            {
                Fail("expected a value");
                return null;
            }

            if (Scanner.Peek() == '.')
            {
                Scanner.Advance();
                if (!ConsumeDigits())
                {
                    Fail("expected digits after the decimal point");
                    return null;
                }
            }

            if (Scanner.Peek() is 'e' or 'E')
            {
                Scanner.Advance();
                if (Scanner.Peek() is '+' or '-')
                {
                    Scanner.Advance();
                }

                if (!ConsumeDigits())
                {
                    Fail("expected digits in the exponent");
                    return null;
                }
            }

            return new JsonNode(JsonKind.Number, line, column)
            {
                Text = text.Substring(start, Scanner.Position - start),
            };
        }

        private bool ConsumeDigits()
        {
            var any = false;
            while (Scanner.Peek() is >= '0' and <= '9')
            {
                Scanner.Advance();
                any = true;
            }

            return any;
        }
    }
}

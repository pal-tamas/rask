using System.Text;

namespace Rask.Generators.Json;

/// <summary>
///     The lexical half of reading JSON by hand: position, whitespace, comments and quoted strings.
/// </summary>
/// <remarks>
///     <para>
///         Hand-written rather than <c>System.Text.Json</c> because this assembly is a Roslyn analyzer —
///         netstandard2.0, loaded into csc and into every IDE, and carrying exactly one package reference.
///         Shipping a serializer alongside it is how an analyzer starts failing to load against whatever
///         version the IDE already has.
///     </para>
///     <para>
///         Shared by the translation catalog reader and the island props snapshot reader, which used to be
///         one copy of this and would otherwise have become two: the escape table, the line counting and
///         the comment rule are exactly the things that drift when duplicated.
///     </para>
/// </remarks>
internal sealed class JsonScanner(string text)
{
    private int _lineStart;

    /// <summary>The text being read.</summary>
    public string Text { get; } = text;

    /// <summary>The index of the next character to read.</summary>
    public int Position { get; private set; }

    /// <summary>The 1-based line of <see cref="Position" />.</summary>
    public int Line { get; private set; } = 1;

    /// <summary>The 1-based column of <see cref="Position" />.</summary>
    public int Column => Position - _lineStart + 1;

    /// <summary>Whether every character has been consumed.</summary>
    public bool AtEnd => Position >= Text.Length;

    /// <summary>The next character, or <c>'\0'</c> at the end.</summary>
    public char Peek() => Position < Text.Length ? Text[Position] : '\0';

    /// <summary>Consumes one character that is known not to be a line break.</summary>
    public void Advance() => Position++;

    /// <summary>Consumes <paramref name="expected" /> when it is next.</summary>
    public bool TryConsume(char expected)
    {
        if (Peek() != expected)
        {
            return false;
        }

        Position++;
        return true;
    }

    /// <summary>Skips whitespace, counting lines, and <c>//</c> line comments.</summary>
    /// <remarks>
    ///     Line comments are not JSON, but every hand-maintained JSON file eventually grows a note.
    ///     Accepting them costs four lines and avoids a defect that teaches nothing.
    /// </remarks>
    public void SkipWhitespace()
    {
        while (Position < Text.Length)
        {
            var c = Text[Position];
            if (c == '\n')
            {
                Line++;
                Position++;
                _lineStart = Position;
                continue;
            }

            if (c is ' ' or '\t' or '\r')
            {
                Position++;
                continue;
            }

            if (c == '/' && Position + 1 < Text.Length && Text[Position + 1] == '/')
            {
                while (Position < Text.Length && Text[Position] != '\n')
                {
                    Position++;
                }

                continue;
            }

            return;
        }
    }

    /// <summary>
    ///     Reads a quoted string starting at the opening quote, or reports why it cannot.
    /// </summary>
    /// <param name="value">The unescaped text.</param>
    /// <param name="defect">Why the string is malformed, with the scanner left at the offending character.</param>
    public bool TryReadString(out string value, out string? defect)
    {
        value = string.Empty;
        defect = null;
        Position++; // opening quote
        var sb = new StringBuilder();

        while (Position < Text.Length)
        {
            var c = Text[Position];
            if (c == '"')
            {
                Position++;
                value = sb.ToString();
                return true;
            }

            if (c == '\\')
            {
                Position++;
                if (Position >= Text.Length)
                {
                    break;
                }

                var esc = Text[Position];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (Position + 4 < Text.Length && TryParseHex(Text.Substring(Position + 1, 4), out var code))
                        {
                            sb.Append((char)code);
                            Position += 4;
                        }
                        else
                        {
                            defect = "malformed \\u escape";
                            return false;
                        }

                        break;
                    default:
                        defect = $"unknown escape '\\{esc}'";
                        return false;
                }

                Position++;
                continue;
            }

            if (c == '\n')
            {
                defect = "a newline inside a quoted value — use \\n";
                return false;
            }

            sb.Append(c);
            Position++;
        }

        defect = "unterminated text value";
        return false;
    }

    private static bool TryParseHex(string s, out int value)
    {
        value = 0;
        foreach (var c in s)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                return false;
            }

            value = (value * 16) + digit;
        }

        return true;
    }
}

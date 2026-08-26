using System.Collections.Generic;
using System.Text;

namespace Rask.Generators.Translations;

/// <summary>
///     Reads a translation catalog: a JSON object whose values are strings or further objects.
/// </summary>
/// <remarks>
///     Hand-written rather than <c>System.Text.Json</c> because this assembly is a Roslyn analyzer —
///     netstandard2.0, loaded into csc and into every IDE, and carrying exactly one package reference.
///     Shipping a serializer alongside it is how an analyzer starts failing to load against whatever
///     version the IDE already has. The grammar needed here is tiny, and doing it by hand also keeps
///     precise line/column offsets, so a defect points at the key rather than at the file.
/// </remarks>
internal static class JsonCatalogReader
{
    public static void Read(string text, Catalog catalog)
    {
        var reader = new Reader(text, catalog);
        reader.ReadDocument();
    }

    private sealed class Reader(string text, Catalog catalog)
    {
        private int _pos;
        private int _line = 1;
        private int _lineStart;

        private int Column => _pos - _lineStart + 1;

        public void ReadDocument()
        {
            SkipWhitespace();
            if (!TryExpect('{', "a catalog must be a JSON object mapping keys to text"))
            {
                return;
            }

            ReadObject(prefix: null);

            // Only look for trailing content when the document was otherwise clean. A reader that has
            // already bailed out mid-document is sitting somewhere arbitrary, so this check would fire
            // every time and bury the real cause under a second, confusing error.
            if (catalog.Defects.Count > 0)
            {
                return;
            }

            SkipWhitespace();
            if (_pos < text.Length)
            {
                Defect("trailing content after the closing brace");
            }
        }

        private void ReadObject(string? prefix)
        {
            // An object's immediate string members, held until the closing brace so a "$plural" marker
            // anywhere inside it can retroactively turn the whole object into ONE key. Without that,
            // "one"/"other" would already have been flattened into separate keys by the time the marker
            // was seen.
            var members = new List<(string Key, string Value, int Line, int Column)>();
            var line = _line;
            var column = Column;

            SkipWhitespace();
            if (Peek() == '}')
            {
                _pos++;
                return;
            }

            while (true)
            {
                SkipWhitespace();
                var keyLine = _line;
                var keyColumn = Column;

                if (Peek() != '"')
                {
                    Defect("expected a quoted key");
                    return;
                }

                if (!TryReadString(out var key))
                {
                    return;
                }

                SkipWhitespace();
                if (!TryExpect(':', $"expected ':' after the key '{key}'"))
                {
                    return;
                }

                var path = prefix is null ? key : prefix + "." + key;

                SkipWhitespace();
                var c = Peek();
                if (c == '"')
                {
                    if (!TryReadString(out var value))
                    {
                        return;
                    }

                    members.Add((key, value, keyLine, keyColumn));
                }
                else if (c == '{')
                {
                    _pos++;
                    ReadObject(path);
                }
                else
                {
                    // A number, bool or null in a catalog is almost always a mistake rather than an
                    // intent — say which key, because the file may have hundreds.
                    Defect($"the value for '{path}' is not text or a nested object");
                    return;
                }

                SkipWhitespace();
                if (Peek() == ',')
                {
                    _pos++;
                    continue;
                }

                if (Peek() == '}')
                {
                    _pos++;
                    Flush(prefix, members, line, column);
                    return;
                }

                Defect($"expected ',' or '}}' after the value for '{path}'");
                return;
            }
        }

        // Turns an object's collected members into catalog entries: one plural key, or one key each.
        private void Flush(
            string? prefix,
            List<(string Key, string Value, int Line, int Column)> members,
            int line,
            int column)
        {
            string? pluralParameter = null;
            foreach (var member in members)
            {
                if (member.Key == "$plural")
                {
                    pluralParameter = member.Value;
                    break;
                }
            }

            if (pluralParameter is null)
            {
                foreach (var member in members)
                {
                    var path = prefix is null ? member.Key : prefix + "." + member.Key;
                    catalog.Add(new CatalogEntry(path, member.Value, member.Line, member.Column));
                }

                return;
            }

            if (prefix is null)
            {
                catalog.Defects.Add(new CatalogDefect(
                    "a '$plural' marker at the top level — it belongs inside the key it pluralises", line, column));
                return;
            }

            var entry = new CatalogEntry(prefix, string.Empty, line, column)
            {
                PluralParameter = pluralParameter,
            };

            foreach (var member in members)
            {
                if (member.Key == "$plural")
                {
                    continue;
                }

                entry.Forms[member.Key] = member.Value;
            }

            catalog.Add(entry);
        }

        private bool TryReadString(out string value)
        {
            value = string.Empty;
            _pos++; // opening quote
            var sb = new StringBuilder();

            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '"')
                {
                    _pos++;
                    value = sb.ToString();
                    return true;
                }

                if (c == '\\')
                {
                    _pos++;
                    if (_pos >= text.Length)
                    {
                        break;
                    }

                    var esc = text[_pos];
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
                            if (_pos + 4 < text.Length
                                && TryParseHex(text.Substring(_pos + 1, 4), out var code))
                            {
                                sb.Append((char)code);
                                _pos += 4;
                            }
                            else
                            {
                                Defect("malformed \\u escape");
                                return false;
                            }

                            break;
                        default:
                            Defect($"unknown escape '\\{esc}'");
                            return false;
                    }

                    _pos++;
                    continue;
                }

                if (c == '\n')
                {
                    Defect("a newline inside a quoted value — use \\n");
                    return false;
                }

                sb.Append(c);
                _pos++;
            }

            Defect("unterminated text value");
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

        private char Peek() => _pos < text.Length ? text[_pos] : '\0';

        private bool TryExpect(char expected, string reason)
        {
            if (Peek() == expected)
            {
                _pos++;
                return true;
            }

            Defect(reason);
            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '\n')
                {
                    _line++;
                    _pos++;
                    _lineStart = _pos;
                    continue;
                }

                if (c is ' ' or '\t' or '\r')
                {
                    _pos++;
                    continue;
                }

                // Line comments are not JSON, but every catalog file eventually grows a note about
                // where a string appears. Accepting them costs four lines and avoids a defect that
                // teaches nothing.
                if (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n')
                    {
                        _pos++;
                    }

                    continue;
                }

                return;
            }
        }

        private void Defect(string reason) =>
            catalog.Defects.Add(new CatalogDefect(reason, _line, Column));
    }
}

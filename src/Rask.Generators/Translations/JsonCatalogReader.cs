using System.Collections.Generic;
using Rask.Generators.Json;

namespace Rask.Generators.Translations;

/// <summary>
///     Reads a translation catalog: a JSON object whose values are strings or further objects.
/// </summary>
/// <remarks>
///     Hand-written rather than <c>System.Text.Json</c> because this assembly is a Roslyn analyzer —
///     netstandard2.0, loaded into csc and into every IDE, and carrying exactly one package reference.
///     Shipping a serializer alongside it is how an analyzer starts failing to load against whatever
///     version the IDE already has. The grammar needed here is tiny, and doing it by hand also keeps
///     precise line/column offsets, so a defect points at the key rather than at the file. The lexing —
///     position, whitespace, comments, escapes — is <see cref="JsonScanner" />, shared with the island
///     props snapshot reader.
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
        private readonly JsonScanner _scanner = new(text);

        public void ReadDocument()
        {
            _scanner.SkipWhitespace();
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

            _scanner.SkipWhitespace();
            if (!_scanner.AtEnd)
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
            var line = _scanner.Line;
            var column = _scanner.Column;

            _scanner.SkipWhitespace();
            if (_scanner.TryConsume('}'))
            {
                return;
            }

            while (true)
            {
                _scanner.SkipWhitespace();
                var keyLine = _scanner.Line;
                var keyColumn = _scanner.Column;

                if (_scanner.Peek() != '"')
                {
                    Defect("expected a quoted key");
                    return;
                }

                if (!TryReadString(out var key))
                {
                    return;
                }

                _scanner.SkipWhitespace();
                if (!TryExpect(':', $"expected ':' after the key '{key}'"))
                {
                    return;
                }

                var path = prefix is null ? key : prefix + "." + key;

                _scanner.SkipWhitespace();
                var c = _scanner.Peek();
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
                    _scanner.Advance();
                    ReadObject(path);
                }
                else
                {
                    // A number, bool or null in a catalog is almost always a mistake rather than an
                    // intent — say which key, because the file may have hundreds.
                    Defect($"the value for '{path}' is not text or a nested object");
                    return;
                }

                _scanner.SkipWhitespace();
                if (_scanner.TryConsume(','))
                {
                    continue;
                }

                if (_scanner.TryConsume('}'))
                {
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
            if (_scanner.TryReadString(out value, out var defect))
            {
                return true;
            }

            Defect(defect!);
            return false;
        }

        private bool TryExpect(char expected, string reason)
        {
            if (_scanner.TryConsume(expected))
            {
                return true;
            }

            Defect(reason);
            return false;
        }

        private void Defect(string reason) =>
            catalog.Defects.Add(new CatalogDefect(reason, _scanner.Line, _scanner.Column));
    }
}

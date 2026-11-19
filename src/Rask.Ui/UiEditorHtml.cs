using System.Net;
using System.Text;

namespace Rask;

/// <summary>
///     An editor's value cut to what its own schema writes (<see cref="UiEditorSchema" />): the HTML a page paints
///     before the engine loads, and the HTML a change hands the app.
/// </summary>
/// <remarks>
///     The output is rebuilt, never copied: text with no <c>&lt;</c> in it, and the schema's tags with their
///     attributes decoded, checked and encoded again. So it is safe however a browser would have read the input —
///     a comment, a script, an event handler or a <c>javascript:</c> link does not come out of it.
/// </remarks>
internal sealed class UiEditorHtml
{
    private readonly string _html;
    private readonly StringBuilder _kept;
    private readonly List<string> _open = [];
    private int _at;

    private UiEditorHtml(string html)
    {
        _html = html;
        _kept = new StringBuilder(html.Length);
    }

    /// <summary>The schema's part of <paramref name="html" />.</summary>
    internal static string Clean(string html) => html.Length == 0 ? html : new UiEditorHtml(html).Read();

    private string Read()
    {
        while (_at < _html.Length)
        {
            var tag = _html.IndexOf('<', _at);
            var text = (tag < 0 ? _html.Length : tag) - _at;
            _kept.Append(_html, _at, text);
            _at += text;
            if (tag >= 0)
            {
                ReadMarkup();
            }
        }

        for (var i = _open.Count - 1; i >= 0; i--)
        {
            _kept.Append("</").Append(_open[i]).Append('>');
        }

        return _kept.ToString();
    }

    private void ReadMarkup()
    {
        var next = At(1);
        if (next is '!' or '?')
        {
            SkipDeclaration();
        }
        else if (next == '/')
        {
            ReadEndTag();
        }
        else if (char.IsAsciiLetter(next))
        {
            ReadStartTag();
        }
        else
        {
            _kept.Append("&lt;");
            _at++;
        }
    }

    // A comment, a doctype, a processing instruction: none of them is the reader's text.
    private void SkipDeclaration()
    {
        if (string.CompareOrdinal(_html, _at, "<!--", 0, 4) == 0)
        {
            var end = _html.IndexOf("-->", _at + 4, StringComparison.Ordinal);
            _at = end < 0 ? _html.Length : end + 3;
            return;
        }

        SkipPast('>');
    }

    private void ReadStartTag()
    {
        _at++;
        var name = ReadName();
        var attributes = ReadAttributes();
        if (UiEditorSchema.DropsContent(name))
        {
            SkipContent(name);
            return;
        }

        if (!UiEditorSchema.Keeps(name))
        {
            return;
        }

        _kept.Append('<').Append(name);
        foreach (var (attribute, raw) in attributes)
        {
            if (UiEditorSchema.Attribute(name, attribute, WebUtility.HtmlDecode(raw)) is { } value)
            {
                _kept.Append(' ').Append(attribute).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
            }
        }

        _kept.Append('>');
        if (!UiEditorSchema.IsVoid(name))
        {
            _open.Add(name);
        }
    }

    private void ReadEndTag()
    {
        _at += 2;
        if (!char.IsAsciiLetter(At(0)))
        {
            SkipPast('>');
            return;
        }

        var name = ReadName();
        SkipPast('>');
        var open = _open.LastIndexOf(name);
        if (open < 0)
        {
            return;
        }

        for (var i = _open.Count - 1; i >= open; i--)
        {
            _kept.Append("</").Append(_open[i]).Append('>');
        }

        _open.RemoveRange(open, _open.Count - open);
    }

    private List<(string Name, string Value)> ReadAttributes()
    {
        var attributes = new List<(string, string)>();
        while (true)
        {
            while (_at < _html.Length && (char.IsWhiteSpace(_html[_at]) || _html[_at] == '/'))
            {
                _at++;
            }

            if (_at >= _html.Length || _html[_at] == '>')
            {
                _at = Math.Min(_at + 1, _html.Length);
                return attributes;
            }

            var start = _at++;
            while (_at < _html.Length && !char.IsWhiteSpace(_html[_at]) && _html[_at] is not ('/' or '>' or '='))
            {
                _at++;
            }

            // A browser keeps the first of two attributes of one name, and so does this.
            var name = _html[start.._at].ToLowerInvariant();
            var value = ReadValue();
            if (!attributes.Exists(attribute => string.Equals(attribute.Item1, name, StringComparison.Ordinal)))
            {
                attributes.Add((name, value));
            }
        }
    }

    private string ReadValue()
    {
        SkipSpace();
        if (At(0) != '=')
        {
            return string.Empty;
        }

        _at++;
        SkipSpace();
        var quote = At(0);
        if (quote is '"' or '\'')
        {
            var end = _html.IndexOf(quote, _at + 1);
            var value = _html[(_at + 1)..(end < 0 ? _html.Length : end)];
            _at = end < 0 ? _html.Length : end + 1;
            return value;
        }

        var start = _at;
        while (_at < _html.Length && !char.IsWhiteSpace(_html[_at]) && _html[_at] != '>')
        {
            _at++;
        }

        return _html[start.._at];
    }

    private string ReadName()
    {
        var start = _at;
        while (_at < _html.Length && !char.IsWhiteSpace(_html[_at]) && _html[_at] is not ('/' or '>'))
        {
            _at++;
        }

        return _html[start.._at].ToLowerInvariant();
    }

    // A script's source or a style sheet goes with its tag, up to the end tag that closes it.
    private void SkipContent(string name)
    {
        var end = _html.IndexOf("</" + name, _at, StringComparison.OrdinalIgnoreCase);
        _at = end < 0 ? _html.Length : end;
        if (end >= 0)
        {
            ReadEndTag();
        }
    }

    private void SkipSpace()
    {
        while (_at < _html.Length && char.IsWhiteSpace(_html[_at]))
        {
            _at++;
        }
    }

    private void SkipPast(char end)
    {
        var at = _html.IndexOf(end, _at);
        _at = at < 0 ? _html.Length : at + 1;
    }

    private char At(int offset) => _at + offset < _html.Length ? _html[_at + offset] : '\0';
}

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Rask.Core.Forms;

// Reads a .NET regular expression token by token and writes what the browser's `v`-flag dialect says the same with, or
// refuses it. An ALLOW-list: what is not named here is refused, since a construct the two read apart would have the
// browser refuse a text the field takes. Where the two differ in what a shorthand covers, it is written out — .NET's \d
// is every decimal digit (\p{Nd}), its `.` everything but \n — and where the browser's reading cannot be made the same
// (\w, \b, \S, a lookbehind, a named group, an inline option, a backreference, a class subtraction), nothing is written.
//
// "Anything" atoms (`.`, a negated class, \D, \P{…}) match one UTF-16 unit in .NET and one character in the browser, so a
// run that counts them could take half of an emoji in .NET; they are told only outside a group, under * or +, and never
// two in a row that each need a character.
internal sealed class HtmlPatternScanner
{
    // Unicode general categories, the names both dialects spell the same. C, Cs and Cn are not: they hold the surrogates.
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "L", "Lu", "Ll", "Lt", "Lm", "Lo", "M", "Mn", "Mc", "Me", "N", "Nd", "Nl", "No",
        "P", "Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po", "S", "Sm", "Sc", "Sk", "So", "Z", "Zs", "Zl", "Zp", "Cc", "Cf", "Co",
    };

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    private readonly string _p;
    private readonly StringBuilder _html = new();
    private readonly Stack<bool> _lookaheads = new();
    private int _i;
    private int _anyLeast;
    private int _anchors;
    private bool _alternates;
    private bool _looksAround;
    private bool _opens;
    private bool _closes;

    private HtmlPatternScanner(string pattern) => _p = pattern;

    private enum Repeat
    {
        Once,
        Star,
        Plus,
        Optional,
        Counted,
    }

    internal static ScannedPattern? Scan(string pattern) => new HtmlPatternScanner(pattern).Run();

    private ScannedPattern? Run()
    {
        while (_i < _p.Length)
        {
            if (!Step())
            {
                return null;
            }
        }

        return _lookaheads.Count == 0
            ? new ScannedPattern(_html.ToString(), _alternates, _opens, _closes, _anchors, _looksAround)
            : null;
    }

    private bool Step()
    {
        var c = _p[_i];
        switch (c)
        {
            case '\\':
                return Escape();
            case '[':
                return Class();
            case '(':
                return Open();
            case ')':
                return Close();
            case '|':
                return Alternate();
            case '^' or '$':
                return Anchor(c);
            case '.':
                _i++;
                return Atom(@"[^\n]", any: true);
            case '*' or '+' or '?' or '{' or '}' or ']':
                return false;
            default:
                if (char.IsSurrogate(c))
                {
                    return false;
                }

                _i++;
                return Atom(c.ToString(), any: false);
        }
    }

    private bool Alternate()
    {
        _i++;
        _html.Append('|');
        if (_lookaheads.Count == 0)
        {
            _alternates = true;
            _anyLeast = 0;
        }

        return true;
    }

    private bool Anchor(char c)
    {
        _opens |= c == '^' && _i == 0;
        _closes |= c == '$' && _i == _p.Length - 1;
        _anchors++;
        _i++;
        _html.Append(c);
        return !StartsRepeat();
    }

    private bool Open()
    {
        _i++;
        var lookahead = false;
        if (Peek('?'))
        {
            var kind = _i + 1 < _p.Length ? _p[_i + 1] : '\0';
            if (kind is not (':' or '=' or '!'))
            {
                return false;
            }

            lookahead = kind is not ':';
            _looksAround |= lookahead;
            _html.Append("(?").Append(kind);
            _i += 2;
        }
        else
        {
            _html.Append('(');
        }

        _lookaheads.Push(lookahead);
        return true;
    }

    private bool Close()
    {
        if (_lookaheads.Count == 0)
        {
            return false;
        }

        _i++;
        _html.Append(')');
        return _lookaheads.Pop() ? !StartsRepeat() : Repeated(out _, out _);
    }

    // One atom and its quantifier. An "anything" atom is told only at the top, repeated by * or +, with at most one
    // character asked of the atoms of that kind since the last atom that takes one character of its own.
    private bool Atom(string written, bool any)
    {
        _html.Append(written);
        if (!Repeated(out var repeat, out var least))
        {
            return false;
        }

        if (any)
        {
            if (_lookaheads.Count > 0 || repeat is not (Repeat.Star or Repeat.Plus) || _anyLeast + least > 1)
            {
                return false;
            }

            _anyLeast += least;
        }
        else if (least > 0 && _lookaheads.Count == 0)
        {
            _anyLeast = 0;
        }

        return true;
    }

    // The quantifier after an atom, written as it stands: both dialects spell * + ? {n} {n,} {n,m} and a lazy ? alike. A
    // `{` that is no quantifier is a literal to .NET and an error to the browser, so it refuses.
    private bool Repeated(out Repeat repeat, out int least)
    {
        (repeat, least) = (Repeat.Once, 1);
        if (_i >= _p.Length)
        {
            return true;
        }

        var start = _i;
        switch (_p[_i])
        {
            case '*':
                (repeat, least) = (Repeat.Star, 0);
                _i++;
                break;
            case '+':
                repeat = Repeat.Plus;
                _i++;
                break;
            case '?':
                (repeat, least) = (Repeat.Optional, 0);
                _i++;
                break;
            case '{':
                if (!Counted(out least))
                {
                    return false;
                }

                repeat = Repeat.Counted;
                break;
            default:
                return true;
        }

        if (Peek('?'))
        {
            _i++;
        }

        _html.Append(_p, start, _i - start);
        return !StartsRepeat();
    }

    private bool Counted(out int least)
    {
        least = 0;
        _i++;
        if (!Digits(out least))
        {
            return false;
        }

        if (Peek(','))
        {
            _i++;
            _ = Digits(out _);
        }

        if (!Peek('}'))
        {
            return false;
        }

        _i++;
        return true;
    }

    private bool Digits(out int value)
    {
        var start = _i;
        while (_i < _p.Length && char.IsAsciiDigit(_p[_i]))
        {
            _i++;
        }

        value = _i > start && _i - start < 9 ? int.Parse(_p.AsSpan(start, _i - start), CultureInfo.InvariantCulture) : 0;
        return _i > start;
    }

    private bool StartsRepeat() => _i < _p.Length && _p[_i] is '*' or '+' or '?' or '{';

    private bool Peek(char c) => _i < _p.Length && _p[_i] == c;

    // An escape outside a class.
    private bool Escape()
    {
        if (_i + 1 >= _p.Length)
        {
            return false;
        }

        var e = _p[_i + 1];
        _i += 2;
        return e switch
        {
            'd' => Atom(@"\p{Nd}", any: false),
            'D' => Atom(@"\P{Nd}", any: true),
            's' => Atom(@"[\s\x85]", any: false),
            't' or 'n' or 'r' or 'f' or 'v' => Atom("\\" + e, any: false),
            'x' or 'u' => Hex(e) is { } code && !char.IsSurrogate(code) && Atom(_p[(_i - (e == 'x' ? 4 : 6)).._i], any: false),
            'p' or 'P' => Category() is { } name && Atom("\\" + e + "{" + name + "}", any: e == 'P'),
            _ when IsSyntax(e) => Atom("\\" + e, any: false),
            _ when IsAsciiPunctuation(e) || e == ' ' => Atom(e.ToString(), any: false),
            _ => false,
        };
    }

    // \xHH or \uHHHH, the cursor past the letter: the character, the cursor past its digits.
    private char? Hex(char letter)
    {
        var length = letter == 'x' ? 2 : 4;
        if (_i + length > _p.Length || _p.AsSpan(_i, length).ContainsAnyExcept(HexDigits))
        {
            return null;
        }

        var code = (char)int.Parse(_p.AsSpan(_i, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        _i += length;
        return code;
    }

    // \p{Name}, the cursor past the letter: a general category both dialects name alike, the cursor past the brace.
    private string? Category()
    {
        if (!Peek('{'))
        {
            return null;
        }

        var close = _p.IndexOf('}', _i);
        if (close < 0)
        {
            return null;
        }

        var name = _p[(_i + 1)..close];
        _i = close + 1;
        return Categories.Contains(name) ? name : null;
    }

    private bool Class()
    {
        const string AnyCharacter = @"[\s\S]";
        if (string.CompareOrdinal(_p, _i, AnyCharacter, 0, AnyCharacter.Length) == 0)
        {
            _i += AnyCharacter.Length;
            return Atom(AnyCharacter, any: true);
        }

        _i++;
        var negated = Peek('^');
        if (negated)
        {
            _i++;
        }

        // .NET reads a ] first in a class as itself; the browser as the class's end.
        if (Peek(']'))
        {
            return false;
        }

        var written = new StringBuilder(negated ? "[^" : "[");
        var any = negated;
        return ClassBody(written, negated, ref any) && Atom(written.Append(']').ToString(), any);
    }

    private bool ClassBody(StringBuilder written, bool negated, ref bool any)
    {
        var members = false;
        while (_i < _p.Length)
        {
            var c = _p[_i];
            if (c == ']')
            {
                _i++;
                return true;
            }

            if (c == '[')
            {
                return false;
            }

            if (c == '-')
            {
                // A - is itself only first or last; anywhere else after an escape or a range it is .NET's to read.
                if (members && !(_i + 1 < _p.Length && _p[_i + 1] == ']'))
                {
                    return false;
                }

                written.Append(@"\-");
                _i++;
                members = true;
                continue;
            }

            if (!Member(written, negated, ref any))
            {
                return false;
            }

            members = true;
        }

        return false;
    }

    // One member of a class: a character, a range of two, or a class escape.
    private bool Member(StringBuilder written, bool negated, ref bool any)
    {
        if (!Single(negated, ref any, out var low, out var text))
        {
            return false;
        }

        if (low is not { } from || !(Peek('-') && _i + 1 < _p.Length && _p[_i + 1] != ']'))
        {
            written.Append(text);
            return true;
        }

        _i++;
        if (Peek('[') || !Single(negated, ref any, out var high, out var highText) || high is not { } to || to < from)
        {
            return false;
        }

        written.Append(text).Append('-').Append(highText);
        return true;
    }

    // A character of a class — `low` is it — or a class escape, which has none. Every ASCII punctuation mark is written
    // escaped, so no `(`, `|`, `&&` or `--` of the `v` flag's is ever left bare.
    private bool Single(bool negated, ref bool any, out char? low, out string text)
    {
        (low, text) = (null, "");
        var c = _p[_i];
        if (c != '\\')
        {
            _i++;
            (low, text) = (c, InClass(c));
            return !char.IsSurrogate(c);
        }

        if (_i + 1 >= _p.Length)
        {
            return false;
        }

        var e = _p[_i + 1];
        _i += 2;
        switch (e)
        {
            case 'd':
                text = @"\p{Nd}";
                return true;
            case 'D':
                (text, any) = (@"\P{Nd}", true);
                return true;
            case 's':
                text = @"\s\x85";
                return !negated;
            case 'p' or 'P':
                any |= e == 'P';
                var name = Category();
                text = "\\" + e + "{" + name + "}";
                return name is not null;
            case 't' or 'n' or 'r' or 'f' or 'v':
                (low, text) = (Control(e), "\\" + e);
                return true;
            case 'x' or 'u':
                var start = _i - 2;
                low = Hex(e);
                text = _p[start.._i];
                return low is { } code && !char.IsSurrogate(code);
            default:
                (low, text) = (e, InClass(e));
                return IsAsciiPunctuation(e) || e == ' ';
        }
    }

    private static char Control(char letter) => letter switch
    {
        't' => '\t',
        'n' => '\n',
        'r' => '\r',
        'f' => '\f',
        _ => '\v',
    };

    private static string InClass(char c) => IsAsciiPunctuation(c) && c is not ('"' or '\'' or '_') ? "\\" + c : c.ToString();

    private static bool IsSyntax(char c) => c is '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/';

    private static bool IsAsciiPunctuation(char c) => char.IsAscii(c) && (char.IsPunctuation(c) || char.IsSymbol(c));
}

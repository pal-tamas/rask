using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Testing;

/// <summary>
///     A text a simple regular expression matches: literals, <c>.</c>, <c>\d</c>, <c>\w</c>, <c>\s</c>, character classes,
///     groups (the first alternative), anchors and repeats (each taken the fewest times it may be, and once for
///     <c>+</c>). Anything else — a lookaround, a backreference, a negated class it cannot answer — is no sample.
/// </summary>
internal sealed class PatternSample
{
    private readonly string _pattern;
    private int _at;

    private PatternSample(string pattern) => _pattern = pattern;

    private bool AtEnd => _at >= _pattern.Length;

    private char Next => _pattern[_at];

    /// <summary>A text <paramref name="pattern" /> matches, or null when the pattern is beyond what this builds.</summary>
    public static string? For(string pattern)
    {
        var sample = new StringBuilder();
        var reader = new PatternSample(pattern);
        if (!reader.Alternatives(sample) || !reader.AtEnd)
        {
            return null;
        }

        var text = sample.ToString();
        return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? text : null;
    }

    // The first alternative is the sample; the others are read past, to the group's end.
    private bool Alternatives(StringBuilder sample)
    {
        if (!Sequence(sample))
        {
            return false;
        }

        while (!AtEnd && Next == '|')
        {
            _at++;
            if (!Sequence(new StringBuilder()))
            {
                return false;
            }
        }

        return true;
    }

    private bool Sequence(StringBuilder sample)
    {
        while (!AtEnd && Next is not ('|' or ')'))
        {
            if (Atom() is not { } piece || Repeats() is not { } times)
            {
                return false;
            }

            for (var i = 0; i < times; i++)
            {
                sample.Append(piece);
            }
        }

        return true;
    }

    private string? Atom()
    {
        var c = _pattern[_at++];
        return c switch
        {
            '^' or '$' => "",
            '.' => "a",
            '\\' => Escaped(inClass: false),
            '[' => Class(),
            '(' => Group(),
            '*' or '+' or '?' or '{' or ')' => null,
            _ => c.ToString(),
        };
    }

    private string? Escaped(bool inClass)
    {
        if (AtEnd)
        {
            return null;
        }

        var c = _pattern[_at++];
        return c switch
        {
            'd' => "1",
            'w' or 'D' or 'S' => "a",
            's' => " ",
            'W' => "-",
            'n' => "\n",
            't' => "\t",
            'r' => "\r",
            'b' when inClass => "\b",
            _ when char.IsLetterOrDigit(c) => null,
            _ => c.ToString(),
        };
    }

    // [A-Z0-9-] is its first member's first character; [^…] the first of a few plain ones it does not hold.
    private string? Class()
    {
        var negated = !AtEnd && Next == '^';
        if (negated)
        {
            _at++;
        }

        var members = new List<(char From, char To)>();
        var first = true;
        while (!AtEnd && (Next != ']' || first))
        {
            first = false;
            if (Member() is not { } member)
            {
                return null;
            }

            members.Add(member);
        }

        if (AtEnd || members.Count == 0)
        {
            return null;
        }

        _at++;
        if (!negated)
        {
            return members[0].From.ToString();
        }

        var outside = "aA1-_ x".Where(c => !members.Exists(m => c >= m.From && c <= m.To)).Take(1).ToArray();
        return outside.Length == 0 ? null : new string(outside);
    }

    private (char From, char To)? Member()
    {
        var c = _pattern[_at++];
        if (c == '\\')
        {
            // \d \w \s inside a class stand for the character Escaped picks for them.
            return Escaped(inClass: true) is [var escaped] ? (escaped, escaped) : null;
        }

        if (_at + 1 < _pattern.Length && Next == '-' && _pattern[_at + 1] != ']')
        {
            _at++;
            var to = _pattern[_at++];
            return to == '\\' ? null : (c, to);
        }

        return (c, c);
    }

    private string? Group()
    {
        if (!AtEnd && Next == '?')
        {
            // (?:…) and named groups (?<name>…) build like a plain group; a lookaround or an inline option does not.
            if (_pattern.AsSpan(_at).StartsWith("?:", StringComparison.Ordinal))
            {
                _at += 2;
            }
            else if (_pattern.AsSpan(_at).StartsWith("?<", StringComparison.Ordinal) && _at + 2 < _pattern.Length
                     && _pattern[_at + 2] is not ('=' or '!'))
            {
                var close = _pattern.IndexOf('>', _at);
                if (close < 0)
                {
                    return null;
                }

                _at = close + 1;
            }
            else
            {
                return null;
            }
        }

        var inner = new StringBuilder();
        if (!Alternatives(inner) || AtEnd || Next != ')')
        {
            return null;
        }

        _at++;
        return inner.ToString();
    }

    // How many times the piece before is taken: the fewest it may be, once for `+`; a lazy `?` after it changes nothing.
    private int? Repeats()
    {
        if (AtEnd || Next is not ('*' or '?' or '+' or '{'))
        {
            return 1;
        }

        var times = Next switch
        {
            '*' or '?' => 0,
            '+' => 1,
            _ => Counted(),
        };

        if (times is null)
        {
            return null;
        }

        // Past the quantifier, or a counted one's closing brace, and a lazy `?` after it.
        _at++;
        if (!AtEnd && Next == '?')
        {
            _at++;
        }

        return times;
    }

    // {n}, {n,} and {n,m}: n. Leaves the reader on the closing brace.
    private int? Counted()
    {
        var close = _pattern.IndexOf('}', _at);
        if (close < 0)
        {
            return null;
        }

        var body = _pattern.AsSpan(_at + 1, close - _at - 1);
        var comma = body.IndexOf(',');
        var least = comma < 0 ? body : body[..comma];
        if (!int.TryParse(least, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return null;
        }

        _at = close;
        return count;
    }
}

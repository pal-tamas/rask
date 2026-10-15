using System.Text;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     A C# source with its comments taken out and everything else left byte for byte.
/// </summary>
/// <remarks>
///     <para>
///         Tailwind reads every word of a scanned file as a candidate class, and prose is words. A doc
///         comment saying "a magnifying glass" put daisyUI's <c>.glass</c> into the kit's sheet and into
///         every app compiled from its class list (#1214). The code is kept whole — a class is written
///         as a string literal, but a scan that saw only literals would be a different scan, and the
///         sheet must not lose a rule some other token was carrying.
///     </para>
///     <para>
///         So this is a lexer for exactly as much of C# as tells a comment from the text around it:
///         <c>//</c> and <c>/* */</c> outside a literal, and every literal that can hold those two
///         characters — a character, a regular, verbatim or raw string, interpolated or not, with code
///         (and so more literals, and more comments) inside an interpolation's holes. A comment becomes
///         a single space, or its line breaks, so the words either side of it stay apart and every line
///         keeps its number.
///     </para>
/// </remarks>
internal static class CSharpComments
{
    public static string Strip(string source)
    {
        var lexer = new Lexer(source);
        lexer.Code(inHole: false);
        return lexer.Output.ToString();
    }

    private sealed class Lexer(string source)
    {
        private int _at;

        public StringBuilder Output { get; } = new(source.Length);

        /// <summary>Code, up to the end — or, inside an interpolation's hole, up to the brace closing it.</summary>
        public void Code(bool inHole)
        {
            var depth = 0;
            while (_at < source.Length)
            {
                var c = source[_at];
                if (c == '/' && Peek(1) == '/')
                {
                    LineComment();
                }
                else if (c == '/' && Peek(1) == '*')
                {
                    BlockComment();
                }
                else if (c == '\'')
                {
                    CharLiteral();
                }
                else if (IsStringStart())
                {
                    StringLiteral();
                }
                else if (inHole && c == '}' && depth == 0)
                {
                    return;
                }
                else
                {
                    depth += Nesting(c);
                    Copy(1);
                }
            }
        }

        private static int Nesting(char c) => c switch
        {
            '{' => 1,
            '}' => -1,
            _ => 0,
        };

        private void LineComment()
        {
            while (_at < source.Length && source[_at] is not ('\n' or '\r'))
            {
                _at++;
            }
        }

        private void BlockComment()
        {
            var lineBreaks = new StringBuilder();
            _at += 2;
            while (_at < source.Length && !(source[_at] == '*' && Peek(1) == '/'))
            {
                if (source[_at] is '\n' or '\r')
                {
                    lineBreaks.Append(source[_at]);
                }

                _at++;
            }

            _at = System.Math.Min(_at + 2, source.Length);
            Output.Append(lineBreaks.Length == 0 ? " " : lineBreaks.ToString());
        }

        private void CharLiteral()
        {
            Copy(1);
            while (_at < source.Length && source[_at] is not ('\'' or '\n' or '\r'))
            {
                Copy(source[_at] == '\\' ? 2 : 1);
            }

            Copy(1);
        }

        /// <summary>A <c>"</c>, after any number of <c>$</c> and at most one <c>@</c>, in either order.</summary>
        private bool IsStringStart()
        {
            var at = _at;
            var verbatim = false;
            while (at < source.Length && (source[at] == '$' || (source[at] == '@' && !verbatim)))
            {
                verbatim |= source[at] == '@';
                at++;
            }

            return at < source.Length && source[at] == '"';
        }

        private void StringLiteral()
        {
            var dollars = 0;
            var verbatim = false;
            while (source[_at] != '"')
            {
                dollars += source[_at] == '$' ? 1 : 0;
                verbatim |= source[_at] == '@';
                Copy(1);
            }

            var quotes = Run('"');
            if (!verbatim && quotes >= 3)
            {
                RawString(quotes, dollars);
            }
            else if (!verbatim && quotes == 2)
            {
                Copy(2);
            }
            else
            {
                Copy(1);
                QuotedString(verbatim, dollars > 0);
            }
        }

        /// <summary>The body of a regular or verbatim string, after its opening quote, through its closing one.</summary>
        private void QuotedString(bool verbatim, bool interpolated)
        {
            while (_at < source.Length)
            {
                var c = source[_at];
                if (c == '"' && verbatim && Peek(1) == '"')
                {
                    Copy(2);
                }
                else if (c == '"')
                {
                    Copy(1);
                    return;
                }
                else if (c == '\\' && !verbatim)
                {
                    Copy(2);
                }
                else if (!verbatim && c is '\n' or '\r')
                {
                    return;
                }
                else if (c == '{' && interpolated)
                {
                    BracesOrHole(1);
                }
                else
                {
                    Copy(1);
                }
            }
        }

        /// <summary>The body of a raw string, after its opening quotes, through a run of as many closing ones.</summary>
        private void RawString(int quotes, int dollars)
        {
            Copy(quotes);
            while (_at < source.Length)
            {
                var c = source[_at];
                if (c == '"')
                {
                    var run = Run('"');
                    Copy(run);
                    if (run >= quotes)
                    {
                        return;
                    }
                }
                else if (c == '{' && dollars > 0)
                {
                    BracesOrHole(dollars);
                }
                else
                {
                    Copy(1);
                }
            }
        }

        /// <summary>
        ///     A run of opening braces in an interpolated string: text when it is shorter than the run that
        ///     opens a hole (<c>{{</c> in <c>$"…"</c>), otherwise the braces and the code of the hole.
        /// </summary>
        private void BracesOrHole(int opener)
        {
            var run = Run('{');
            var isHole = opener == 1 ? run % 2 == 1 : run >= opener;
            Copy(run);
            if (isHole)
            {
                Code(inHole: true);
            }
        }

        private int Run(char c)
        {
            var end = _at;
            while (end < source.Length && source[end] == c)
            {
                end++;
            }

            return end - _at;
        }

        private char Peek(int offset) => _at + offset < source.Length ? source[_at + offset] : '\0';

        private void Copy(int count)
        {
            var end = System.Math.Min(_at + count, source.Length);
            Output.Append(source, _at, end - _at);
            _at = end;
        }
    }
}

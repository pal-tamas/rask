using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rask.Tailwind.Tasks;

/// <summary>
///     Every class name a compiled stylesheet has a selector for, as it is written in markup.
/// </summary>
/// <remarks>
///     <para>
///         A compiled sheet is Tailwind's verdict on a project's sources: the class names it found there
///         that mean something. Read back out of the selectors, they are the list another project's
///         Tailwind build needs in order to emit the same rules — which is how a component library that
///         ships as an assembly gets its classes into an app's one stylesheet.
///     </para>
///     <para>
///         Selectors only: a declaration's value (<c>1.5rem</c>, <c>url(a.png)</c>), a string, a comment
///         and an at-rule's prelude (<c>@layer daisyui.l1</c>) all carry dots that are not classes.
///         Every class in a selector is taken, not just the first, because a variant can put the
///         candidate anywhere (<c>:where(.dark) .x</c>); a marker class that rides along (<c>group</c>,
///         <c>dark</c>) names no utility and emits nothing.
///     </para>
/// </remarks>
internal static class CssClassNames
{
    public static IReadOnlyList<string> In(string css)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var prelude = new StringBuilder();
        var at = 0;

        while (at < css.Length)
        {
            var c = css[at];
            if (c == '/' && Peek(css, at + 1) == '*')
            {
                at = AfterComment(css, at);
            }
            else if (c is '"' or '\'')
            {
                at = AfterString(css, at);
                prelude.Append(' ');
            }
            else if (c == '\\' && at + 1 < css.Length)
            {
                prelude.Append(c).Append(css[at + 1]);
                at += 2;
            }
            else
            {
                at++;
                Step(c, prelude, names);
            }
        }

        return [.. names];
    }

    // `{` closes a prelude that is a selector (or an at-rule's, which is skipped); `;` and `}` close one
    // that was a declaration.
    private static void Step(char c, StringBuilder prelude, SortedSet<string> names)
    {
        if (c == '{')
        {
            AddSelectorClasses(prelude.ToString(), names);
            prelude.Clear();
        }
        else if (c is ';' or '}')
        {
            prelude.Clear();
        }
        else
        {
            prelude.Append(c);
        }
    }

    private static char Peek(string text, int at) => at < text.Length ? text[at] : '\0';

    private static int AfterComment(string css, int start)
    {
        var end = css.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return end < 0 ? css.Length : end + 2;
    }

    private static int AfterString(string css, int start)
    {
        var at = start + 1;
        while (at < css.Length && css[at] != css[start])
        {
            at += css[at] == '\\' ? 2 : 1;
        }

        return Math.Min(at + 1, css.Length);
    }

    private static void AddSelectorClasses(string selector, SortedSet<string> names)
    {
        if (selector.TrimStart().StartsWith("@", StringComparison.Ordinal))
        {
            return;
        }

        var at = 0;
        while (at < selector.Length)
        {
            if (selector[at] == '\\')
            {
                at += 2;
            }
            else if (selector[at] == '.' && StartsIdentifier(Peek(selector, at + 1)))
            {
                var name = new StringBuilder();
                at = ReadIdentifier(selector, at + 1, name);
                if (name.Length > 1 || (name.Length == 1 && name[0] != '-'))
                {
                    names.Add(name.ToString());
                }
            }
            else
            {
                at++;
            }
        }
    }

    private static bool StartsIdentifier(char c) =>
        c is '-' or '_' or '\\' || char.IsLetter(c) || c > 0x7F;

    private static bool ContinuesIdentifier(char c) =>
        c is '-' or '_' || char.IsLetterOrDigit(c) || c > 0x7F;

    // Returns the index after the identifier.
    private static int ReadIdentifier(string selector, int start, StringBuilder name)
    {
        var at = start;
        while (at < selector.Length)
        {
            if (selector[at] == '\\' && at + 1 < selector.Length)
            {
                at = Unescape(selector, at + 1, name);
            }
            else if (ContinuesIdentifier(selector[at]))
            {
                name.Append(selector[at]);
                at++;
            }
            else
            {
                break;
            }
        }

        return at;
    }

    // CSS escapes come in two shapes: up to six hex digits and one optional space (`\32 xl` is `2xl`), or
    // the character itself (`\:`). Returns the index after the escape.
    private static int Unescape(string selector, int start, StringBuilder name)
    {
        var end = start;
        while (end < selector.Length && end - start < 6 && Uri.IsHexDigit(selector[end]))
        {
            end++;
        }

        if (end == start)
        {
            name.Append(selector[start]);
            return start + 1;
        }

        var code = int.Parse(selector.Substring(start, end - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        name.Append(char.ConvertFromUtf32(code));
        return end < selector.Length && char.IsWhiteSpace(selector[end]) ? end + 1 : end;
    }
}

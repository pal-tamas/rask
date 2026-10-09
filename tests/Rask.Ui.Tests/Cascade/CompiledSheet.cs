namespace Rask.UiTests.Cascade;

/// <summary>One style rule of a compiled sheet, for one of its selectors.</summary>
/// <param name="Selector">The selector, parsed.</param>
/// <param name="Text">The selector as the sheet writes it.</param>
/// <param name="Layer">Its cascade layer's rank; an unlayered rule outranks them all.</param>
/// <param name="Order">Where it stands in the sheet.</param>
/// <param name="Declarations">What it sets, by physical longhand.</param>
internal sealed record CssRule(
    CssSelector Selector,
    string Text,
    int Layer,
    int Order,
    IReadOnlyDictionary<string, (string Value, bool Important)> Declarations)
{
    /// <summary>Whether this rule beats <paramref name="other" /> for <paramref name="property" /> where both apply.</summary>
    public bool Beats(CssRule other, string property)
    {
        var mine = (Declarations[property].Important, Layer, Selector.Weight, Order);
        var theirs = (other.Declarations[property].Important, other.Layer, other.Selector.Weight, other.Order);

        // An important declaration turns the layers round: the earlier layer's wins.
        return mine.Important && theirs.Important && Layer != other.Layer ? Layer < other.Layer : mine.CompareTo(theirs) > 0;
    }
}

/// <summary>
///     A compiled stylesheet as the cascade reads it: every rule that is not behind a media or container
///     query, with its layer, its weight and its place — enough to say which of two utilities on one element
///     has the last word.
/// </summary>
internal sealed class CompiledSheet
{
    private const int Unlayered = int.MaxValue;

    // How a shorthand or a logical property lands on the physical longhands, left to right.
    private static readonly Dictionary<string, string[]> Longhands = new(StringComparer.Ordinal)
    {
        ["padding"] = ["padding-top", "padding-right", "padding-bottom", "padding-left"],
        ["padding-inline"] = ["padding-left", "padding-right"],
        ["padding-block"] = ["padding-top", "padding-bottom"],
        ["padding-inline-start"] = ["padding-left"],
        ["padding-inline-end"] = ["padding-right"],
        ["padding-block-start"] = ["padding-top"],
        ["padding-block-end"] = ["padding-bottom"],
        ["margin"] = ["margin-top", "margin-right", "margin-bottom", "margin-left"],
        ["margin-inline"] = ["margin-left", "margin-right"],
        ["margin-block"] = ["margin-top", "margin-bottom"],
        ["margin-inline-start"] = ["margin-left"],
        ["margin-inline-end"] = ["margin-right"],
        ["margin-block-start"] = ["margin-top"],
        ["margin-block-end"] = ["margin-bottom"],
        ["border-radius"] = ["border-top-left-radius", "border-top-right-radius", "border-bottom-right-radius", "border-bottom-left-radius"],
        ["border-start-start-radius"] = ["border-top-left-radius"],
        ["border-start-end-radius"] = ["border-top-right-radius"],
        ["border-end-end-radius"] = ["border-bottom-right-radius"],
        ["border-end-start-radius"] = ["border-bottom-left-radius"],
        ["gap"] = ["row-gap", "column-gap"],
        ["inline-size"] = ["width"],
        ["min-inline-size"] = ["min-width"],
        ["max-inline-size"] = ["max-width"],
        ["overflow"] = ["overflow-x", "overflow-y"],
    };

    private readonly List<string> _layers = [];
    private readonly Dictionary<string, List<CssRule>> _byClass = new(StringComparer.Ordinal);
    private readonly List<CssRule> _classless = [];
    private int _order;

    private CompiledSheet()
    {
    }

    public static CompiledSheet Parse(string css)
    {
        var sheet = new CompiledSheet();
        var at = 0;
        sheet.Block(css, ref at, Unlayered);
        return sheet;
    }

    /// <summary>The rule that sets <paramref name="property" /> on the element as rendered, if any does.</summary>
    /// <remarks>
    ///     A rule that depends on where the element sits among its siblings counts as applying: a cell is the
    ///     first of its row somewhere.
    /// </remarks>
    public CssRule? Winner(StyledElement element, string property)
    {
        CssRule? winner = null;
        foreach (var rule in element.Classes.SelectMany(name => _byClass.GetValueOrDefault(name) ?? []).Concat(_classless))
        {
            if (rule.Declarations.ContainsKey(property)
                && rule.Selector.On(element) >= Holds.ByPosition
                && (winner is null || rule.Beats(winner, property)))
            {
                winner = rule;
            }
        }

        return winner;
    }

    private void Block(string css, ref int at, int layer)
    {
        while (at < css.Length)
        {
            var prelude = ReadUntil(css, ref at, out var end).Trim();
            if (end == '}')
            {
                return;
            }

            if (end == ';')
            {
                Statement(prelude);
            }
            else if (end == '{')
            {
                Open(css, ref at, prelude, layer);
            }
        }
    }

    private void Statement(string prelude)
    {
        if (prelude.StartsWith("@layer", StringComparison.Ordinal))
        {
            foreach (var name in prelude[6..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                _ = Rank(name);
            }
        }
    }

    private void Open(string css, ref int at, string prelude, int layer)
    {
        if (!prelude.StartsWith('@'))
        {
            Rule(prelude, layer, ReadDeclarations(css, ref at));
        }
        else if (prelude.StartsWith("@layer", StringComparison.Ordinal))
        {
            Block(css, ref at, Rank(prelude[6..].Trim()));
        }
        else if (prelude.StartsWith("@supports", StringComparison.Ordinal))
        {
            // Tailwind's fallbacks for colour functions: true in every browser the kit supports.
            Block(css, ref at, layer);
        }
        else
        {
            // A media or container query is a state, and keyframes, fonts and properties are not rules.
            Skip(css, ref at);
        }
    }

    private int Rank(string name)
    {
        var rank = _layers.IndexOf(name);
        if (rank < 0)
        {
            _layers.Add(name);
            rank = _layers.Count - 1;
        }

        return rank;
    }

    private void Rule(string prelude, int layer, Dictionary<string, (string Value, bool Important)> declarations)
    {
        if (declarations.Count == 0)
        {
            return;
        }

        var order = _order++;
        foreach (var text in CssSelector.SplitTopLevel(prelude, ','))
        {
            var rule = new CssRule(CssSelector.ParseList(text)[0], text, layer, order, declarations);
            var named = rule.Selector.SubjectClasses().FirstOrDefault();
            if (named is null)
            {
                _classless.Add(rule);
            }
            else if (_byClass.TryGetValue(named, out var rules))
            {
                rules.Add(rule);
            }
            else
            {
                _byClass[named] = [rule];
            }
        }
    }

    private static Dictionary<string, (string Value, bool Important)> ReadDeclarations(string css, ref int at)
    {
        var declarations = new Dictionary<string, (string Value, bool Important)>(StringComparer.Ordinal);
        while (at < css.Length)
        {
            var text = ReadUntil(css, ref at, out var end).Trim();
            if (end == '{')
            {
                // A nested rule: the compiled sheet is flat, and what a nested block says is another selector's.
                Skip(css, ref at);
                continue;
            }

            var colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && !text.StartsWith("--", StringComparison.Ordinal))
            {
                var value = text[(colon + 1)..].Trim();
                var important = value.EndsWith("!important", StringComparison.Ordinal);
                var name = text[..colon].Trim();
                foreach (var longhand in Longhands.GetValueOrDefault(name) ?? [name])
                {
                    declarations[longhand] = (important ? value[..^10].Trim() : value, important);
                }
            }

            if (end == '}')
            {
                break;
            }
        }

        return declarations;
    }

    private static void Skip(string css, ref int at)
    {
        for (var depth = 1; at < css.Length && depth > 0;)
        {
            _ = ReadUntil(css, ref at, out var end);
            depth += end == '{' ? 1 : end == '}' ? -1 : 0;
        }
    }

    // The text up to the next `{`, `}` or `;` that is outside every string, bracket, escape and comment.
    private static string ReadUntil(string css, ref int at, out char end)
    {
        var start = at;
        var depth = 0;
        char quote = default;
        for (; at < css.Length; at++)
        {
            var c = css[at];
            if (c == '\\')
            {
                at++;
            }
            else if (quote != default)
            {
                quote = c == quote ? default : quote;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '/' && at + 1 < css.Length && css[at + 1] == '*')
            {
                var close = css.IndexOf("*/", at + 2, StringComparison.Ordinal);
                var before = css[start..at];
                at = close < 0 ? css.Length : close + 2;
                return before + ReadUntil(css, ref at, out end);
            }
            else if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth--;
            }
            else if (depth == 0 && c is '{' or '}' or ';')
            {
                end = c;
                return css[start..at++];
            }
        }

        end = '}';
        return css[start..Math.Min(at, css.Length)];
    }
}

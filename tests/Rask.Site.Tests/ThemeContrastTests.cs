using System.Globalization;
using System.Text.RegularExpressions;
using Rask.Site.Tests.Infrastructure;
using Rask.Ui;

namespace Rask.Site.Tests;

/// <summary>
///     Every colour pair the showcase writes clears WCAG AA, in every palette a reader can pick.
/// </summary>
/// <remarks>
///     <para>
///     The site carries a theme picker, so "readable" is a claim about thirty-six palettes rather than
///     about the one the design was drawn in. Nothing else in the suite can see this: markup assertions
///     read class names, and the class names were right the whole time — <c>text-ui-muted</c> was present
///     and correct on all 282 elements that write it while resolving to 1.26:1 on daisyUI's own
///     <c>dark</c>, because <c>--color-ui-muted</c> aliased <c>--color-neutral</c>, which daisyUI
///     publishes as a SURFACE. Seventeen palettes failed, including both of the two the operating system
///     can select on its own.
///     </para>
///     <para>
///     So this computes. It reads the theme palettes out of the COMPILED stylesheet the browser gets
///     (<see cref="UiStylesheet.Css" />), reads the showcase's token formulas out of its own
///     <c>Styles/app.css</c>, applies the kit's per-theme corrections from <c>Styles/ui.css</c>, resolves
///     <c>var()</c> and <c>color-mix()</c> the way a browser would, and measures the ratio. A weight
///     edited by hand in either sheet fails here rather than on a page.
///     </para>
///     <para>
///     It is arithmetic on the shipped bytes, not a screenshot, which is what makes it cheap enough to
///     run in the unit suite: 36 palettes × 24 pairs in a few milliseconds, with no browser. What it
///     cannot see is a colour that arrives from somewhere other than these sheets — a literal hex in a
///     component's scoped CSS, or a raw Tailwind hue like the <c>sky-*</c> family this file's first run
///     found in <c>Tw.cs</c>. <c>ChromeStylesheetTests</c> and the no-literal-colour assertion below are
///     what keep that surface closed.
///     </para>
/// </remarks>
public sealed partial class ThemeContrastTests
{
    /// <summary>WCAG AA for normal-size text. The showcase writes <c>text-sm</c> and <c>text-xs</c>.</summary>
    private const double AA = 4.5;

    /// <summary>
    ///     The pairs, as (text token, the surface it is read on). Both sides are CSS expressions in the
    ///     showcase's own vocabulary, so they are resolved by the same evaluator the sheets are.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Listed rather than inferred, and that is the honest shape for this: a stylesheet does not record
    ///     which background a text colour ends up on, so the pairing is a claim about the design that a
    ///     human makes. <c>Every_text_token_the_pages_write_is_measured</c> is what stops the list going
    ///     stale — a new <c>text-ui-*</c> class anywhere in the app fails that test until it appears here
    ///     with the surfaces it is used on.
    ///     </para>
    ///     <para>
    ///     The inverted rows are not redundant. Contrast is symmetric, so <c>ui-bg</c> read on a
    ///     <c>ui-brand-ink</c> fill is the same number as <c>ui-brand-ink</c> read on <c>ui-bg</c> — and
    ///     that symmetry is precisely why a filled control in <c>Tw.cs</c> is
    ///     <c>bg-ui-brand-ink text-ui-bg</c> rather than a saturated fill with a white label. They are
    ///     spelled out anyway so the table reads as the design does.
    ///     </para>
    /// </remarks>
    private static readonly (string Text, string On, string What)[] Pairs =
    [
        // Body copy and chrome.
        ("--color-ui-ink", "--color-ui-bg", "body text on a card"),
        ("--color-ui-ink", "--color-ui-well", "body text on the page ground"),
        ("--color-ui-ink", "--color-ui-panel", "body text on a panel"),
        ("--color-ui-muted", "--color-ui-bg", "secondary text on a card"),
        ("--color-ui-muted", "--color-ui-well", "secondary text on the page ground"),

        // The -ink tiers on the two grounds and on their own wash.
        ("--color-ui-brand-ink", "--color-ui-bg", "brand text on a card"),
        ("--color-ui-brand-ink", "--color-ui-well", "brand text on the page ground"),
        ("--color-ui-brand-ink", "--color-ui-brand-surface", "brand text in a brand badge"),
        ("--color-ui-ok-ink", "--color-ui-bg", "success text on a card"),
        ("--color-ui-ok-ink", "--color-ui-well", "success text on the page ground"),
        ("--color-ui-ok-ink", "--color-ui-ok-surface", "success text in a success badge"),
        ("--color-ui-warn-ink", "--color-ui-bg", "warning text on a card"),
        ("--color-ui-warn-ink", "--color-ui-well", "warning text on the page ground"),
        ("--color-ui-warn-ink", "--color-ui-warn-surface", "warning text in a warning badge"),
        ("--color-ui-danger-ink", "--color-ui-bg", "error text on a card"),
        ("--color-ui-danger-ink", "--color-ui-well", "error text on the page ground"),
        ("--color-ui-danger-ink", "--color-ui-danger-surface", "error text in an error badge"),
        ("--color-ui-info-ink", "--color-ui-bg", "info text on a card"),
        ("--color-ui-info-ink", "--color-ui-well", "info text on the page ground"),
        ("--color-ui-info-ink", "--color-ui-info-surface", "info text in an info badge"),

        // The three daisyUI tones the kit's own palette never named. Nothing in the showcase writes
        // text-ui-secondary-ink, so the coverage test below does not demand these — but the kit's
        // .btn-secondary / .badge-accent / .tooltip-neutral are filled with them and labelled with the
        // ground, so the pair has to hold here or those components are unreadable.
        ("--color-ui-secondary-ink", "--color-ui-bg", "a secondary fill's label"),
        ("--color-ui-secondary-ink", "--color-ui-well", "secondary text on the page ground"),
        ("--color-ui-accent-ink", "--color-ui-bg", "an accent fill's label"),
        ("--color-ui-accent-ink", "--color-ui-well", "accent text on the page ground"),
        ("--color-ui-neutral-ink", "--color-ui-bg", "a neutral fill's label"),
        ("--color-ui-neutral-ink", "--color-ui-well", "neutral text on the page ground"),

        // Filled controls: the ground read on an -ink fill (Tw.Btn*).
        ("--color-ui-bg", "--color-ui-ink", "a dark button's label"),
        ("--color-ui-bg", "--color-ui-muted", "a dark button's label, hovered"),
        ("--color-ui-bg", "--color-ui-brand-ink", "a primary button's label"),
        ("--color-ui-bg", "--color-ui-ok-ink", "a success button's label"),
        ("--color-ui-bg", "--color-ui-warn-ink", "a warning button's label"),
        ("--color-ui-bg", "--color-ui-danger-ink", "an error button's label"),
        ("--color-ui-bg", "--color-ui-info-ink", "an info button's label"),

        // Tw.BtnSecondary's hover, which is the one alpha fill left in the vocabulary.
        (
            "--color-ui-ink",
            "color-mix(in srgb, var(--color-ui-line) 40%, var(--color-ui-well))",
            "a secondary button's label, hovered"),
    ];

    [Fact]
    public void Every_pair_clears_AA_in_every_palette_a_reader_can_pick()
    {
        var palettes = Palettes();
        var tokens = ShowcaseTokens();
        var corrections = KitCorrections();

        // Vacuous-pass guards. Each of these three readers is a regex over a file that is free to move,
        // and a parser that quietly stopped matching would turn every assertion below into a no-op — the
        // exact failure mode this file exists to catch.
        Assert.True(palettes.Count >= 30, $"only {palettes.Count} palette(s) parsed out of the kit's sheet.");
        Assert.True(tokens.Count >= 18, $"only {tokens.Count} token(s) parsed out of the showcase's @theme.");
        Assert.True(
            corrections.Count >= 5,
            $"only {corrections.Count} per-theme correction block(s) parsed out of the kit's sheet; the six "
            + "low-contrast palettes depend on them.");

        var failures = new List<string>();

        foreach (var (theme, palette) in palettes.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // The declarations a browser would have in scope on <html>: daisyUI's palette for this theme,
            // the showcase's own @theme on top, then the kit's correction for this theme last — which is
            // the order the cascade reads them in, the corrections living in a later layer.
            var scope = new Dictionary<string, string>(palette, StringComparer.Ordinal);
            foreach (var (name, value) in tokens)
            {
                scope[name] = value;
            }

            if (corrections.TryGetValue(theme, out var fixes))
            {
                foreach (var (name, value) in fixes)
                {
                    scope[name] = value;
                }
            }

            foreach (var (text, on, what) in Pairs)
            {
                var fg = Resolve(text.StartsWith("--", StringComparison.Ordinal) ? $"var({text})" : text, scope);
                var bg = Resolve(on.StartsWith("--", StringComparison.Ordinal) ? $"var({on})" : on, scope);
                var ratio = Contrast(fg, bg);

                if (ratio < AA)
                {
                    // Invariant, not the machine's culture: a ratio is a WCAG number and reading it back
                    // as "1,26:1" in a failure message invites the wrong kind of second look.
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{theme}: {what} measures {ratio:0.00}:1 — {text} on {on}"));
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} pair(s) fail WCAG AA ({AA}:1) in a palette the theme picker offers. A reader "
            + "who picks one of these gets text they cannot read, on a page whose class names are all "
            + "correct. Fix the token in src/Rask.Ui/Styles/ui.css (and the showcase's copy of the @theme "
            + "in src/Rask.Site/Styles/app.css), or add a per-theme correction beside the others:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(40)));
    }

    [Fact]
    public void Every_text_token_the_pages_write_is_measured()
    {
        var written = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(SiteRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // Comments name these classes to explain them — including the one beside Tw.CheckInput
            // saying which token NOT to use — and a scanner that reads prose reports a class the
            // markup never writes.
            foreach (Match m in TextUtility().Matches(CSharpComment().Replace(File.ReadAllText(file), " ")))
            {
                written.Add("--color-ui-" + m.Groups["token"].Value);
            }
        }

        Assert.True(written.Count >= 6, $"only {written.Count} text-ui-* utility/utilities found under src/Rask.Site.");

        var measured = Pairs.Select(p => p.Text).ToHashSet(StringComparer.Ordinal);
        var unmeasured = written.Where(t => !measured.Contains(t)).ToArray();

        Assert.True(
            unmeasured.Length == 0,
            $"the pages write {string.Join(", ", unmeasured)} as TEXT, and no row in Pairs measures it. Add "
            + "the surfaces it is read on — a text colour nobody measured is how text-ui-muted shipped at "
            + "1.26:1 on daisyUI's own dark theme with every class name in the markup correct.");
    }

    [Fact]
    public void The_showcase_declares_no_colour_of_its_own()
    {
        var failures = new List<string>();

        foreach (var (name, value) in ShowcaseTokens())
        {
            if (LiteralColour().IsMatch(value))
            {
                failures.Add($"{name}: {value}");
            }

            // `black` and `white` are the specific trap. Mixing toward them is the obvious way to darken a
            // status colour for a light page, it reads as theme-agnostic because it names no palette, and
            // it is the single assumption this whole file exists to have caught: on a dark theme it pushes
            // the colour INTO its own background. The kit's success ink measured 3.26:1 on `dark` and
            // 1.46:1 on `aqua` while it was written that way. Mix toward --color-base-content instead,
            // which is the ground's opposite in every palette and so moves the right way in both.
            if (BlackOrWhite().IsMatch(value))
            {
                failures.Add($"{name}: {value} — mixes toward black/white rather than --color-base-content");
            }
        }

        Assert.True(
            failures.Count == 0,
            "the showcase's @theme pins a colour that cannot follow the theme: "
            + string.Join("; ", failures));
    }

    // ---- readers -------------------------------------------------------------------------------

    /// <summary>
    ///     Every palette in the compiled kit sheet, as <c>theme name -&gt; declarations</c>.
    /// </summary>
    /// <remarks>
    ///     Keyed off <c>--color-base-100</c>, which every theme block declares and nothing else does.
    ///     daisyUI compiles each one as
    ///     <c>[data-rask-ui]:has(input.theme-controller[value=x]:checked),[data-theme=x]</c>, the default
    ///     as <c>:where([data-rask-ui])</c>, and the one the operating system selects as
    ///     <c>[data-rask-ui]:not([data-theme])</c> inside a <c>prefers-color-scheme</c> query — which is
    ///     the palette a reader who has chosen nothing actually gets, so it is measured under its own name
    ///     rather than skipped.
    /// </remarks>
    private static Dictionary<string, Dictionary<string, string>> Palettes()
    {
        var css = UiStylesheet.Css;
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var fallback = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match block in ThemeBlock().Matches(css))
        {
            var selector = block.Groups["selector"].Value;
            var declarations = Stylesheets.Declarations(block.Groups["body"].Value);

            var names = ThemeName().Matches(selector).Select(m => m.Groups["name"].Value).ToArray();

            if (names.Length == 0)
            {
                // The OS-dark block, and the bare default. Both are real palettes a reader reaches without
                // touching the picker, which is exactly why the default used to be the only one measured.
                if (selector.Contains(":not([data-theme])", StringComparison.Ordinal))
                {
                    names = ["(system dark)"];
                }
                else if (selector.Contains(":where([data-rask-ui])", StringComparison.Ordinal))
                {
                    names = ["(system light)"];
                    foreach (var (k, v) in declarations)
                    {
                        fallback[k] = v;
                    }
                }
                else
                {
                    continue;
                }
            }

            foreach (var name in names)
            {
                if (!result.TryGetValue(name, out var existing))
                {
                    result[name] = existing = new Dictionary<string, string>(StringComparer.Ordinal);
                }

                foreach (var (k, v) in declarations)
                {
                    existing[k] = v;
                }
            }
        }

        // A theme block declares only what it changes, so anything it leaves out comes from the default.
        foreach (var palette in result.Values)
        {
            foreach (var (k, v) in fallback)
            {
                _ = palette.TryAdd(k, v);
            }
        }

        return result;
    }

    /// <summary>The showcase's own <c>@theme</c> block — the <c>--color-ui-*</c> formulas.</summary>
    private static Dictionary<string, string> ShowcaseTokens() =>
        Stylesheets
            .Declarations(Stylesheets.ThemeBlock(
                Path.Combine(RepoRoot(), "src", "Rask.Site", "Styles", "app.css")))
            .Where(kv => kv.Key.StartsWith("--color-ui-", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>
    ///     The kit's per-theme corrections, as <c>theme name -&gt; declarations</c>.
    /// </summary>
    /// <remarks>
    ///     Read from <c>ui.css</c> rather than from the compiled sheet on purpose: Tailwind splits every
    ///     <c>color-mix()</c> into a flat fallback plus an <c>@supports</c> copy, so the compiled form
    ///     declares each of these tokens twice and the first one is the unmixed surface colour. Reading the
    ///     source keeps the parser honest about which of the two a browser with <c>color-mix()</c> applies
    ///     — every browser the site supports.
    /// </remarks>
    private static Dictionary<string, Dictionary<string, string>> KitCorrections()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Ui", "Styles", "ui.css"));
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (Match m in Correction().Matches(css))
        {
            result[m.Groups["name"].Value] = Stylesheets.Declarations(m.Groups["body"].Value);
        }

        return result;
    }

    private static string SiteRoot() => Path.Combine(RepoRoot(), "src", "Rask.Site");

    private static string RepoRoot() => Stylesheets.RepoRoot();

    // ---- the evaluator -------------------------------------------------------------------------

    /// <summary>A colour in Oklab, which is the space both sheets mix in.</summary>
    private readonly record struct Lab(double L, double A, double B);

    /// <summary>
    ///     Resolves a CSS colour expression against the declarations in scope.
    /// </summary>
    /// <remarks>
    ///     Only what the two sheets actually use: <c>var()</c>, <c>color-mix()</c> in <c>oklab</c> and
    ///     <c>srgb</c>, <c>oklch()</c> literals, and the <c>black</c>/<c>white</c> keywords — which are
    ///     accepted so the assertion that no token uses them can fail with a measurement rather than a
    ///     parse error. Anything else throws, which is the right outcome: a colour this cannot read is a
    ///     colour it cannot hold to AA, and silently scoring it is how a gate starts lying.
    /// </remarks>
    private static Lab Resolve(string expr, Dictionary<string, string> scope, int depth = 0)
    {
        Assert.True(depth < 24, $"var() cycle resolving '{expr}'.");
        expr = expr.Trim();

        if (expr.StartsWith("var(", StringComparison.Ordinal))
        {
            var inner = Inside(expr, "var(");
            var parts = SplitTopLevel(inner);
            var name = parts[0].Trim();

            if (scope.TryGetValue(name, out var value))
            {
                return Resolve(value, scope, depth + 1);
            }

            // A var() with a fallback is legal and the sheets use none; without one, an undefined token is
            // the silent "no colour at all" failure, so it is an error here rather than a default.
            Assert.True(parts.Count > 1, $"'{name}' is not declared in this theme scope.");
            return Resolve(parts[1], scope, depth + 1);
        }

        if (expr.StartsWith("color-mix(", StringComparison.Ordinal))
        {
            var parts = SplitTopLevel(Inside(expr, "color-mix("));
            Assert.Equal(3, parts.Count);

            var space = parts[0].Trim();
            var (first, weight) = WithWeight(parts[1]);
            var (second, _) = WithWeight(parts[2]);
            var a = Resolve(first, scope, depth + 1);
            var b = Resolve(second, scope, depth + 1);

            return space switch
            {
                "in oklab" => new Lab(
                    (a.L * weight) + (b.L * (1 - weight)),
                    (a.A * weight) + (b.A * (1 - weight)),
                    (a.B * weight) + (b.B * (1 - weight))),

                // srgb mixes gamma-encoded channels, which is also what compositing an alpha fill over a
                // background does — the one place the showcase still has one (Tw.BtnSecondary's hover).
                "in srgb" => FromEncoded(
                    Encoded(a).Zip(Encoded(b), (x, y) => (x * weight) + (y * (1 - weight))).ToArray()),

                _ => throw new InvalidOperationException($"unsupported mix space '{space}'."),
            };
        }

        if (expr.StartsWith("oklch(", StringComparison.Ordinal))
        {
            var m = Oklch().Match(expr);
            Assert.True(m.Success, $"could not read '{expr}'.");

            var l = Number(m.Groups["l"].Value) / (m.Groups["pct"].Success ? 100 : 1);
            var c = m.Groups["c"].Value.Length == 0 ? 0 : Number(m.Groups["c"].Value);
            var h = m.Groups["h"].Value.Length == 0 ? 0 : Number(m.Groups["h"].Value) * Math.PI / 180;

            return new Lab(l, c * Math.Cos(h), c * Math.Sin(h));
        }

        return expr switch
        {
            "black" => FromEncoded([0, 0, 0]),
            "white" => FromEncoded([1, 1, 1]),
            _ => throw new InvalidOperationException($"unsupported colour '{expr}'."),
        };
    }

    private static double Number(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static string Inside(string expr, string prefix) =>
        expr[prefix.Length..expr.LastIndexOf(')')];

    /// <summary>Splits on commas that are not inside parentheses.</summary>
    private static List<string> SplitTopLevel(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(s[start..i]);
                    start = i + 1;
                    break;
            }
        }

        parts.Add(s[start..]);
        return parts;
    }

    /// <summary>Separates a <c>color-mix()</c> argument from its percentage.</summary>
    private static (string Colour, double Weight) WithWeight(string part)
    {
        var m = Weight().Match(part);
        return m.Success
            ? (part[..m.Index].Trim(), Number(m.Groups["pct"].Value) / 100)
            : (part.Trim(), 0.5);
    }

    // ---- colour maths --------------------------------------------------------------------------
    //
    // Oklab -> linear sRGB is the standard matrix pair; the luminance and ratio formulas are WCAG 2.x.
    // Out-of-gamut channels are clamped, which is what a browser displays.

    private static double[] Linear(Lab c)
    {
        var l = Math.Pow(c.L + (0.3963377774 * c.A) + (0.2158037573 * c.B), 3);
        var m = Math.Pow(c.L - (0.1055613458 * c.A) - (0.0638541728 * c.B), 3);
        var s = Math.Pow(c.L - (0.0894841775 * c.A) - (1.2914855480 * c.B), 3);

        return
        [
            (+4.0767416621 * l) - (3.3077115913 * m) + (0.2309699292 * s),
            (-1.2684380046 * l) + (2.6097574011 * m) - (0.3413193965 * s),
            (-0.0041960863 * l) - (0.7034186147 * m) + (1.7076147010 * s),
        ];
    }

    private static Lab FromLinear(double[] rgb)
    {
        var l = Math.Cbrt((0.4122214708 * rgb[0]) + (0.5363325363 * rgb[1]) + (0.0514459929 * rgb[2]));
        var m = Math.Cbrt((0.2119034982 * rgb[0]) + (0.6806995451 * rgb[1]) + (0.1073969566 * rgb[2]));
        var s = Math.Cbrt((0.0883024619 * rgb[0]) + (0.2817188376 * rgb[1]) + (0.6299787005 * rgb[2]));

        return new Lab(
            (0.2104542553 * l) + (0.7936177850 * m) - (0.0040720468 * s),
            (1.9779984951 * l) - (2.4285922050 * m) + (0.4505937099 * s),
            (0.0259040371 * l) + (0.7827717662 * m) - (0.8086757660 * s));
    }

    private static double[] Encoded(Lab c) =>
        [.. Linear(c).Select(v => Encode(Math.Clamp(v, 0, 1)))];

    private static Lab FromEncoded(double[] encoded) =>
        FromLinear([.. encoded.Select(v => Decode(Math.Clamp(v, 0, 1)))]);

    private static double Encode(double v) =>
        v <= 0.0031308 ? 12.92 * v : (1.055 * Math.Pow(v, 1 / 2.4)) - 0.055;

    private static double Decode(double v) =>
        v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Luminance(Lab c)
    {
        var rgb = Linear(c).Select(v => Math.Clamp(v, 0, 1)).ToArray();
        return (0.2126 * rgb[0]) + (0.7152 * rgb[1]) + (0.0722 * rgb[2]);
    }

    private static double Contrast(Lab a, Lab b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    // ---- patterns ------------------------------------------------------------------------------

    [GeneratedRegex(@"(?<selector>[^{}]*)\{(?<body>[^{}]*--color-base-100[^{}]*)\}")]
    private static partial Regex ThemeBlock();

    [GeneratedRegex(@"\[data-theme=""?(?<name>[a-z][a-z0-9-]*)""?\]")]
    private static partial Regex ThemeName();

    [GeneratedRegex(@"\[data-theme=""(?<name>[a-z][a-z0-9-]*)""\]\s*\{(?<body>[^{}]*)\}")]
    private static partial Regex Correction();

    [GeneratedRegex(@"(?<name>--[a-z0-9-]+)\s*:\s*(?<value>[^;{}]+)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CSharpComment();

    [GeneratedRegex(@"(?<![a-z0-9-])text-ui-(?<token>[a-z]+(?:-[a-z]+)*)")]
    private static partial Regex TextUtility();

    [GeneratedRegex(@"\b(oklch|rgba?|hsla?|lab|lch)\s*\(|#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex LiteralColour();

    [GeneratedRegex(@"(?<![a-z-])(black|white)(?![a-z-])")]
    private static partial Regex BlackOrWhite();

    [GeneratedRegex(@"oklch\(\s*(?<l>[0-9.]+)(?<pct>%)?\s+(?<c>[0-9.]*)\s*(?<h>[0-9.]*)")]
    private static partial Regex Oklch();

    [GeneratedRegex(@"\s(?<pct>[0-9.]+)%\s*$")]
    private static partial Regex Weight();
}

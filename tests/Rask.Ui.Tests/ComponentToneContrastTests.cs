using System.Text.RegularExpressions;
using Rask.Ui;

namespace Rask.Ui.Tests;

/// <summary>
///     Every filled component the kit can render labels itself with a pair that has been measured.
/// </summary>
/// <remarks>
///     <para>
///     daisyUI pairs each tone with its own <c>-content</c> colour — <c>.btn-primary</c> sets
///     <c>--btn-fg: var(--color-primary-content)</c>, <c>.badge-success</c> its <c>--badge-fg</c>,
///     <c>.alert-warning</c> a plain <c>color</c>. Those are generated to clear 3:1, which is the bar for
///     a LARGE label, and these components render small text. Measured across all thirty-six palettes the
///     kit ships, they fail WCAG AA on between two and ten palettes per tone: <c>secondary</c> is 3.05:1
///     on daisyUI's own <c>dark</c>, <c>warning</c> 3.06:1 on <c>pastel</c>, <c>error</c> under AA on ten.
///     </para>
///     <para>
///     So the kit corrects them to the <c>-ink</c> fill with the ground as the label. This test does not
///     re-measure that pair — <c>ThemeContrastTests</c> already proves <c>--color-ui-bg</c> against every
///     <c>-ink</c> token on every palette, and contrast is symmetric, so the fill and the label are the
///     same measurement read the other way round. What it proves is that every tone class the kit can
///     WRITE actually reaches that measured pair, which is the half a contrast calculation cannot see: a
///     new <c>.btn-*</c> tone added to <see cref="UiClassNames" /> without an override here would ship
///     daisyUI's 3:1 colours while every number in the other suite stayed green.
///     </para>
/// </remarks>
public sealed partial class ComponentToneContrastTests
{
    /// <summary>The tone each family is corrected to, by the token that carries the measured value.</summary>
    private static readonly Dictionary<string, string> Ink = new(StringComparer.Ordinal)
    {
        ["primary"] = "--color-ui-brand-ink",
        ["secondary"] = "--color-ui-secondary-ink",
        ["accent"] = "--color-ui-accent-ink",
        ["neutral"] = "--color-ui-neutral-ink",
        ["info"] = "--color-ui-info-ink",
        ["success"] = "--color-ui-ok-ink",
        ["warning"] = "--color-ui-warn-ink",
        ["error"] = "--color-ui-danger-ink",
    };

    /// <summary>
    ///     The families that put TEXT on a fill, and the custom property each drives its label with.
    /// </summary>
    /// <remarks>
    ///     The control families — <c>checkbox</c>, <c>radio</c>, <c>toggle</c>, <c>range</c>,
    ///     <c>progress</c> — are deliberately absent: they carry no text, so WCAG asks 3:1 of them as
    ///     non-text UI (1.4.11) rather than 4.5, and recolouring them would be a redesign rather than a
    ///     correction. <c>step</c> is absent for a different and less comfortable reason — see
    ///     <see cref="Step_is_the_one_text_bearing_family_left_uncorrected" />.
    /// </remarks>
    private static readonly (string Family, string Fill, string Label)[] Filled =
    [
        ("btn", "--btn-color", "--btn-fg"),
        ("badge", "--badge-color", "--badge-fg"),
    ];

    [Fact]
    public void Every_filled_tone_the_kit_writes_is_corrected_to_a_measured_pair()
    {
        var css = CorrectionsLayer();
        var written = ToneClassesTheKitWrites(css: null);

        Assert.NotEmpty(written);

        var missing = new List<string>();

        foreach (var (family, fill, label) in Filled)
        {
            foreach (var tone in Ink.Keys)
            {
                if (!written.Contains($"{family}-{tone}"))
                {
                    // The kit cannot render it, so daisyUI never emits it either — nothing to correct.
                    continue;
                }

                var rule = RuleFor(css, $".{family}-{tone}");

                if (rule is null)
                {
                    missing.Add($".{family}-{tone} has no correction at all");
                    continue;
                }

                if (!rule.Contains($"{fill}: var({Ink[tone]})", StringComparison.Ordinal))
                {
                    missing.Add($".{family}-{tone} does not set {fill} to var({Ink[tone]})");
                }

                if (!rule.Contains($"{label}: var(--color-ui-bg)", StringComparison.Ordinal))
                {
                    missing.Add($".{family}-{tone} does not label itself with the ground");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "these filled components still carry daisyUI's own -content label, which is generated for 3:1 "
            + "and fails AA on up to ten of the palettes the kit ships:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Every_link_tone_the_kit_writes_is_corrected_to_the_ink_tier()
    {
        // A link is text on the page ground rather than a fill, so the ink tier IS its colour. daisyUI's
        // own rule paints it the raw tone — the surface-read-as-text mistake — and darkens the hover state
        // toward #000, which on a dark palette pushes the link INTO its background rather than out of it.
        var css = CorrectionsLayer();
        var written = ToneClassesTheKitWrites(css: null);
        var missing = new List<string>();

        foreach (var tone in Ink.Keys)
        {
            if (!written.Contains($"link-{tone}"))
            {
                continue;
            }

            var rule = RuleFor(css, $".link-{tone}");

            if (rule is null || !rule.Contains($"color: var({Ink[tone]})", StringComparison.Ordinal))
            {
                missing.Add($".link-{tone}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "these link tones are still daisyUI's raw surface colour read as text: " + string.Join(", ", missing));
    }

    [Fact]
    public void Step_is_the_one_text_bearing_family_left_uncorrected()
    {
        // Deliberate, and asserted so it cannot be forgotten quietly. daisyUI sets --step-bg/--step-fg on
        // compound pseudo-element selectors (`.step-primary + .step-primary:before`, `.steps .step-primary
        // > .step-icon`), and a custom property inherited from the element loses to a declaration made
        // directly on the pseudo — so unlike every other family here, step cannot be corrected by setting
        // the variable on the class. It needs its own matching selectors.
        //
        // This test states the debt rather than hiding it: the day step IS corrected, it fails, and whoever
        // did the work moves it into Filled above where it belongs.
        var css = File.ReadAllText(KitStylesheet());

        Assert.DoesNotContain("--step-bg: var(--color-ui-", css, StringComparison.Ordinal);
    }

    /// <summary>The body of the LAST rule matching a selector in the kit's own corrections.</summary>
    private static string? RuleFor(string css, string selector)
    {
        // Anchored to the corrections layer: daisyUI's own rule for the same selector lives in the vendored
        // bundle's output, not here, but the source file is what this reads and a loose search over it
        // would happily match a mention inside a comment.
        var matches = Regex.Matches(
            css,
            Regex.Escape(selector) + @"\s*(?:,[^{}]*)?\{(?<body>[^{}]*)\}",
            RegexOptions.Singleline);

        return matches.Count == 0 ? null : matches[^1].Groups["body"].Value;
    }

    /// <summary>Every tone class name <c>UiClassNames</c> can produce.</summary>
    private static HashSet<string> ToneClassesTheKitWrites(string? css)
    {
        _ = css;

        var source = File.ReadAllText(
            Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "UiClassNames.cs"));

        return Regex
            .Matches(source, @"""(?<name>[a-z]+-(?:primary|secondary|accent|neutral|info|success|warning|error))""")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    ///     The body of the kit's <c>@layer rask-ui-corrections</c> block.
    /// </summary>
    /// <remarks>
    ///     THE LAYER IS PART OF THE CONTRACT, which is why these tests read this block rather than the whole
    ///     file. A correction with the right value in the wrong layer is the exact defect that shipped here:
    ///     daisyUI emits its component rules inside <c>@layer utilities</c>, and the kit's order statement
    ///     puts <c>rask</c> BEFORE <c>utilities</c> so that an app keeps its own cascade — so the overrides,
    ///     written in <c>rask</c>, computed the right colour and lost. A browser measured
    ///     <c>.btn-primary</c> at 3.29:1 on <c>corporate</c> while the compiled sheet contained the fix and
    ///     every unit test was green. Reading the whole file would pass on precisely that bug.
    /// </remarks>
    private static string CorrectionsLayer()
    {
        var css = File.ReadAllText(KitStylesheet());
        var open = css.IndexOf("@layer rask-ui-corrections", StringComparison.Ordinal);

        Assert.True(open >= 0, "the kit declares no @layer rask-ui-corrections block.");

        var brace = css.IndexOf('{', open);
        var depth = 0;

        for (var i = brace; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return css[(brace + 1)..i];
                }
            }
        }

        throw new InvalidOperationException("the corrections layer is never closed.");
    }

    private static string KitStylesheet() =>
        Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Styles", "ui.css");
}

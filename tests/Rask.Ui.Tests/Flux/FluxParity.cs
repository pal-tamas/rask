using Rask.Core;

namespace Rask.UiTests.Flux;

/// <summary>
///     One component's examples, in the order and under the section ids Flux UI's docs page gives them.
/// </summary>
/// <remarks>
///     <para>
///     Rask.Ui has to look and behave exactly as Flux does, and "exactly" is measured: <c>scripts/flux/parity.mjs</c>
///     opens Flux's live page and the page <see cref="FluxParityPages" /> writes from these examples, and
///     compares what the browser computed for each — boxes, colours, borders, shadows, and what hover, press
///     and keyboard focus change — in light and in dark.
///     </para>
///     <para>
///     So an example here is a TRANSLATION of the one on Flux's page, not a new one: the same components
///     in the same order with the same words, because the words set the widths. Its section is the id of
///     the <c>&lt;h2&gt;</c> above it there (<c>variants</c>, <c>sizes</c>, <c>button-groups</c>).
///     </para>
///     <para>
///     The kit's sheet is compiled from the kit's own sources, so a utility class written HERE is in no
///     stylesheet. Arrange an example with <see cref="Row" />, <see cref="Stack" /> or an inline style.
///     </para>
/// </remarks>
public abstract partial class FluxParity : global::Rask.Core.RaskMarkup
{
    /// <summary>The page's slug on fluxui.dev: <c>button</c> for <c>/components/button</c>.</summary>
    public abstract string Page { get; }

    public abstract IEnumerable<(string Section, Component Example)> Examples();

    /// <summary>
    ///     The docs site's monospace face, which a typed date's and time's fields are set in (<c>font-mono</c>) and
    ///     sized by (two of its <c>ch</c>): CSS, for an example's <c>&lt;style&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     MonoLisa is a licensed webfont this page cannot load, so its name is given a free one (OFL) of the same
    ///     shape from the host Inter comes from, scaled to its advance: 0.6395em (19.906px for two digits and a
    ///     pixel a side at 14px, measured on Flux's page) over JetBrains Mono's 0.6em. A webfont and not a local
    ///     face, as one machine has Menlo, a runner DejaVu Sans Mono and Playwright's image neither: a local stand-in
    ///     was a different width on each, and the fields with it.
    /// </remarks>
    protected const string DocsMonospace =
        "@font-face{font-family:MonoLisa;font-weight:400;"
        + "src:url(https://fonts.bunny.net/jetbrains-mono/files/jetbrains-mono-latin-400-normal.woff2)format(\"woff2\");"
        + "size-adjust:106.585%}:root{--font-mono:MonoLisa,monospace}";

    /// <summary>Side by side, as most of Flux's examples are laid out.</summary>
    protected static Component Row(params Component[] items) =>
        Div.Style("display:flex;gap:16px;align-items:flex-end;justify-content:center")[items];

    /// <summary>
    ///     The column Flux's docs page centres most examples in: 384px at most, and on a phone as wide as what it holds
    ///     needs, past the screen's edge if it must. An example that fills it says so itself (<c>width:100%</c>).
    /// </summary>
    protected static Component DocsColumn(params Component[] items) => Div.Data("docs-column", "")[items];

    /// <summary>
    ///     An example in <see cref="DocsColumn(Component[])" /> at its own width, but never narrower than
    ///     <paramref name="smMinWidth" /> from <c>sm</c> up (Tailwind's <c>sm:min-w-*</c> on Flux's page).
    /// </summary>
    protected static Component DocsColumn(int smMinWidth, params Component[] items) =>
        DocsColumn(Div.Data("sm-min-width", "").Style(FormattableString.Invariant($"--sm-min-width:{smMinWidth}px"))[items]);

    /// <summary>
    ///     The 384px column less 48px a side from <c>lg</c> up, as Flux's page holds some examples (288px on a
    ///     desktop); a phone gives them the whole column.
    /// </summary>
    protected static Component DocsInset(params Component[] items) =>
        Div.Data("lg-inset", "").Style("max-width:384px;margin:0 auto")[items];

    /// <summary>One above the other.</summary>
    protected static Component Stack(params Component[] items) =>
        Div.Style("display:flex;flex-direction:column;gap:16px;align-items:stretch")[items];

    /// <summary>
    ///     Tailwind's <c>space-y-{units}</c>, which Flux's examples stack with: a margin under every item but
    ///     the last, so each item measures the margin Flux's does. 2, 3, 4, 6 and 8 are in the page's sheet.
    /// </summary>
    protected static Component SpaceY(int units, params Component[] items) =>
        Div.Data("space-y", units.ToString(System.Globalization.CultureInfo.InvariantCulture))[items];
}

namespace Rask.Site;

/// <summary>
/// The showcase's styling vocabulary, as Tailwind utilities over the shared kit's palette.
/// </summary>
/// <remarks>
/// <para>
/// Class constants rather than a component per control. The showcase's subject is the FRAMEWORK — the
/// chain, the live diff, the lifecycle — so a wrapper component per button would put a layer of the
/// showcase's own invention between the reader and the thing being shown. A constant is a string: what
/// a demo renders is still plain <c>Button</c> and <c>Div</c>, which is what the surrounding prose is
/// talking about.
/// </para>
/// <para>
/// Every colour below is a <c>--color-ui-*</c> token declared by <c>Rask.Ui</c> — the same palette the
/// operator console and the landing site are drawn from. It used to be raw Tailwind hues
/// (<c>violet-600</c>, <c>slate-200</c>, <c>emerald-600</c>) with a <c>dark:</c> twin on almost every
/// one. That is why this file is worth reading before changing a demo's markup: re-pointing these
/// constants moved 153 call sites onto the shared palette without touching a single page.
/// </para>
/// <para>
/// The <c>dark:</c> variants are gone with them, and they are not coming back: the page follows the
/// reader's theme now (see the pre-paint script in <c>App.cs</c>), and a <c>dark:</c> twin selects on
/// <c>prefers-color-scheme</c> rather than on the palette actually showing — so it would be wrong in
/// both directions the moment a reader picked `dracula` on a light machine. Every colour here resolves
/// through the theme instead.
/// </para>
/// <para>
/// A FILLED CONTROL IS <c>bg-ui-*-ink text-ui-bg</c>, never a saturated fill with a white label. This is
/// the file where <c>bg-ui-brand text-white</c> lived, and it measured 1.00:1 on `luxury` and 1.07:1 on
/// `black` — a button whose label was not there. The <c>-ink</c> tier is held to 4.5:1 against the
/// ground, and contrast is symmetric, so the ground read ON that fill is the same proven measurement.
/// daisyUI's own <c>-content</c> colours were the other candidate and are generated for 3:1, not 4.5.
/// </para>
/// <para>
/// One variant has no token of its own: the <c>Light</c>/<c>Dark</c> pair, which is the ground and the
/// ink rather than a hue. <c>Info</c> used to be in that sentence too, drawn in raw <c>sky-*</c> — the
/// one family here that ignored the theme completely, and <c>text-sky-700</c> on a dark palette was
/// 2.1:1. It is <c>--color-ui-info</c> now, daisyUI's own, and <c>ThemeContrastTests</c> measures it
/// with the rest.
/// </para>
/// </remarks>
public static class Tw
{
    /// <summary>The shared shape of every button: the size, radius and focus behaviour.</summary>
    private const string BtnBase =
        "inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline "
        + "transition disabled:cursor-default disabled:opacity-50";

    public const string BtnPrimary = BtnBase + " bg-ui-brand-ink text-ui-bg hover:bg-ui-ink";

    public const string BtnSecondary = BtnBase + " bg-ui-well text-ui-ink hover:bg-ui-line/40";

    public const string BtnSuccess = BtnBase + " bg-ui-ok-ink text-ui-bg hover:bg-ui-ink";

    public const string BtnDanger = BtnBase + " bg-ui-danger-ink text-ui-bg hover:bg-ui-ink";

    public const string BtnWarning = BtnBase + " bg-ui-warn-ink text-ui-bg hover:bg-ui-ink";

    public const string BtnInfo = BtnBase + " bg-ui-info-ink text-ui-bg hover:bg-ui-ink";

    public const string BtnLight = BtnBase + " bg-ui-bg text-ui-ink ring-1 ring-ui-line hover:bg-ui-well";

    // hover:bg-ui-muted rather than bg-ui-ink/90: an alpha fill composites with whatever is behind the
    // button, so the hovered contrast depended on the page instead of the token. The muted tier is
    // held to 4.5:1 against the ground, and contrast is symmetric, so the ground reads on it.
    public const string BtnDark = BtnBase + " bg-ui-ink text-ui-bg hover:bg-ui-muted";

    /// <summary>An outline button — the same shape, drawn as a border rather than a fill.</summary>
    private const string OutlineBase = BtnBase + " bg-transparent ring-1";

    public const string BtnOutlinePrimary =
        OutlineBase + " text-ui-brand-ink ring-ui-brand/40 hover:bg-ui-brand-surface";

    public const string BtnOutlineSecondary = OutlineBase + " text-ui-ink ring-ui-line hover:bg-ui-well";

    public const string BtnOutlineSuccess = OutlineBase + " text-ui-ok-ink ring-ui-ok/40 hover:bg-ui-ok-surface";

    public const string BtnOutlineDanger =
        OutlineBase + " text-ui-danger-ink ring-ui-danger/40 hover:bg-ui-danger-surface";

    public const string BtnOutlineWarning =
        OutlineBase + " text-ui-warn-ink ring-ui-warn/40 hover:bg-ui-warn-surface";

    public const string BtnOutlineInfo = OutlineBase + " text-ui-info-ink ring-ui-info/40 hover:bg-ui-info-surface";

    public const string BtnOutlineLight = OutlineBase + " text-ui-ink ring-ui-line hover:bg-ui-well";

    public const string BtnOutlineDark = OutlineBase + " text-ui-ink ring-ui-muted hover:bg-ui-well";

    /// <summary>A panel.</summary>
    public const string Card = "rounded-xl bg-ui-bg ring-1 ring-ui-line";

    /// <summary>A panel's padded interior.</summary>
    public const string CardBody = "p-5";

    /// <summary>A panel's heading strip.</summary>
    public const string CardHeader = "border-b border-ui-line px-5 py-3 font-medium";

    private const string AlertBase = "rounded-lg px-4 py-3 text-sm";

    public const string AlertPrimary = AlertBase + " bg-ui-brand-surface text-ui-brand-ink";

    public const string AlertSecondary = AlertBase + " bg-ui-well text-ui-ink";

    public const string AlertSuccess = AlertBase + " bg-ui-ok-surface text-ui-ok-ink";

    public const string AlertDanger = AlertBase + " bg-ui-danger-surface text-ui-danger-ink";

    public const string AlertWarning = AlertBase + " bg-ui-warn-surface text-ui-warn-ink";

    public const string AlertInfo = AlertBase + " bg-ui-info-surface text-ui-info-ink";

    public const string AlertLight = AlertBase + " bg-ui-bg text-ui-ink ring-1 ring-ui-line";

    public const string AlertDark = AlertBase + " bg-ui-ink text-ui-bg";

    private const string BadgeBase = "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium";

    public const string BadgePrimary = BadgeBase + " bg-ui-brand-surface text-ui-brand-ink";

    public const string BadgeSecondary = BadgeBase + " bg-ui-well text-ui-muted";

    public const string BadgeSuccess = BadgeBase + " bg-ui-ok-surface text-ui-ok-ink";

    public const string BadgeDanger = BadgeBase + " bg-ui-danger-surface text-ui-danger-ink";

    public const string BadgeWarning = BadgeBase + " bg-ui-warn-surface text-ui-warn-ink";

    public const string BadgeInfo = BadgeBase + " bg-ui-info-surface text-ui-info-ink";

    public const string BadgeLight = BadgeBase + " bg-ui-bg text-ui-ink ring-1 ring-ui-line";

    public const string BadgeDark = BadgeBase + " bg-ui-ink text-ui-bg";

    /// <summary>A text input, and the controls that share its shape.</summary>
    /// <remarks>
    /// <c>min-h-11</c> below <c>sm</c>, like every control in the kit: 44px is the smallest reliable
    /// touch target, and these are <c>text-sm</c>.
    /// </remarks>
    public const string Input =
        "min-h-11 w-full rounded-md border border-ui-line bg-ui-bg px-3 text-sm text-ui-ink "
        + "placeholder:text-ui-muted focus:border-ui-brand focus:outline-none sm:min-h-0 sm:py-1.5";

    /// <summary>A button drawn as a link — no fill, no ring.</summary>
    public const string BtnLink =
        "inline-flex items-center gap-1.5 p-0 text-sm font-medium text-ui-brand-ink underline-offset-2 "
        + "hover:underline";

    /// <summary>A panel's footer strip.</summary>
    public const string CardFooter = "border-t border-ui-line px-5 py-3 text-sm";

    /// <summary>A panel's title.</summary>
    public const string CardTitle = "mb-1 text-lg font-semibold text-ui-ink";

    /// <summary>A panel's secondary title.</summary>
    public const string CardSubtitle = "mb-2 text-sm text-ui-muted";

    /// <summary>A select, shaped like <see cref="Input" />.</summary>
    public const string Select = Input;

    /// <summary>A form field's label.</summary>
    public const string Label = "mb-1 block text-sm font-medium text-ui-ink";

    /// <summary>The hint under a field.</summary>
    public const string FormText = "mt-1 text-xs text-ui-muted";

    /// <summary>A checkbox or radio.</summary>
    // text-ui-brand-ink, not text-ui-brand: on a checkbox this colour is the CHECKED FILL, and the
    // tick a browser draws on it is white. The raw brand surface is 1.37:1 against white on pastel,
    // so the tick disappeared while the box still looked checked. The -ink tier is the same hue held
    // to 4.5:1 against the ground, which is the measurement that makes the tick visible.
    public const string CheckInput = "size-4 rounded border-ui-line text-ui-brand-ink";

    /// <summary>The label beside a checkbox or radio.</summary>
    public const string CheckLabel = "text-sm text-ui-ink";

    /// <summary>An input with something butted against it.</summary>
    public const string InputGroup = "flex items-stretch gap-2";

    /// <summary>A bordered list.</summary>
    public const string ListGroup =
        "divide-y divide-ui-line overflow-hidden rounded-lg ring-1 ring-ui-line";

    /// <summary>One row of a bordered list.</summary>
    public const string ListGroupItem = "flex items-center gap-2 bg-ui-bg px-4 py-2 text-ui-ink";

    /// <summary>A data table.</summary>
    public const string Table = "w-full text-left text-sm [&_td]:px-3 [&_td]:py-2 [&_th]:px-3 [&_th]:py-2";

    /// <summary>A busy indicator.</summary>
    public const string Spinner =
        "inline-block size-5 animate-spin rounded-full border-2 border-current border-r-transparent";

    /// <summary>A progress track.</summary>
    public const string Progress = "h-2 w-full overflow-hidden rounded-full bg-ui-line";

    /// <summary>The filled part of a progress track.</summary>
    public const string ProgressBar = "h-full bg-ui-brand transition-all";

    /// <summary>A tab-shaped navigation link.</summary>
    public const string NavLink = "rounded-md px-3 py-1.5 text-sm no-underline hover:bg-ui-well";

    /// <summary>A row of tabs.</summary>
    public const string NavTabs = "flex flex-wrap items-center gap-1 border-b border-ui-line";

    /// <summary>A pulled quote.</summary>
    public const string Blockquote = "border-l-4 border-ui-line pl-4 italic text-ui-muted";

    /// <summary>A caption under a figure.</summary>
    public const string FigureCaption = "mt-2 text-sm text-ui-muted";

    /// <summary>A field whose label floats over the control.</summary>
    public const string FormFloating = "relative";
}

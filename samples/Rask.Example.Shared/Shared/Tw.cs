namespace Rask.Example.Shared;

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
/// The <c>dark:</c> variants are gone with them. The showcase is light — see the pre-paint script in
/// <c>App.cs</c> — so a second set of colours had nothing left to select.
/// </para>
/// <para>
/// Two variants have no token behind them. <c>Info</c> and the <c>Light</c>/<c>Dark</c> pair exist
/// because this showcase demonstrates a full range of control weights side by side, and the kit's
/// palette is deliberately smaller than that: it names the states an operator surface needs (ok, warn,
/// danger) rather than a decorative spectrum. Collapsing them onto <c>brand</c> would have printed the
/// same button twice in a demo whose point is that they differ, so they keep one hue of their own.
/// </para>
/// </remarks>
public static class Tw
{
    /// <summary>The shared shape of every button: the size, radius and focus behaviour.</summary>
    private const string BtnBase =
        "inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline "
        + "transition disabled:cursor-default disabled:opacity-50";

    public const string BtnPrimary = BtnBase + " bg-ui-brand text-white hover:bg-ui-brand/90";

    public const string BtnSecondary = BtnBase + " bg-ui-well text-ui-ink hover:bg-ui-line/40";

    public const string BtnSuccess = BtnBase + " bg-ui-ok text-white hover:bg-ui-ok/90";

    public const string BtnDanger = BtnBase + " bg-ui-danger text-white hover:bg-ui-danger/90";

    public const string BtnWarning = BtnBase + " bg-ui-warn text-ui-ink hover:bg-ui-warn/90";

    public const string BtnInfo = BtnBase + " bg-sky-600 text-white hover:bg-sky-500";

    public const string BtnLight = BtnBase + " bg-ui-bg text-ui-ink ring-1 ring-ui-line hover:bg-ui-well";

    public const string BtnDark = BtnBase + " bg-ui-ink text-ui-bg hover:bg-ui-ink/90";

    /// <summary>An outline button — the same shape, drawn as a border rather than a fill.</summary>
    private const string OutlineBase = BtnBase + " bg-transparent ring-1";

    public const string BtnOutlinePrimary =
        OutlineBase + " text-ui-brand-ink ring-ui-brand/40 hover:bg-ui-brand/5";

    public const string BtnOutlineSecondary = OutlineBase + " text-ui-ink ring-ui-line hover:bg-ui-well";

    public const string BtnOutlineSuccess = OutlineBase + " text-ui-ok-ink ring-ui-ok/40 hover:bg-ui-ok/5";

    public const string BtnOutlineDanger =
        OutlineBase + " text-ui-danger ring-ui-danger/40 hover:bg-ui-danger/5";

    public const string BtnOutlineWarning =
        OutlineBase + " text-ui-warn-ink ring-ui-warn/40 hover:bg-ui-warn/10";

    public const string BtnOutlineInfo = OutlineBase + " text-sky-700 ring-sky-300 hover:bg-sky-50";

    public const string BtnOutlineLight = OutlineBase + " text-ui-ink ring-ui-line hover:bg-ui-well";

    public const string BtnOutlineDark = OutlineBase + " text-ui-ink ring-ui-muted hover:bg-ui-well";

    /// <summary>A panel.</summary>
    public const string Card = "rounded-xl bg-ui-bg ring-1 ring-ui-line";

    /// <summary>A panel's padded interior.</summary>
    public const string CardBody = "p-5";

    /// <summary>A panel's heading strip.</summary>
    public const string CardHeader = "border-b border-ui-line px-5 py-3 font-medium";

    private const string AlertBase = "rounded-lg px-4 py-3 text-sm";

    public const string AlertPrimary = AlertBase + " bg-ui-brand/10 text-ui-brand-ink";

    public const string AlertSecondary = AlertBase + " bg-ui-well text-ui-ink";

    public const string AlertSuccess = AlertBase + " bg-ui-ok/10 text-ui-ok-ink";

    public const string AlertDanger = AlertBase + " bg-ui-danger/10 text-ui-danger";

    public const string AlertWarning = AlertBase + " bg-ui-warn/15 text-ui-warn-ink";

    public const string AlertInfo = AlertBase + " bg-sky-50 text-sky-900";

    public const string AlertLight = AlertBase + " bg-ui-bg text-ui-ink ring-1 ring-ui-line";

    public const string AlertDark = AlertBase + " bg-ui-ink text-ui-bg";

    private const string BadgeBase = "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium";

    public const string BadgePrimary = BadgeBase + " bg-ui-brand/10 text-ui-brand-ink";

    public const string BadgeSecondary = BadgeBase + " bg-ui-well text-ui-muted";

    public const string BadgeSuccess = BadgeBase + " bg-ui-ok/10 text-ui-ok-ink";

    public const string BadgeDanger = BadgeBase + " bg-ui-danger/10 text-ui-danger";

    public const string BadgeWarning = BadgeBase + " bg-ui-warn/15 text-ui-warn-ink";

    public const string BadgeInfo = BadgeBase + " bg-sky-100 text-sky-800";

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
    public const string CheckInput = "size-4 rounded border-ui-line text-ui-brand";

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

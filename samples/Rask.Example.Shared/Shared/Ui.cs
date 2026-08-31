namespace Rask.Example.Shared;

/// <summary>
/// The showcase's styling vocabulary, as Tailwind utilities.
/// </summary>
/// <remarks>
/// Class constants rather than a component per control. The showcase's subject is the FRAMEWORK — the
/// chain, the live diff, the lifecycle — so a wrapper component per button would put a layer of the
/// showcase's own invention between the reader and the thing being shown. A constant is a string: what
/// a demo renders is still plain <c>Button</c> and <c>Div</c>, which is what the surrounding prose is
/// talking about.
/// <para>
/// This replaced <c>Rask.Bootstrap</c>'s typed <c>BsColor</c>/<c>BsSize</c> enums. Only
/// <c>BsSize.Sm</c> was ever used across the whole showcase, so size is not a dimension here.
/// </para>
/// </remarks>
public static class Ui
{
    /// <summary>The shared shape of every button: the size, radius and focus behaviour.</summary>
    private const string BtnBase =
        "inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline "
        + "transition disabled:cursor-default disabled:opacity-50";

    public const string BtnPrimary = BtnBase + " bg-violet-600 text-white hover:bg-violet-500";

    public const string BtnSecondary =
        BtnBase + " bg-slate-200 text-slate-800 hover:bg-slate-300 dark:bg-slate-700 dark:text-slate-100 "
        + "dark:hover:bg-slate-600";

    public const string BtnSuccess = BtnBase + " bg-emerald-600 text-white hover:bg-emerald-500";

    public const string BtnDanger = BtnBase + " bg-red-600 text-white hover:bg-red-500";

    public const string BtnWarning = BtnBase + " bg-amber-500 text-white hover:bg-amber-400";

    public const string BtnInfo = BtnBase + " bg-sky-600 text-white hover:bg-sky-500";

    public const string BtnLight = BtnBase + " bg-white text-slate-800 ring-1 ring-slate-200 hover:bg-slate-50";

    public const string BtnDark = BtnBase + " bg-slate-900 text-white hover:bg-slate-800";

    /// <summary>An outline button — the same shape, drawn as a border rather than a fill.</summary>
    private const string OutlineBase = BtnBase + " bg-transparent ring-1";

    public const string BtnOutlinePrimary =
        OutlineBase + " text-violet-700 ring-violet-300 hover:bg-violet-50 dark:text-violet-300 "
        + "dark:ring-violet-700 dark:hover:bg-violet-950";

    public const string BtnOutlineSecondary =
        OutlineBase + " text-slate-700 ring-slate-300 hover:bg-slate-50 dark:text-slate-300 "
        + "dark:ring-slate-600 dark:hover:bg-slate-800";

    public const string BtnOutlineSuccess =
        OutlineBase + " text-emerald-700 ring-emerald-300 hover:bg-emerald-50 dark:text-emerald-300";

    public const string BtnOutlineDanger =
        OutlineBase + " text-red-700 ring-red-300 hover:bg-red-50 dark:text-red-300";

    public const string BtnOutlineWarning =
        OutlineBase + " text-amber-700 ring-amber-300 hover:bg-amber-50 dark:text-amber-300";

    public const string BtnOutlineInfo =
        OutlineBase + " text-sky-700 ring-sky-300 hover:bg-sky-50 dark:text-sky-300";

    public const string BtnOutlineLight = OutlineBase + " text-slate-700 ring-slate-200 hover:bg-slate-50";

    public const string BtnOutlineDark = OutlineBase + " text-slate-900 ring-slate-400 hover:bg-slate-100";

    /// <summary>A panel.</summary>
    public const string Card =
        "rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700";

    /// <summary>A panel's padded interior.</summary>
    public const string CardBody = "p-5";

    /// <summary>A panel's heading strip.</summary>
    public const string CardHeader =
        "border-b border-slate-200 px-5 py-3 font-medium dark:border-slate-700";

    private const string AlertBase = "rounded-lg px-4 py-3 text-sm";

    public const string AlertPrimary = AlertBase + " bg-violet-50 text-violet-900 dark:bg-violet-950 dark:text-violet-200";

    public const string AlertSecondary = AlertBase + " bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200";

    public const string AlertSuccess = AlertBase + " bg-emerald-50 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200";

    public const string AlertDanger = AlertBase + " bg-red-50 text-red-900 dark:bg-red-950 dark:text-red-200";

    public const string AlertWarning = AlertBase + " bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200";

    public const string AlertInfo = AlertBase + " bg-sky-50 text-sky-900 dark:bg-sky-950 dark:text-sky-200";

    public const string AlertLight = AlertBase + " bg-white text-slate-800 ring-1 ring-slate-200";

    public const string AlertDark = AlertBase + " bg-slate-900 text-white";

    private const string BadgeBase = "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium";

    public const string BadgePrimary = BadgeBase + " bg-violet-100 text-violet-800 dark:bg-violet-900 dark:text-violet-200";

    public const string BadgeSecondary = BadgeBase + " bg-slate-100 text-slate-700 dark:bg-slate-700 dark:text-slate-200";

    public const string BadgeSuccess = BadgeBase + " bg-emerald-100 text-emerald-800 dark:bg-emerald-900 dark:text-emerald-200";

    public const string BadgeDanger = BadgeBase + " bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-200";

    public const string BadgeWarning = BadgeBase + " bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200";

    public const string BadgeInfo = BadgeBase + " bg-sky-100 text-sky-800 dark:bg-sky-900 dark:text-sky-200";

    public const string BadgeLight = BadgeBase + " bg-white text-slate-700 ring-1 ring-slate-200";

    public const string BadgeDark = BadgeBase + " bg-slate-900 text-white";

    /// <summary>A text input, and the controls that share its shape.</summary>
    public const string Input =
        "w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 "
        + "placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 "
        + "dark:bg-slate-900 dark:text-slate-100";

    /// <summary>A button drawn as a link — no fill, no ring.</summary>
    public const string BtnLink =
        "inline-flex items-center gap-1.5 p-0 text-sm font-medium text-violet-700 underline-offset-2 "
        + "hover:underline dark:text-violet-300";

    /// <summary>A panel's footer strip.</summary>
    public const string CardFooter =
        "border-t border-slate-200 px-5 py-3 text-sm dark:border-slate-700";

    /// <summary>A panel's title.</summary>
    public const string CardTitle = "mb-1 text-lg font-semibold";

    /// <summary>A panel's secondary title.</summary>
    public const string CardSubtitle = "mb-2 text-sm text-slate-500 dark:text-slate-400";

    /// <summary>A select, shaped like <see cref="Input" />.</summary>
    public const string Select = Input;

    /// <summary>A form field's label.</summary>
    public const string Label = "mb-1 block text-sm font-medium";

    /// <summary>The hint under a field.</summary>
    public const string FormText = "mt-1 text-xs text-slate-500 dark:text-slate-400";

    /// <summary>A checkbox or radio.</summary>
    public const string CheckInput = "size-4 rounded border-slate-300 text-violet-600";

    /// <summary>The label beside a checkbox or radio.</summary>
    public const string CheckLabel = "text-sm";

    /// <summary>An input with something butted against it.</summary>
    public const string InputGroup = "flex items-stretch gap-2";

    /// <summary>A bordered list.</summary>
    public const string ListGroup =
        "divide-y divide-slate-200 overflow-hidden rounded-lg ring-1 ring-slate-200 "
        + "dark:divide-slate-700 dark:ring-slate-700";

    /// <summary>One row of a bordered list.</summary>
    public const string ListGroupItem = "flex items-center gap-2 bg-white px-4 py-2 dark:bg-slate-800";

    /// <summary>A data table.</summary>
    public const string Table = "w-full text-left text-sm [&_td]:px-3 [&_td]:py-2 [&_th]:px-3 [&_th]:py-2";

    /// <summary>A busy indicator.</summary>
    public const string Spinner =
        "inline-block size-5 animate-spin rounded-full border-2 border-current border-r-transparent";

    /// <summary>A progress track.</summary>
    public const string Progress = "h-2 w-full overflow-hidden rounded-full bg-slate-200 dark:bg-slate-700";

    /// <summary>The filled part of a progress track.</summary>
    public const string ProgressBar = "h-full bg-violet-600 transition-all";

    /// <summary>A tab-shaped navigation link.</summary>
    public const string NavLink =
        "rounded-md px-3 py-1.5 text-sm no-underline hover:bg-slate-100 dark:hover:bg-slate-800";

    /// <summary>A row of tabs.</summary>
    public const string NavTabs = "flex flex-wrap items-center gap-1 border-b border-slate-200 dark:border-slate-700";

    /// <summary>A pulled quote.</summary>
    public const string Blockquote =
        "border-l-4 border-slate-300 pl-4 italic text-slate-700 dark:border-slate-600 dark:text-slate-300";

    /// <summary>A caption under a figure.</summary>
    public const string FigureCaption = "mt-2 text-sm text-slate-500 dark:text-slate-400";

    /// <summary>A field whose label floats over the control.</summary>
    public const string FormFloating = "relative";
}

using System.Collections.ObjectModel;

namespace Rask;

/// <summary>
///     The classes Flux's checkbox and radio are drawn with, each written from fluxui.dev's measurements.
/// </summary>
/// <remarks>
///     <para>
///     Flux's <c>ui-checkbox</c> and <c>ui-radio</c> are custom elements its script makes behave like inputs.
///     Here each is a <c>&lt;label&gt;</c> (the <c>group/option</c>) around a real <c>&lt;input&gt;</c> that is
///     out of sight, so ticking, the space bar, a radio group's arrow keys and the form post are the
///     browser's. Everything drawn reads the input's own state: <c>has-checked</c> on the label,
///     <c>group-has-checked/option</c> inside it.
///     </para>
///     <para>
///     Where two states meet (checked and hovered, checked and disabled) the rule for the pair is written
///     out, with both variants: two rules of equal weight would leave the winner to the order Tailwind
///     happens to print them in.
///     </para>
/// </remarks>
internal static class UiOptionLook
{
    /// <summary>The real input: out of sight, still in the tab order and the accessibility tree.</summary>
    internal const string Native = "sr-only";

    /// <summary>
    ///     The keyboard's focus ring, which Flux leaves to the browser, drawn where the input is seen — and the
    ///     pointer Flux's element has, where a label's own would be the arrow.
    /// </summary>
    internal const string Focus = "cursor-auto has-[:focus-visible]:[outline:auto_1px_-webkit-focus-ring-color]";

    /// <summary>A checkbox drawn as its box alone.</summary>
    internal const string Checkbox = "group/option flex size-[1.125rem] mt-px rounded-[.3rem] outline-offset-2 " + Focus;

    /// <summary>A radio drawn as its dot alone.</summary>
    internal const string Radio = "group/option flex size-[1.125rem] mt-px rounded-full outline-offset-2 " + Focus;

    /// <summary>The box a tick is drawn in.</summary>
    internal const string CheckboxIndicator = Box + " rounded-[.3rem]";

    /// <summary>The circle a radio's dot is drawn in.</summary>
    internal const string RadioIndicator = Box + " rounded-full";

    /// <summary>An indicator a card places itself, a pixel down to sit on the heading's line.</summary>
    internal const string InCard = "mt-px";

    /// <summary>The tick: shown while checked, unless the box is indeterminate.</summary>
    internal const string Tick =
        "hidden text-fx-accent-foreground group-has-checked/option:block "
        + "group-data-indeterminate/option:group-has-checked/option:hidden";

    /// <summary>The dash of an indeterminate checkbox.</summary>
    internal const string Dash = "hidden text-fx-accent-foreground group-data-indeterminate/option:block";

    /// <summary>The dot of a chosen radio.</summary>
    internal const string Dot = "hidden size-2 rounded-full bg-fx-accent-foreground group-has-checked/option:block";

    /// <summary>A card: bordered, the accent border and a faint tint when chosen.</summary>
    internal const string Card =
        "group/option relative flex flex-1 justify-between gap-3 rounded-lg border p-4 shadow-xs "
        + "border-zinc-800/15 bg-white hover:border-zinc-800/20 "
        + "dark:border-white/10 dark:bg-white/10 dark:hover:border-white/10 dark:hover:bg-white/15 "
        + "has-checked:border-fx-accent has-checked:hover:border-fx-accent dark:has-checked:bg-white/15 "
        + "after:absolute after:-inset-px after:rounded-lg has-checked:after:bg-zinc-800/[2.5%] dark:has-checked:after:bg-white/10 "
        + "has-disabled:opacity-50 dark:has-disabled:opacity-75 "
        + Focus;

    /// <summary>The icon and the words of a card, beside its indicator.</summary>
    internal const string CardBody = "flex flex-1 gap-2";

    /// <summary>A card's icon, two pixels down to sit on the heading's line.</summary>
    internal const string CardIcon =
        "mt-0.5 text-zinc-400 group-has-checked/option:text-zinc-800 dark:group-has-checked/option:text-white";

    /// <summary>A card's label.</summary>
    internal const string CardHeading = "mb-2 text-sm font-medium text-zinc-800 dark:text-white";

    /// <summary>A card's description.</summary>
    internal const string CardSubheading = "text-xs text-zinc-500 dark:text-white/70";

    /// <summary>A pill: a faint tint, filled with the accent when chosen.</summary>
    internal const string Pill =
        "group/option flex items-center gap-2 rounded-full px-2 py-1 text-sm leading-4 font-medium whitespace-nowrap "
        + "bg-zinc-800/6 text-zinc-800 hover:bg-zinc-800/10 "
        + "dark:bg-white/10 dark:text-white/70 dark:hover:bg-white/15 dark:hover:text-white "
        + "has-checked:bg-fx-accent has-checked:text-fx-accent-foreground "
        + "has-checked:hover:bg-fx-accent has-checked:hover:text-fx-accent-foreground "
        + "has-disabled:opacity-50 dark:has-disabled:opacity-75 "
        + Focus;

    /// <summary>A button: Flux's outline button, with the accent border when chosen.</summary>
    internal const string Button =
        "group/option relative flex h-10 items-center justify-center gap-2 rounded-lg border ps-3 pe-4 "
        + "text-sm font-medium whitespace-nowrap shadow-xs "
        + "border-zinc-200 border-b-zinc-300/80 bg-white text-zinc-800 hover:border-zinc-800/20 "
        + "dark:border-zinc-600 dark:bg-zinc-700 dark:text-white dark:hover:border-zinc-500 "
        + "has-checked:border-fx-accent has-checked:hover:border-fx-accent dark:has-checked:bg-white/15 "
        + "after:absolute after:-inset-px after:rounded-lg has-checked:after:bg-zinc-800/[2.5%] dark:has-checked:after:bg-white/10 "
        + Focus;

    /// <summary>A button's icon: faint until the button is chosen.</summary>
    internal const string ButtonIcon =
        "text-zinc-300 dark:text-zinc-400 group-has-checked/option:text-zinc-800 dark:group-has-checked/option:text-white";

    /// <summary>One segment of a segmented radio group: raised and white when chosen.</summary>
    internal const string Segment =
        "group/option flex flex-1 items-center justify-center gap-2 rounded-md text-sm font-medium whitespace-nowrap "
        + "text-zinc-600 hover:text-zinc-800 dark:text-white/70 dark:hover:text-white "
        + "has-checked:bg-white has-checked:text-zinc-800 has-checked:shadow-xs "
        + "dark:has-checked:bg-white/20 dark:has-checked:text-white "
        + "has-disabled:opacity-50 dark:has-disabled:opacity-75 "
        + Focus;

    /// <summary>A segment's icon.</summary>
    internal const string SegmentIcon =
        "text-zinc-500 dark:text-zinc-400 group-has-checked/option:text-zinc-800 dark:group-has-checked/option:text-white";

    /// <summary>What marks a control inside a field: Flux's <c>data-flux-control</c>.</summary>
    internal const string ControlMark = "ui-control";

    // White with a zinc-300 edge and shadow-xs; the accent, edgeless and flat, when on; paler when disabled.
    private const string Box =
        "flex size-[1.125rem] shrink-0 items-center justify-center border text-sm text-zinc-700 dark:text-zinc-800 "
        + "border-zinc-300 bg-white shadow-xs dark:border-white/10 dark:bg-white/10 "
        + "group-has-checked/option:border-transparent group-has-checked/option:bg-fx-accent group-has-checked/option:shadow-none "
        + "group-data-indeterminate/option:border-transparent group-data-indeterminate/option:bg-fx-accent "
        + "group-data-indeterminate/option:shadow-none "
        + "group-has-disabled/option:border-zinc-200 group-has-disabled/option:opacity-75 group-has-disabled/option:shadow-none "
        + "dark:group-has-disabled/option:border-white/5 "
        + "group-has-checked/option:group-has-disabled/option:border-transparent "
        + "group-has-checked/option:group-has-disabled/option:opacity-50 "
        + "group-data-indeterminate/option:group-has-disabled/option:border-transparent "
        + "group-data-indeterminate/option:group-has-disabled/option:opacity-50 "
        + "group-[[data-invalid]:not(:has(:checked))]/option:border-red-500";

    /// <summary>
    ///     The real input's attributes: the control's mark, which a field reads <c>disabled</c> beside, then what
    ///     the call site forwards.
    /// </summary>
    internal static Dictionary<string, string?> Marks(IReadOnlyDictionary<string, string?>? forwarded)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal) { ["data-" + ControlMark] = "" };
        foreach (var (name, value) in forwarded ?? ReadOnlyDictionary<string, string?>.Empty)
        {
            marks[name] = value;
        }

        return marks;
    }

    /// <summary>What a choice posts from a plain form: its value as text, or the browser's own "on" for none.</summary>
    internal static string? Posted(object? value) =>
        value is null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A segment's height and side padding.</summary>
    internal static string SegmentSize(Ui.RadioGroupSize size) =>
        size == Ui.RadioGroupSize.Sm ? "h-7 px-3" : "h-8 px-4";
}

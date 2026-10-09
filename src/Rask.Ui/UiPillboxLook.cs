namespace Rask;

/// <summary>
///     Flux's pillbox as measured on <c>fluxui.dev/components/pillbox</c>: the trigger, a pill, the input among
///     the pills and a row of its list. The popup's box and the search field are <see cref="UiListboxLook" />'s
///     and <see cref="UiSelectLook" />'s.
/// </summary>
internal static class UiPillboxLook
{
    /// <summary>The trigger: the input's box, as tall as its pills need.</summary>
    internal const string Trigger =
        "group/pillbox-trigger flex items-center w-full overflow-hidden border text-sm leading-5 "
        + "bg-white dark:bg-white/10 shadow-xs data-invalid:shadow-none "
        + "border-zinc-200 border-b-zinc-300/80 dark:border-white/10 dark:border-b-white/10 "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500 "
        + "data-disabled:shadow-none data-disabled:border-b-zinc-200 dark:data-disabled:bg-white/[7%] "
        + "dark:data-disabled:border-white/5 dark:data-disabled:border-b-white/5";

    /// <summary>What the trigger holds before its chevron: the placeholder, then the pills.</summary>
    internal const string Selected = "flex flex-1 gap-2 overflow-hidden text-zinc-700 dark:text-zinc-300";

    /// <summary>A wrapper Flux's script fills, with no box of its own.</summary>
    internal const string Contents = "contents";

    /// <summary>Shown while nothing is picked.</summary>
    internal const string Placeholder = "block ms-1 text-zinc-400 dark:text-zinc-400";

    /// <summary>The pills, wrapping onto as many lines as they need.</summary>
    internal const string Pills = "flex flex-wrap flex-auto gap-1";

    /// <summary>One picked option.</summary>
    internal const string Pill =
        "flex max-w-full px-2 cursor-default text-sm leading-4 bg-zinc-400/15 dark:bg-zinc-400/40 text-zinc-700 dark:text-zinc-200";

    /// <summary>A pill's words.</summary>
    internal const string PillWords = "min-w-0 font-medium";

    /// <summary>The cross that takes a pill off.</summary>
    internal const string Remove =
        "shrink-0 -me-2 px-1 text-zinc-400 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-white";

    /// <summary>The chevron at the end of the trigger.</summary>
    internal const string Chevron = "shrink-0 self-start ms-2 -me-1 text-zinc-300 dark:text-white/60";

    /// <summary>The input among the pills: no box of its own, and never narrower than three rem.</summary>
    internal const string Input =
        "flex-1 min-w-12 ms-1 bg-transparent outline-none text-sm leading-5 text-zinc-700 dark:text-zinc-300 "
        + "placeholder:text-zinc-400 dark:placeholder:text-zinc-400";

    /// <summary>The button at the end of a trigger that holds an input.</summary>
    internal const string InputChevron =
        "relative flex self-start items-center justify-center gap-2 size-8 -my-1 -me-2 rounded-md text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 "
        + "dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The icon in that button.</summary>
    internal const string InputChevronIcon = "text-zinc-400/75 dark:text-white/60";

    /// <summary>
    ///     A row of the list. Its fill is the keyboard's cursor (<c>data-active</c>) or the pointer; while the
    ///     pointer is on a row, the cursor's row gives its fill up, so one row at a time is lit.
    /// </summary>
    internal const string Option =
        "group/option overflow-hidden outline-hidden data-hidden:hidden flex items-center px-2 py-1.5 w-full rounded-md "
        + "text-start text-sm font-medium select-none cursor-default scroll-my-[.3125rem] "
        + "text-zinc-800 dark:text-white "
        + "[[popover]:popover-open_&]:data-active:bg-zinc-100 [[popover]:popover-open_&]:hover:bg-zinc-100 "
        + "dark:[[popover]:popover-open_&]:data-active:bg-zinc-600 dark:[[popover]:popover-open_&]:hover:bg-zinc-600 "
        + "[[popover]:has([data-ui-listbox-option]:hover,[data-ui-option-create]:hover)_&:not(:hover)]:data-active:bg-transparent "
        + "aria-disabled:text-zinc-400 dark:aria-disabled:text-zinc-400 aria-disabled:hover:bg-transparent!";

    /// <summary>The tick's place at the end of a row.</summary>
    internal const string CheckSlot = "w-6 shrink-0";

    /// <summary>The tick, drawn in the picked rows only.</summary>
    internal const string Check = "shrink-0 hidden group-data-selected/option:block";

    private static readonly Dictionary<string, string?> SearchInput = new(StringComparer.Ordinal)
    {
        ["data-ui-pillbox-input"] = null,
        ["data-rask-keys"] = UiListKeys.Pills,
        ["data-rask-clear-keys"] = UiListKeys.PillsClear,
    };

    /// <summary>The padding and corners of the trigger at each size.</summary>
    /// <param name="small">Flux's <c>size="sm"</c>.</param>
    internal static string TriggerSize(bool small) =>
        small ? "min-h-6 py-1 ps-1 pe-2 rounded-md" : "min-h-10 py-[7px] ps-[7px] pe-3 rounded-lg";

    /// <summary>The trigger that holds an input: a text cursor, and the browser's own ring while the input has focus.</summary>
    internal const string TriggerWithInput = "cursor-text focus-within:[outline:-webkit-focus-ring-color_auto]";

    /// <summary>The list when it is the popup: shorter than a select's.</summary>
    internal const string Popup = "max-h-[14rem]";

    /// <summary>The popup when a search field sits over the list: as tall as both.</summary>
    internal const string SearchedPopup = "max-h-none";

    /// <summary>The list under a search field: a select's height.</summary>
    internal const string SearchedList = "max-h-[20rem]";

    /// <summary>A pill's padding and corners at each size.</summary>
    /// <param name="small">Flux's <c>size="sm"</c>.</param>
    internal static string PillSize(bool small) => small ? "py-[3px] rounded-sm" : "py-1 rounded-md";

    /// <summary>How far the cross reaches over its pill's padding at each size.</summary>
    /// <param name="small">Flux's <c>size="sm"</c>.</param>
    internal static string RemoveSize(bool small) => small ? "-my-0.5 py-0.5" : "-my-[3px] py-[3px]";

    /// <summary>Where the chevron sits beside the first line of pills at each size.</summary>
    /// <param name="small">Flux's <c>size="sm"</c>.</param>
    internal static string ChevronSize(bool small) => small ? "my-px" : "mt-0.5";

    /// <summary>What marks an input as the pillbox's: the search field's, or the one among the pills.</summary>
    /// <param name="placeholder">The pillbox's placeholder, which the input among the pills keeps as data.</param>
    internal static Dictionary<string, string?> InputMarks(string? placeholder) =>
        placeholder is null
            ? SearchInput
            : new Dictionary<string, string?>(SearchInput, StringComparer.Ordinal) { ["data-placeholder"] = placeholder };
}

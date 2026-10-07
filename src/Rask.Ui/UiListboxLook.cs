namespace Rask;

/// <summary>
///     Flux's open list of options — its <c>ui-options</c> and <c>ui-option</c> — as measured on
///     <c>fluxui.dev/components/select</c>: the popup's box, a row at rest and as the active one, and what a row
///     holds.
/// </summary>
/// <remarks>
///     Shared by every control that drops a list of options under itself: the select, the pillbox (whose rows
///     are <see cref="UiPillboxLook" />'s) and the autocomplete (the box alone).
/// </remarks>
internal static class UiListboxLook
{
    /// <summary>The popup's box: white, as wide as what it hangs from, at most twenty rem tall.</summary>
    internal const string Box =
        "[:where(&)]:max-h-[20rem] p-[.3125rem] overscroll-y-none rounded-lg shadow-xs "
        + "border border-zinc-200 dark:border-zinc-600 bg-white dark:bg-zinc-700 text-start";

    /// <summary>The popup of a select or a pillbox: the box, never narrower than twelve rem.</summary>
    internal const string Popup = "[:where(&)]:min-w-48 " + Box;

    /// <summary>The popup when it is the list itself, and scrolls.</summary>
    internal const string Scrolls = "overflow-y-auto";

    /// <summary>The popup when a search field sits over the list: a column whose list scrolls.</summary>
    internal const string Column = "overflow-auto [&:popover-open]:flex [&:popover-open]:flex-col [&:popover-open]:overflow-y-hidden";

    /// <summary>
    ///     A row. Its fill is the keyboard's cursor (<c>data-active</c>) or the pointer; while the pointer is on
    ///     a row, the cursor's row gives its fill up, so one row at a time is lit — as on Flux, whose script
    ///     moves the cursor under the pointer.
    /// </summary>
    internal const string Option =
        "group/option overflow-hidden outline-hidden data-hidden:hidden flex items-start px-2 py-1.5 w-full rounded-md "
        + "text-start text-sm font-medium select-none cursor-default scroll-my-[.3125rem] "
        + "text-zinc-800 dark:text-white "
        + "[[data-ui-options]:popover-open_&]:data-active:bg-zinc-100 [[data-ui-options]:popover-open_&]:hover:bg-zinc-100 "
        + "dark:[[data-ui-options]:popover-open_&]:data-active:bg-zinc-600 dark:[[data-ui-options]:popover-open_&]:hover:bg-zinc-600 "
        + "[[data-ui-options]:has([data-ui-option]:hover,[data-ui-option-create]:hover)_&:not(:hover)]:data-active:bg-transparent "
        + "aria-disabled:text-zinc-400 dark:aria-disabled:text-zinc-400 aria-disabled:hover:bg-transparent!";

    /// <summary>The row's content beside its indicator.</summary>
    internal const string OptionBody = "flex items-center min-w-0";

    /// <summary>The same in a row with a line under its words: the tick and the icon stay on the first line.</summary>
    internal const string OptionBodyTall = "flex items-start min-w-0";

    /// <summary>A row with a line under its words is taller.</summary>
    internal const string OptionTall = "py-2.5";

    /// <summary>Indicator and icon, before the words.</summary>
    internal const string OptionLead = "flex items-center";

    /// <summary>The tick's place: as wide in a row that is not picked, so the words line up.</summary>
    internal const string Indicator = "w-6 shrink-0 flex items-center min-h-5 [[data-ui-select-button]_&]:hidden";

    /// <summary>The tick, drawn in the picked row only.</summary>
    internal const string Check = "shrink-0 hidden group-data-selected/option:block";

    /// <summary>The option's icon.</summary>
    internal const string Icon = "shrink-0 me-2 text-zinc-400 dark:text-white/60";

    /// <summary>Words over description.</summary>
    internal const string OptionText = "flex flex-col gap-1 min-w-0";

    /// <summary>The words, where the button repeats the picked option: one line, cut short.</summary>
    internal const string Words = "[[data-ui-select-button]_&]:truncate";

    /// <summary>The words beside an avatar: a name, kept on one line.</summary>
    internal const string WordsBesideAvatar = "whitespace-nowrap [[data-ui-select-button]_&]:truncate";

    /// <summary>The line under the words. The button does not repeat it.</summary>
    internal const string Description = "[[data-ui-select-button]_&]:hidden text-xs font-normal text-zinc-500 dark:text-white/70";

    /// <summary>The heading over a group of rows.</summary>
    internal const string GroupHeading = "px-2 py-1.5 text-xs font-medium text-zinc-500 dark:text-zinc-300 select-none";

    /// <summary>The line between two groups.</summary>
    internal const string GroupSeparator = "-mx-[.3125rem] my-[.3125rem] h-px border-0 bg-zinc-800/5 dark:bg-white/10";

    /// <summary>The "no results" row.</summary>
    /// <summary>
    ///     The keys an open list answers, for <c>data-rask-contain-keys</c> on the text field they are typed into:
    ///     the browser does nothing of its own with them there.
    /// </summary>
    internal const string ListKeys = "Enter ArrowUp ArrowDown Home End PageUp PageDown";

    internal const string Empty =
        "data-hidden:hidden block items-center px-2 py-1.5 w-full rounded-md text-start text-sm font-medium select-none cursor-default "
        + "text-zinc-500 dark:text-zinc-300";

    /// <summary>The row that makes a new option.</summary>
    internal const string Create =
        "group/option overflow-hidden outline-hidden data-hidden:hidden flex items-center px-2 py-1.5 w-full rounded-md "
        + "text-start text-sm font-medium select-none cursor-default scroll-my-[.3125rem] "
        + "text-zinc-800 dark:text-white "
        + "[[data-ui-options]:popover-open_&]:data-active:bg-zinc-100 [[data-ui-options]:popover-open_&]:hover:bg-zinc-100 "
        + "dark:[[data-ui-options]:popover-open_&]:data-active:bg-zinc-600 dark:[[data-ui-options]:popover-open_&]:hover:bg-zinc-600 "
        + "[[data-ui-options]:has([data-ui-option]:hover,[data-ui-option-create]:hover)_&:not(:hover)]:data-active:bg-transparent";

    /// <summary>The plus before the create row's words.</summary>
    internal const string CreateLead = "w-6 shrink-0";

    /// <summary>The create row's words.</summary>
    internal const string CreateWords = "truncate";

    /// <summary>The spinner at the end of the create row while its handler runs.</summary>
    internal const string CreateBusy = "hidden group-data-loading/option:block ms-auto shrink-0 text-zinc-400 animate-spin";
}

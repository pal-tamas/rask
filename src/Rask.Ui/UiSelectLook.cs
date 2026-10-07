namespace Rask;

/// <summary>
///     Flux's select, as measured on <c>fluxui.dev/components/select</c>: the classes of the native control,
///     the listbox button, the combobox input's chevron and the search field.
/// </summary>
/// <remarks>
///     The open list and its rows are <see cref="UiListboxLook" />'s, which a pillbox and an autocomplete draw
///     too. One home for the literals, because <c>UiSelect&lt;T&gt;</c> is generic and a static in it would be
///     one copy per <c>T</c>.
/// </remarks>
internal static class UiSelectLook
{
    /// <summary>The root of the two drawn variants: Flux's <c>ui-select</c>.</summary>
    internal const string Root = "block";

    /// <summary>
    ///     The native <c>&lt;select&gt;</c> over the input's own box: room for the chevron
    ///     (<c>[data-ui-select-native]</c> in ui.css) and the placeholder's grey while it is the chosen option.
    /// </summary>
    internal const string Native =
        "ps-3 pe-10 shadow-xs disabled:shadow-none data-invalid:shadow-none "
        + "[&:has(option[value='']:checked)]:text-zinc-400 dark:[&:has(option[value='']:checked)]:text-zinc-400 "
        + "dark:[&_option]:bg-zinc-700 dark:[&_option]:text-white [&_optgroup]:font-normal";

    /// <summary>The listbox's button: an input's box that holds what was picked and a chevron.</summary>
    internal const string Button =
        "group/select-button cursor-default overflow-hidden flex items-center w-full border "
        + "bg-white dark:bg-white/10 disabled:shadow-none dark:disabled:bg-white/[7%] "
        + "border-zinc-200 border-b-zinc-300/80 disabled:border-b-zinc-200 "
        + "dark:border-white/10 dark:border-b-white/10 dark:disabled:border-white/5 dark:disabled:border-b-white/5 "
        + "shadow-xs data-invalid:shadow-none "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500 disabled:data-invalid:border-red-500";

    /// <summary>What was picked, inside the button: Flux's <c>ui-selected</c>.</summary>
    internal const string Selected =
        "flex flex-1 gap-2 min-w-0 text-start text-zinc-700 dark:text-zinc-300 "
        + "group-disabled/select-button:text-zinc-500 dark:group-disabled/select-button:text-zinc-400";

    /// <summary>The placeholder, while nothing is picked.</summary>
    internal const string Placeholder =
        "block truncate text-zinc-400 group-disabled/select-button:text-zinc-400/70 dark:group-disabled/select-button:text-zinc-500";

    /// <summary>The picked option, drawn again in the button.</summary>
    internal const string Picked = "truncate min-w-0";

    /// <summary>Flux's <c>prefix</c>: words that stay before the picked option.</summary>
    internal const string Prefix = "shrink-0 me-1 max-w-1/2 truncate text-zinc-500 dark:text-zinc-400";

    /// <summary>The button's chevron.</summary>
    internal const string Chevron =
        "ms-2 -me-1 text-zinc-400/75 group-hover/select-button:text-zinc-800 "
        + "dark:text-white/60 dark:group-hover/select-button:text-white "
        + "group-disabled/select-button:text-zinc-200! dark:group-disabled/select-button:text-white/20! "
        // Open, the pointer is the list's: Flux takes the page's pointer away, so nothing behind it hovers.
        + "[[data-ui-select]:has(:popover-open)_&]:text-zinc-400/75! dark:[[data-ui-select]:has(:popover-open)_&]:text-white/60!";

    /// <summary>The button that empties the answer, laid over the end of the listbox's button.</summary>
    internal const string ClearSlot = "absolute top-0 bottom-0 end-8 flex items-center";

    /// <summary>The chevron button at the end of the combobox's input.</summary>
    internal const string InputChevron =
        "relative flex items-center justify-center gap-2 size-8 -me-1 rounded-md text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 "
        + "dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The spinner beside the chevron while the page answers a search it filters itself.</summary>
    internal const string InputBusy = "hidden [[data-loading]>&]:block text-zinc-400 animate-spin";

    /// <summary>The chevron inside the input's button.</summary>
    internal const string InputChevronIcon =
        "text-zinc-400/75 dark:text-white/60 "
        + "[[data-ui-input]:hover_&]:text-zinc-800 dark:[[data-ui-input]:hover_&]:text-white";

    /// <summary>The button that empties the search field.</summary>
    internal const string SearchClearButton =
        "relative inline-flex items-center justify-center gap-2 size-8 rounded-md text-sm font-medium whitespace-nowrap "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 "
        + "dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The search field's row at the top of the list.</summary>
    internal const string Search = "relative flex grow -mx-[.3125rem] -mt-[.3125rem] mb-[.3125rem]";

    /// <summary>The search field's leading icon.</summary>
    internal const string SearchIcon =
        "absolute top-0 bottom-0 start-0 flex items-center justify-center ps-3.5 text-xs text-zinc-400";

    /// <summary>The search field's input.</summary>
    internal const string SearchInput =
        "flex items-center w-full h-10 px-9 py-2 text-base sm:text-sm font-medium outline-hidden focus:ring-0 "
        + "bg-white dark:bg-zinc-700 text-zinc-800 dark:text-white "
        + "placeholder:text-zinc-400 dark:placeholder:text-zinc-400 "
        + "border-b border-zinc-200 dark:border-zinc-600";

    /// <summary>The search field's clear button: there while the field holds text.</summary>
    internal const string SearchClear =
        "absolute top-0 bottom-0 end-0 flex items-center justify-center pe-1 transition-opacity "
        + "[[data-ui-select-search]:has(input:placeholder-shown)_&]:hidden "
        + "[[data-ui-pillbox-search]:has(input:placeholder-shown)_&]:hidden";

    /// <summary>The list under a search field: the part of the popup that scrolls.</summary>
    internal const string SearchedList = "block overflow-auto cursor-default -me-[.3125rem] -my-[.3125rem] pe-[.3125rem] py-[.3125rem]";

    /// <summary>The button's height, text and corners at one of Flux's sizes.</summary>
    internal static string ButtonSize(Ui.SelectSize size) => size switch
    {
        Ui.SelectSize.Sm => "h-8 py-1.5 ps-3 pe-3 text-sm leading-[1.125rem] rounded-md",
        Ui.SelectSize.Xs => "h-6 py-1.5 ps-3 pe-3 text-xs leading-[1.125rem] rounded-md",
        _ => "h-10 py-2 ps-3 pe-3 text-base sm:text-sm rounded-lg",
    };

    /// <summary>The same size, as the input's own enum names it.</summary>
    internal static Ui.InputSize InputSize(Ui.SelectSize size) => size switch
    {
        Ui.SelectSize.Sm => Ui.InputSize.Sm,
        Ui.SelectSize.Xs => Ui.InputSize.Xs,
        _ => Ui.InputSize.Base,
    };
}

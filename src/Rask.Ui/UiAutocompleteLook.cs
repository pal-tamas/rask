namespace Rask;

/// <summary>
///     Flux's autocomplete as measured on <c>fluxui.dev/components/autocomplete</c>: what is its own over the
///     input's look and the open list's.
/// </summary>
internal static class UiAutocompleteLook
{
    /// <summary>The root: the field and the list it drops.</summary>
    internal const string Root = "block";

    /// <summary>The root's marker, where Flux writes <c>data-flux-autocomplete</c>.</summary>
    internal static readonly Dictionary<string, string?> RootMarks = new(StringComparer.Ordinal)
    {
        ["data-ui-autocomplete"] = null,
        // The list is the browser's (rask-list.ts): the answer is the input's text, and typing hides rows.
        ["data-rask-list"] = "text filter",
    };

    /// <summary>The list, over <see cref="UiListboxLook.Box" />: nothing to show, nothing drawn.</summary>
    internal const string Items = "[&:not(:has([data-ui-autocomplete-item]:not([data-hidden])))]:hidden!";

    /// <summary>The empty slot after the items: no box of its own.</summary>
    internal const string Empty = "contents cursor-default";

    /// <summary>
    ///     A suggestion. Its fill is the cursor (<c>data-active</c>), which the runtime writes for the pointer
    ///     and the keys alike: one row at a time is lit, and none by <c>:hover</c>.
    /// </summary>
    internal const string Item =
        "group/option outline-hidden data-hidden:hidden flex items-center px-2 py-1.5 w-full rounded-md "
        + "text-start text-sm font-medium select-none cursor-default scroll-my-[.3125rem] "
        + "text-zinc-800 dark:text-white "
        + "[[data-ui-autocomplete-items]:popover-open_&]:data-active:bg-zinc-100 dark:[[data-ui-autocomplete-items]:popover-open_&]:data-active:bg-zinc-600 "
        + "aria-disabled:text-zinc-400 dark:aria-disabled:text-zinc-400";

    /// <summary>The input's height for the autocomplete's.</summary>
    internal static Ui.InputSize? InputSize(Ui.AutocompleteSize? size) => size switch
    {
        Ui.AutocompleteSize.Sm => Ui.InputSize.Sm,
        Ui.AutocompleteSize.Xs => Ui.InputSize.Xs,
        _ => null,
    };
}

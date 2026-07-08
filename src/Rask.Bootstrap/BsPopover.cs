namespace Rask.Bootstrap;

// Shared opt-in markers for the declarative fixed-position popover helper (installRaskPopover in
// Rask.Core's rask-dom.js). Every Popper-less .dropdown-menu component (the date/time pickers,
// BsDropdown, BsMultiSelect) marks its .dropdown wrapper with data-rask-popover and its trigger with
// data-rask-anchor. While the menu carries .show the client JS re-anchors it with position:fixed and
// viewport-computed coordinates, so it escapes any overflow-clipping ancestor (a card, a scroll region)
// instead of being cut off like a plain position:absolute menu. Attribute-only — no render hot-path
// cost; the dictionaries are shared immutable singletons.
internal static class BsPopover
{
    // Marks the .dropdown wrapper the helper manages (it resolves the .show menu + the anchor inside it).
    internal static readonly IReadOnlyDictionary<string, string?> Wrapper =
        new Dictionary<string, string?> { ["rask-popover"] = "" };

    // Wrapper variant that asks the helper to right-align the menu to the trigger (BsDropdown AlignEnd).
    private static readonly IReadOnlyDictionary<string, string?> WrapperEnd =
        new Dictionary<string, string?> { ["rask-popover"] = "", ["rask-popover-align"] = "end" };

    // Marks the trigger element the menu anchors to (falls back to .dropdown-toggle / firstElementChild).
    internal static readonly IReadOnlyDictionary<string, string?> Anchor =
        new Dictionary<string, string?> { ["rask-anchor"] = "" };

    internal static IReadOnlyDictionary<string, string?> WrapperFor(bool alignEnd) =>
        alignEnd ? WrapperEnd : Wrapper;
}

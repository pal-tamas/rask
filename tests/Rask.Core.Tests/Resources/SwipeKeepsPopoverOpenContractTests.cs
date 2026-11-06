namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract: a finger that starts outside an open popover and moves on — a swipe — closes nothing,
///     where a tap outside still closes it, as Flux UI's popovers, dropdowns and menus do (#1260).
/// </summary>
/// <remarks>
///     The browser's light dismiss closes an auto popover on the pointerup of a press that began outside it, and
///     under an overlay's page lock a finger that cannot scroll the page lifts with that pointerup (Chromium on
///     Linux). rask-overlay.ts hands the finger to the popover once it has moved past a tap's slop, so the pointerup
///     lands inside it. Structural, as <see cref="PopoverInvokerClientContractTests" /> is: the hooks run against a
///     live document. The behaviour is held in a browser by <c>UiMenuHookTests</c> (Server E2E) and by
///     <c>flux parity-popover --view 390x844</c>.
/// </remarks>
public class SwipeKeepsPopoverOpenContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string Overlay => File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "Resources", "rask-overlay.ts"));

    private static string Hooks => File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "Resources", "rask-hooks.ts"));

    [Fact]
    public void A_finger_that_moves_past_a_taps_slop_is_handed_to_the_open_popover()
    {
        var swipe = Section(Overlay, "function installSwipeKeepsOpen", "// ----- Modal dialogs");

        Assert.Contains("panel.setPointerCapture(p.pointerId)", swipe, StringComparison.Ordinal);
        Assert.Contains("<= SLOP", swipe, StringComparison.Ordinal);
        Assert.Contains("const SLOP = 15;", Overlay, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mouse_and_a_finger_put_down_inside_the_popover_are_left_to_the_browser()
    {
        var swipe = Section(Overlay, "function installSwipeKeepsOpen", "// ----- Modal dialogs");

        Assert.Contains("p.pointerType === \"mouse\" ? null : topmost(doc)", swipe, StringComparison.Ordinal);
        Assert.Contains("!(p.target instanceof Node && panel.contains(p.target))", swipe, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_page_with_the_hooks_installs_it()
    {
        var install = Section(Overlay, "if (page) {", "}");

        Assert.Contains("installSwipeKeepsOpen(page);", install, StringComparison.Ordinal);
        Assert.Contains("import \"./rask-overlay.js\";", Hooks, StringComparison.Ordinal);
    }

    private static string Section(string source, string from, string to)
    {
        var start = source.LastIndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' is gone from rask-overlay.ts");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        return source[start..(end < 0 ? source.Length : end)];
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }
}

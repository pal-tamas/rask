using System.Text.RegularExpressions;
using Rask.Example.Playground;

namespace Rask.Example.Playground.Tests;

/// <summary>
///     Keeps the tutorial track's DOM hooks and the browser E2E that drives them in step — the same
///     contract, and the same failure mode, as <see cref="IdeBadgeStateTests" />.
/// </summary>
/// <remarks>
///     The E2E opens a chapter by id and asserts on chapter state. If a redesign drops one of those hooks,
///     the locator resolves to nothing and the suite fails by timing out, blaming whatever step it was in
///     the middle of. These assertions move that failure into the fast unit gate, where it names itself.
/// </remarks>
public class TutorialPaneStateTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    [Fact]
    public void Every_chapter_state_renders_a_distinct_class()
    {
        Assert.Equal("is-open", TutorialPaneState.ClassFor(ChapterState.Open));
        Assert.Equal("is-active", TutorialPaneState.ClassFor(ChapterState.Active));
        Assert.Equal("is-locked", TutorialPaneState.ClassFor(ChapterState.Locked));

        var rendered = Enum.GetValues<ChapterState>().Select(TutorialPaneState.ClassFor).ToArray();
        Assert.Equal(rendered.Length, rendered.Distinct().Count());

        // All is what the E2E-parity check below measures against, so it must cover every state class the
        // app can render — the states, plus the completion tick that composes on top of them.
        Assert.Equal(
            rendered.Append(TutorialPaneState.Done).OrderBy(c => c, StringComparer.Ordinal),
            TutorialPaneState.All.OrderBy(c => c, StringComparer.Ordinal));
    }

    // The tick is independent of the state, not another value of it: the chapter you just ran is BOTH the
    // active one and a completed one, and the E2E asserts the tick while the chapter is still active.
    [Fact]
    public void A_completed_chapter_keeps_its_state_class_alongside_the_tick()
    {
        var active = TutorialPaneState.ClassesFor(ChapterState.Active, done: true);

        Assert.Contains("is-active", active, StringComparison.Ordinal);
        Assert.Contains("is-done", active, StringComparison.Ordinal);
        Assert.StartsWith(TutorialPaneState.ChapterClass, active, StringComparison.Ordinal);

        Assert.DoesNotContain("is-done", TutorialPaneState.ClassesFor(ChapterState.Active, done: false),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Chapter_and_tab_hooks_are_stable_and_unique()
    {
        Assert.Equal("pg-chapter-5", TutorialPaneState.ChapterId(5));
        Assert.NotEqual(
            TutorialPaneState.TabId(PlaygroundTab.Tutorial),
            TutorialPaneState.TabId(PlaygroundTab.Examples));

        // One id per chapter, or clicking "chapter 5" could open something else.
        var ids = TutorialChapters.All.Select(c => TutorialPaneState.ChapterId(c.Number)).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_view_composes_its_pane_hooks_from_TutorialPaneState()
    {
        var view = ReadView();

        Assert.True(
            view.Contains("TutorialPaneState.ClassesFor(", StringComparison.Ordinal),
            "The chapter list no longer composes its class from TutorialPaneState, so `.pg-chapter.is-*` "
            + "resolves to nothing and the browser E2E waits minutes to tell you.");
        Assert.Contains("TutorialPaneState.ChapterId(", view, StringComparison.Ordinal);
        Assert.Contains("TutorialPaneState.TabId(", view, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hooks_the_E2E_drives_are_ones_the_app_can_render()
    {
        var e2e = File.ReadAllText(Path.Combine(
            _repoRoot, "tests", "Rask.Examples.E2E.Tests", "PlaygroundExampleTests.cs"));

        // The E2E has to still exercise the track at all — deleting the steps would "fix" a red run by no
        // longer checking that the tutorial works, which is the failure worth guarding against.
        Assert.Contains(TutorialPaneState.TabId(PlaygroundTab.Tutorial), e2e, StringComparison.Ordinal);

        // Match BOTH shapes the E2E can use: the class hook (`.pg-chapter.is-done`) and the per-chapter id
        // hook it actually prefers (`#pg-chapter-5.is-done`). Anchoring on only the first is how this guard
        // passed while checking nothing.
        var states = Regex.Matches(e2e, @"(?:\.pg-chapter|#pg-chapter-\d+)\.([A-Za-z0-9_-]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The E2E must actually assert on chapter state somewhere, or this loop guards nothing — which was
        // this test's own first bug.
        Assert.NotEmpty(states);

        foreach (var state in states)
        {
            Assert.True(
                TutorialPaneState.All.Contains(state, StringComparer.Ordinal),
                $"PlaygroundExampleTests drives '.pg-chapter.{state}', which no chapter ever renders. It "
                + $"renders one of: {string.Join(", ", TutorialPaneState.All)}.");
        }

        foreach (var id in Regex.Matches(e2e, @"#(pg-chapter-\d+)")
                     .Select(m => m.Groups[1].Value)
                     .Distinct(StringComparer.Ordinal))
        {
            Assert.True(
                TutorialChapters.All.Any(c => TutorialPaneState.ChapterId(c.Number) == id),
                $"PlaygroundExampleTests opens '#{id}', which is not a chapter in the track.");
        }
    }

    // The locked state is the only thing standing between a build without the SQLite packages and a
    // reader pressing Run on a chapter that cannot compile — so the view has to consult it.
    [Fact]
    public void The_view_locks_the_data_chapters_when_the_build_ships_without_them()
    {
        var view = ReadView();

        Assert.Contains("RASK_PLAYGROUND_DATA", view, StringComparison.Ordinal);
        Assert.Contains("NeedsDatabase && !DataChaptersAvailable", view, StringComparison.Ordinal);
        Assert.Contains("ChapterState.Locked", view, StringComparison.Ordinal);
    }

    // #647: Run and Reset were gated on the editor having mounted, but the controls that LOAD code were
    // not — they only guarded against a compile being in flight. Loading a chapter is a JS round-trip to
    // setEditorValue, and before mountEditor has run there is no editor for the host, so the call is a
    // silent no-op and mountEditor then installs the starter over the selection: the brief says one
    // chapter, the editor holds another, and Run compiles the wrong code and ticks the chapter off.
    //
    // The fix is one shared condition. This asserts the condition is genuinely shared — a new control
    // written with the old bare `Disabled: _busy` is the exact regression, and it re-opens the race.
    [Fact]
    public void Every_control_is_gated_on_the_editor_being_ready_not_just_on_busy()
    {
        var view = ReadView();

        Assert.Contains("private bool CanInteract => !_busy && _editorReady;", view, StringComparison.Ordinal);

        var bare = Regex.Matches(view, @"Disabled: _busy\b").Count;
        Assert.True(
            bare == 0,
            $"{bare} control(s) still gate only on _busy. Loading code into an editor that has not mounted "
            + "is a silent no-op (#647) — use `Disabled: !CanInteract` so the control waits for the editor, "
            + "as Run and Reset already do.");

        // And every control really is gated: one per Disabled: site, all of them through CanInteract.
        var disabled = Regex.Matches(view, @"Disabled: ").Count;
        var gated = Regex.Matches(view, @"Disabled: !CanInteract").Count;
        Assert.Equal(disabled, gated);
    }

    private static string ReadView() =>
        File.ReadAllText(Path.Combine(_repoRoot, "samples", "Rask.Example.Playground", "PlaygroundView.cs"));

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}

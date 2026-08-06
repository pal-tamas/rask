using System.Text.RegularExpressions;
using Rask.Example.Playground;

namespace Rask.Example.Playground.Tests;

/// <summary>
///     Keeps the browser E2E's readiness selector and the markup the playground actually renders in step.
/// </summary>
/// <remarks>
///     #593: <c>PlaygroundExampleTests</c> waits for <c>.pg-ide.is-ready</c> to know the in-browser Roslyn
///     workspace has finished pulling its references. #470 turned the pill into a <c>BsBadge</c> and
///     dropped the state classes, so the selector matched nothing and the browser gate was red on
///     <c>main</c> from then on — and because an unresolvable locator fails by <i>timing out</i>, the
///     report pointed at the reference download rather than at the missing class, so it read as a sandbox
///     network problem and got waved through with <c>RASK_SKIP_E2E=1</c>.
///     <para>
///         The point of these tests is <b>where they fail</b>: in the fast unit gate, naming the cause,
///         rather than three minutes into a suite people have learned to skip.
///     </para>
/// </remarks>
public class IdeBadgeStateTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    [Fact]
    public void Every_state_renders_a_distinct_class()
    {
        Assert.Equal("is-loading", IdeBadgeState.ClassFor(IdeState.Loading));
        Assert.Equal("is-ready", IdeBadgeState.ClassFor(IdeState.Ready));
        Assert.Equal("is-off", IdeBadgeState.ClassFor(IdeState.Unavailable));

        // Distinct, or a selector can't tell two states apart — "ready" matching "loading" would make the
        // E2E pass before the workspace is usable, which is worse than failing.
        var rendered = Enum.GetValues<IdeState>().Select(IdeBadgeState.ClassFor).ToArray();
        Assert.Equal(rendered.Length, rendered.Distinct().Count());

        // And every state the enum can hold is covered by All, which is what the selector check below
        // measures against — a new state added without a class would otherwise be invisible here.
        Assert.Equal(rendered.OrderBy(c => c), IdeBadgeState.All.OrderBy(c => c));
    }

    [Fact]
    public void The_badge_renders_the_state_class_beside_its_positioning_hook()
    {
        // THE regression assertion — this is the edit #470 made, reduced to what it broke. Checking the
        // constants alone would not catch it: they stayed correct while the view stopped using them.
        var view = File.ReadAllText(Path.Combine(
            _repoRoot, "samples", "Rask.Example.Playground", "PlaygroundView.cs"));

        Assert.True(
            view.Contains("IdeBadgeState.ClassFor(", StringComparison.Ordinal),
            "The readiness pill no longer composes its class from IdeBadgeState, so `.pg-ide.is-*` "
            + "resolves to nothing and the browser E2E waits three minutes to tell you (#593). Keep the "
            + "state in the class, whatever the pill is styled with.");

        // The `.pg-ide` half is the positioning hook PlaygroundView.css styles, and the other half of
        // every selector the E2E uses.
        Assert.Contains("\"pg-ide {IdeBadgeState.ClassFor(", view, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pill_classes_the_E2E_waits_on_are_ones_the_app_can_render()
    {
        // The other direction: the E2E may not wait on a class no state produces. Catches the mirror
        // mistake — a test updated to a selector nobody renders — which fails the same slow, silent way.
        var e2e = File.ReadAllText(Path.Combine(
            _repoRoot, "tests", "Rask.Examples.E2E.Tests", "PlaygroundExampleTests.cs"));

        var selectors = Regex.Matches(e2e, @"\.pg-ide\.([A-Za-z0-9_-]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

        // And it has to still be waiting on readiness at all: deleting the wait would "fix" the red by
        // no longer checking that the IDE ever comes up, which is the failure mode worth guarding.
        Assert.NotEmpty(selectors);

        foreach (var selector in selectors)
        {
            Assert.True(
                IdeBadgeState.All.Contains(selector),
                $"PlaygroundExampleTests waits for '.pg-ide.{selector}', which the readiness pill never "
                + $"renders. It renders one of: {string.Join(", ", IdeBadgeState.All)}. Either the badge "
                + "lost a state class (see IdeBadgeState) or the test is waiting for the wrong one — "
                + "which is #593, and it costs a three-minute timeout in the browser gate to discover.");
        }
    }

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

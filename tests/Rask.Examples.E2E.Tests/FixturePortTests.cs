using System.Text.RegularExpressions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     Guards the invariant #614 established: no fixture in this suite picks its own port.
/// </summary>
/// <remarks>
///     Every fixture used to declare one, kept unique by a comment naming its siblings' numbers. That is a
///     list a human maintains, and it drifted exactly as you'd expect — <c>WasmWatchAppFixture</c> and
///     <c>SiteWasmAppFixture</c> both held <c>5101</c> in different collections xUnit runs in parallel.
///     Uniqueness was also only ever per <em>run</em>: a second worktree mid-suite claimed the same numbers,
///     and <c>5099</c> is the port <c>.githooks/pre-push</c> gates on, so one straggler blocked pushing from
///     everywhere on the machine.
///     <para>
///         This is a source scan rather than a reflection check on purpose: the thing being prevented is
///         somebody writing the constant back, and by the time a port is reachable through a property it has
///         already been reserved.
///     </para>
/// </remarks>
public sealed class FixturePortTests
{
    // Any literal in the 5000-5999 range — the block the fixtures drew from, and the one an author reaching
    // for "a port that looks free" reaches into.
    private static readonly Regex Literal = new(@"\b5\d{3}\b", RegexOptions.Compiled);

    [Fact]
    public void No_fixture_hard_codes_a_port()
    {
        var infrastructure = Path.Combine(RepoRoot(), "tests", "Rask.Examples.E2E.Tests", "Infrastructure");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(infrastructure, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Comments are where the history lives ("this fixture used to hold 5101"), and keeping that
                // readable is worth more than a scan that cannot tell code from prose.
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Literal.IsMatch(line))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "E2E fixtures must take their port from LoopbackPort.Reserve(), not a literal — a number kept "
            + "unique by hand is only unique within one run of one checkout. Offending lines:\n  "
            + string.Join("\n  ", offenders));
    }

    private static string RepoRoot()
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

        throw new InvalidOperationException($"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}

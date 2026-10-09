using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract: a copy of the Server runtime that declines to run a document stands its host down
///     first, and the morph is told which <c>&lt;script&gt;</c> is the runtime's own.
/// </summary>
/// <remarks>
///     The shared modules bind their document listeners when the bundle is read, before the entry point can
///     decide anything. A copy that returned early without a word left those listeners asking a host nobody
///     installed, and <c>rask-host.ts</c> answers that by throwing: an uncaught error at every keystroke in a
///     bound field, for the rest of the document's life. The behaviour is proven in a browser by
///     <c>RuntimeRunsOnceTests</c> in <c>Rask.Server.E2E.Tests</c>. This is what keeps a NEW early return from
///     bringing it back.
/// </remarks>
public partial class RuntimeStandDownContractTests
{
    private static readonly string _runtime = File.ReadAllText(Path.Combine(LocateRepoRoot(), "src", "Rask.Server", "Resources", "rask.ts"));

    [Fact]
    public void Every_return_before_the_host_is_installed_stands_the_host_down_first()
    {
        var entry = _runtime[_runtime.IndexOf("(function () {", StringComparison.Ordinal)..];
        var beforeAnythingElse = entry[..entry.IndexOf("const devMode", StringComparison.Ordinal)];

        var returns = Return().Count(beforeAnythingElse);
        var stoodDown = StandDownThenReturn().Count(beforeAnythingElse);

        Assert.Equal(2, returns);
        Assert.Equal(returns, stoodDown);
    }

    [Fact]
    public void The_runtime_names_its_own_script_to_the_morph_so_that_a_navigation_does_not_run_it_again()
    {
        var installed = _runtime.IndexOf("setHost({send, inRoot});", StringComparison.Ordinal);

        var kept = _runtime.IndexOf("keepRuntimeScript(self);", installed, StringComparison.Ordinal);

        Assert.True(installed > 0, "rask.ts no longer installs its host the way this test reads it.");
        Assert.True(kept > installed, "rask.ts does not name its own <script> to the morph (keepRuntimeScript).");
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    [GeneratedRegex(@"\breturn\b")]
    private static partial Regex Return();

    [GeneratedRegex(@"standDown\(\);\s+return;")]
    private static partial Regex StandDownThenReturn();
}

using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The Node versions the templates state agree with the two the repository already pins.
/// </summary>
/// <remarks>
///     <para>
///         There are two numbers and they mean different things (see <see cref="NodeRequirement"/>).
///         <b>BuildFloor</b> is the lowest Node an already-scaffolded app builds on, enforced by
///         Rask.Spa.Hosting as RASKSPA005. <b>ScaffoldLine</b> is the current Active LTS, which is what
///         the container images install. A template may state either, and must contradict neither.
///     </para>
///     <para>
///         This found real drift the moment it was written: the Analog template's manifest declared
///         <c>node &gt;=20.19.1</c> — its creator's floor, carried in verbatim — while Rask's own build
///         refuses anything below 22.12. A scaffolded Analog app therefore advertised an engine that its
///         first build rejects. Nothing could see it before, because the manifest was fetched from
///         <c>create-analog@latest</c> at scaffold time and never existed in this repository.
///     </para>
///     <para>
///         Offline, like every other pin here: <c>lts-watch.yml</c> is the one thing that reaches
///         nodejs.org, monthly, and it opens an issue rather than failing a branch.
///     </para>
/// </remarks>
public sealed class TemplateNodePinTests
{
    private static readonly string Root =
        Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    [Fact]
    public void No_template_claims_an_engine_below_the_floor_its_own_build_enforces()
    {
        var offenders = new List<string>();

        foreach (var manifest in Directory.EnumerateFiles(Root, "package.json", SearchOption.AllDirectories))
        {
            // Parsed, not pattern-matched. The creators write '>' as the > escape, and a regex over
            // the raw text reads the "003" in that escape as the version — which is how this test first
            // reported every manifest as claiming Node 3.
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
            if (!document.RootElement.TryGetProperty("engines", out var engines)
                || !engines.TryGetProperty("node", out var node))
            {
                continue;
            }

            var match = Regex.Match(node.GetString() ?? "", @"(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!match.Success)
            {
                continue;
            }

            var stated = new Version(
                int.Parse(match.Groups[1].Value),
                match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0,
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);

            if (stated < NodeRequirement.BuildFloor)
            {
                offenders.Add(
                    $"{Path.GetRelativePath(Root, manifest).Replace(Path.DirectorySeparatorChar, '/')}"
                    + $" says node {stated}, below the {NodeRequirement.BuildFloor} the build enforces");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These templates advertise a Node their own first build would refuse (RASKSPA005):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_container_installs_the_Node_line_the_CLI_asks_for()
    {
        // The meta lane's image keeps node at RUNTIME — the front end has a server of its own — so the
        // major it installs is the one the app is actually run on. Left to drift, the image would
        // install a line the framework no longer tests against, and only in a container.
        var offenders = new List<string>();
        var expected = NodeRequirement.ScaffoldLine.Major;

        foreach (var dockerfile in Directory.EnumerateFiles(Root, "Dockerfile", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(dockerfile), @"setup_(\d+)\.x"))
            {
                var major = int.Parse(match.Groups[1].Value);
                if (major != expected)
                {
                    offenders.Add(
                        $"{Path.GetRelativePath(Root, dockerfile).Replace(Path.DirectorySeparatorChar, '/')}"
                        + $" installs Node {major}.x, not the {expected}.x line NodeRequirement states");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These container images install a different Node line from the one the CLI asks for:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_client_ships_a_lockfile()
    {
        // Without one, Dependabot can only act when a release falls OUTSIDE the declared range, so every
        // minor and patch update to a front-end dependency is invisible — which is most of them, and the
        // opposite of why these manifests are committed. It is also what lets the scaffolded app's build
        // use `npm ci` rather than resolving a different tree on every machine.
        var missing = Directory
            .EnumerateFiles(Root, "package.json", SearchOption.AllDirectories)
            .Where(m => !File.Exists(Path.Combine(Path.GetDirectoryName(m)!, "package-lock.json")))
            .Select(m => Path.GetRelativePath(Root, m).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These client manifests have no package-lock.json beside them:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void No_template_carries_another_package_manager_s_lockfile()
    {
        // create-solid writes a pnpm lockfile. Rask builds these with npm (the targets run `npm ci`),
        // and two lockfiles for two package managers is a tree that resolves differently depending on
        // who builds it.
        var strays = new[] { "pnpm-lock.yaml", "yarn.lock", "bun.lockb" }
            .SelectMany(name => Directory.EnumerateFiles(Root, name, SearchOption.AllDirectories))
            .Select(p => Path.GetRelativePath(Root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            strays.Length == 0,
            "These are lockfiles for a package manager this repository does not build with:\n  "
            + string.Join("\n  ", strays));
    }
}

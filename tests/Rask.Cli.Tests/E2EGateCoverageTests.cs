using Xunit;

namespace Rask.Cli.Tests;

/// <summary>
///     Every <c>*.E2E.Tests</c> project must be run by some gate script.
/// </summary>
/// <remarks>
///     <para>
///         This replaces <c>CliGateFilterTests</c>, which asked the same question of a world that no
///         longer exists. Back then every end-to-end class lived inside <c>Rask.Cli.Tests</c> and was
///         picked out by a <c>--filter</c> on its class name, so the failure to guard against was a
///         class no filter happened to match: it simply never ran, while the gate printed that it
///         passed. That had already cost this repo once — the CLI gate filtered on
///         <c>~BuildE2ETests</c>, and <c>TailwindPublishE2ETests</c>, written specifically to catch a
///         stylesheet that never reached the publish output, matched none of them and was executed by
///         nothing at all.
///     </para>
///     <para>
///         End-to-end tests now live in their own <c>*.E2E.Tests</c> projects and each gate names a
///         PROJECT, so a filter can no longer miss a class — the boundary is structural. The failure
///         that replaces it is one level up and just as quiet: adding a new <c>*.E2E.Tests</c> project
///         that no script under <c>scripts/</c> ever runs. The suite would compile, be excluded from
///         the unit gate by the <c>.E2E.Tests.</c> exclusion, and be run by nothing.
///     </para>
///     <para>
///         It lives HERE, in a unit-test project, and that is deliberate. Putting it inside an
///         <c>*.E2E.Tests</c> project would exclude it from the unit gate by the very rule it checks,
///         and leave it to be run by the gate whose absence it exists to detect.
///     </para>
/// </remarks>
public sealed class E2EGateCoverageTests
{
    [Fact]
    public void Every_e2e_project_is_run_by_some_gate_script()
    {
        var root = CliBuildE2E.FindRepoRoot();

        var e2eProjects = Directory
            .GetDirectories(Path.Combine(root, "tests"), "*.E2E.Tests")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // If this ever finds none, the naming convention moved and this test went vacuous with it.
        Assert.NotEmpty(e2eProjects);

        var gateText = string.Concat(
            Directory.GetFiles(Path.Combine(root, "scripts"), "run-*.sh").Select(File.ReadAllText));

        // The .csproj PATH, not the bare project name. A name matches a comment, and a comment naming a
        // gate is not evidence that the gate exists — this repo has already been caught by exactly that.
        // The path appears where a script actually invokes the project and essentially nowhere else.
        var orphaned = e2eProjects
            .Where(name => !gateText.Contains($"tests/{name}/{name}.csproj", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            orphaned.Length == 0,
            $"no gate script under scripts/ runs: {string.Join(", ", orphaned)}. The unit gate excludes "
            + "every *.E2E.Tests project by design, so a suite no script names is run by nothing at all "
            + "while every gate keeps reporting success. Add it to the script that owns its area.");
    }
}

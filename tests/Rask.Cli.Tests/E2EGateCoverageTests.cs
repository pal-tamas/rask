using System.Text.RegularExpressions;

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
public sealed partial class E2EGateCoverageTests
{
    [Fact]
    public void Every_e2e_project_is_run_by_some_gate_script()
    {
        var root = CliBuildE2E.FindRepoRoot();

        // A PROJECT, not merely a directory whose name ends that way. Deleting a suite leaves its bin/
        // and obj/ behind — they are ignored, so nothing sweeps them — and matching on the name alone
        // then reports a phantom for ever: the suite this named had been gone for months, and the
        // failure surfaced only when an unrelated change first pulled this project into the gate's
        // scope. A gate that fails for a reason that cannot be fixed by adding the thing it asks for is
        // worse than one that misses.
        var e2eProjects = Directory
            .GetDirectories(Path.Combine(root, "tests"), "*.E2E.Tests")
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Where(name => File.Exists(Path.Combine(root, "tests", name, name + ".csproj")))
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

    /// <summary>
    ///     Every class a gate script filters on is declared in the project that script runs.
    /// </summary>
    /// <remarks>
    ///     The test above proves a project is NAMED by some script, and three gates satisfied it while running
    ///     nothing (#1054): the watch, deploy and Linux dev-host scripts built and tested <c>Rask.Cli.Tests</c>
    ///     with a filter on a class that had moved to <c>Rask.Cli.E2E.Tests</c>. A VSTest filter that matches
    ///     nothing exits 0, so each printed that it passed. Here a filter must find something to run in the project
    ///     beside it, which is the one fact a green exit code cannot vouch for.
    /// </remarks>
    [Fact]
    public void Every_gate_filter_names_a_type_in_the_project_it_tests()
    {
        var root = CliBuildE2E.FindRepoRoot();
        var checkedPairs = 0;
        var missing = new List<string>();

        foreach (var script in Directory.GetFiles(Path.Combine(root, "scripts"), "run-*.sh"))
        {
            // One logical command per line: a backslash continuation is how every gate spreads its dotnet test.
            var text = File.ReadAllText(script).Replace("\\\n", " ", StringComparison.Ordinal);
            foreach (Match command in TestCommand().Matches(text))
            {
                if (FilterArgument().Match(command.Groups["rest"].Value) is not { Success: true } filter)
                {
                    continue;
                }

                var project = command.Groups["project"].Value;
                var projectDirectory = Path.Combine(root, Path.GetDirectoryName(project)!);
                var declared = DeclaredNames(projectDirectory);

                foreach (Match token in PositiveToken().Matches(filter.Groups["filter"].Value))
                {
                    checkedPairs++;
                    var name = token.Groups["name"].Value;
                    if (!declared.Any(d => d.Contains(name, StringComparison.Ordinal)))
                    {
                        missing.Add($"{Path.GetFileName(script)}: {project} has no type or namespace matching '{name}'");
                    }
                }
            }
        }

        // Would go vacuous if the scripts stopped spelling their test runs this way.
        Assert.True(checkedPairs > 0, "no literal `dotnet test <project> --filter \"FullyQualifiedName~...\"` found under scripts/");
        Assert.True(
            missing.Count == 0,
            "These gate filters match nothing in the project they run, so the gate runs zero tests and still exits 0:\n  "
            + string.Join("\n  ", missing));
    }

    private static HashSet<string> DeclaredNames(string projectDirectory)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(projectDirectory, file);
            if (relative.StartsWith("bin", StringComparison.Ordinal) || relative.StartsWith("obj", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match declaration in Declaration().Matches(File.ReadAllText(file)))
            {
                names.Add(declaration.Groups["name"].Value);
            }
        }

        return names;
    }

    [GeneratedRegex(@"dotnet test\s+(?<project>tests/\S+\.csproj)(?<rest>[^\n]*)")]
    private static partial Regex TestCommand();

    [GeneratedRegex(@"--filter\s+""(?<filter>[^""]*)""")]
    private static partial Regex FilterArgument();

    // `~` only: a `!~` names what to leave out, which may legitimately match nothing.
    [GeneratedRegex(@"(?<!!)FullyQualifiedName~(?<name>[A-Za-z0-9_.]+)")]
    private static partial Regex PositiveToken();

    [GeneratedRegex(@"\b(?:class|record|struct|namespace)\s+(?<name>[A-Za-z0-9_.]+)")]
    private static partial Regex Declaration();
}

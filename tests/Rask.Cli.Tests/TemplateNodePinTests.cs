using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rask.Cli.Tests;

/// <summary>
///     The Node versions the templates state agree with the two the repository pins (see
///     <see cref="NodeRequirement"/>): the floor a build enforces, and the LTS line an image installs.
/// </summary>
public sealed class TemplateNodePinTests
{
    private static readonly string Root =
        Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    private static string Relative(string path) =>
        Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');

    // Parsed, not pattern-matched: a creator writes '>' as an escape, and a regex over the raw text reads its digits.
    [Fact]
    public void No_template_claims_an_engine_below_the_floor_its_own_build_enforces()
    {
        var offenders = new List<string>();

        foreach (var manifest in Directory.EnumerateFiles(Root, "package.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            if (document.RootElement.TryGetProperty("engines", out var engines)
                && engines.TryGetProperty("node", out var node)
                && Regex.Match(node.GetString() ?? "", @"\d+(?:\.\d+){0,2}") is { Success: true } match
                && NodeRequirement.Parse(match.Value) is { } stated
                && stated < NodeRequirement.BuildFloor)
            {
                offenders.Add($"{Relative(manifest)} says node {stated}, below the {NodeRequirement.BuildFloor} the build enforces");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These templates advertise a Node their own first build would refuse (RASKSPA005):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_container_installs_the_Node_line_the_installers_do()
    {
        var expected = NodeRequirement.ScaffoldLine.Major;

        var offenders = Directory.EnumerateFiles(Root, "Dockerfile", SearchOption.AllDirectories)
            .SelectMany(dockerfile => Regex.Matches(File.ReadAllText(dockerfile), @"setup_(\d+)\.x")
                .Select(match => (File: Relative(dockerfile), Major: int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))))
            .Where(image => image.Major != expected)
            .Select(image => $"{image.File} installs Node {image.Major}.x, not the {expected}.x line NodeRequirement states")
            .ToArray();

        Assert.True(offenders.Length == 0, string.Join("\n  ", offenders));
    }

    // Angular's CLI enforces a Node range of its own, above the build's floor. The template's csproj raises
    // RASKSPA005's bar to that range's lowest version, so a lockfile bump that moves the range must move the bar.
    [Fact]
    public void The_Angular_template_refuses_a_Node_below_the_one_its_own_cli_accepts()
    {
        using var lockfile = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "angular", "client", "package-lock.json")));
        var csproj = File.ReadAllText(Path.Combine(Root, "angular", "Company.RaskServer.csproj"));

        var range = lockfile.RootElement.GetProperty("packages").GetProperty("node_modules/@angular/cli")
            .GetProperty("engines").GetProperty("node").GetString() ?? "";
        var lowest = Regex.Matches(range, @"\d+\.\d+\.\d+").Select(arm => Version.Parse(arm.Value)).Min();
        var stated = Regex.Match(csproj, "<RaskSpaMinimumNode>([0-9.]+)</RaskSpaMinimumNode>");

        Assert.True(stated.Success, "the Angular template's csproj no longer sets RaskSpaMinimumNode.");
        Assert.True(
            lowest == Version.Parse(stated.Groups[1].Value),
            $"@angular/cli asks for Node '{range}', and the template's RaskSpaMinimumNode is {stated.Groups[1].Value}.");
    }

    // The lockfile is what lets the scaffolded app's build run `npm ci` and resolve the same tree on every machine.
    [Fact]
    public void Every_client_ships_a_lockfile()
    {
        var missing = Directory
            .EnumerateFiles(Root, "package.json", SearchOption.AllDirectories)
            .Where(m => !File.Exists(Path.Combine(Path.GetDirectoryName(m)!, "package-lock.json")))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These client manifests have no package-lock.json beside them:\n  " + string.Join("\n  ", missing));
    }

    // Rask builds these with npm, and a second manager's lockfile is a tree that resolves differently by who builds it.
    [Fact]
    public void No_template_carries_another_package_manager_s_lockfile()
    {
        var strays = new[] { "pnpm-lock.yaml", "yarn.lock", "bun.lockb" }
            .SelectMany(name => Directory.EnumerateFiles(Root, name, SearchOption.AllDirectories))
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            strays.Length == 0,
            "These are lockfiles for a package manager this repository does not build with:\n  "
            + string.Join("\n  ", strays));
    }
}

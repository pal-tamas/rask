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

    // The floor is the Node LTS line; a template that sets a bar of its own may only raise it.
    [Fact]
    public void No_template_lowers_the_Node_floor()
    {
        var lowered = Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Select(csproj => (File: Relative(csproj), Bar: Regex.Match(File.ReadAllText(csproj), "<RaskSpaMinimumNode>([0-9.]+)</RaskSpaMinimumNode>")))
            .Where(t => t.Bar.Success && Version.Parse(t.Bar.Groups[1].Value) < NodeRequirement.BuildFloor)
            .Select(t => $"{t.File}: RaskSpaMinimumNode {t.Bar.Groups[1].Value} is below {NodeRequirement.BuildFloor}")
            .ToArray();

        Assert.True(lowered.Length == 0, string.Join("\n  ", lowered));
    }

    // `rask new` chooses the client's versions (FrontEndVersions) and the first build writes the app's own lockfile:
    // a committed one is a tree that goes stale the day it lands, and a pull request a week to move it.
    [Fact]
    public void No_client_ships_a_lockfile()
    {
        var shipped = Directory
            .EnumerateFiles(Root, "package-lock.json", SearchOption.AllDirectories)
            .Select(Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(shipped.Length == 0, "These templates ship a lockfile:\n  " + string.Join("\n  ", shipped));
    }

    // A version in a template is a hold, and only these have one: Tailwind is the binary the C# hosts download
    // (TailwindVersionPinTests), @types/node is the Node LTS line. Everything else floats to the newest that installs.
    [Fact]
    public void Every_template_package_floats_except_the_declared_holds()
    {
        string[] holds = ["tailwindcss", "@tailwindcss/vite", "@tailwindcss/postcss", "@types/node"];
        var manifests = Directory.EnumerateFiles(Root, "package.json", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Root, "island.json", SearchOption.AllDirectories));

        var pinned = manifests
            .SelectMany(manifest => Dependencies(manifest).Select(d => (File: Relative(manifest), d.Name, d.Version)))
            .Where(d => !holds.Contains(d.Name, StringComparer.Ordinal) && d.Version != "latest")
            .Select(d => $"{d.File}: {d.Name} {d.Version}")
            .ToArray();

        Assert.True(pinned.Length == 0, "These template packages name a version instead of \"latest\":\n  " + string.Join("\n  ", pinned));
    }

    [Fact]
    public void Types_node_follows_the_Node_LTS_line()
    {
        var expected = $"^{NodeRequirement.ScaffoldLine.Major.ToString(CultureInfo.InvariantCulture)}.";

        var off = Directory.EnumerateFiles(Root, "package.json", SearchOption.AllDirectories)
            .SelectMany(manifest => Dependencies(manifest).Select(d => (File: Relative(manifest), d.Name, d.Version)))
            .Where(d => d.Name == "@types/node" && !d.Version.StartsWith(expected, StringComparison.Ordinal))
            .Select(d => $"{d.File}: @types/node {d.Version}")
            .ToArray();

        Assert.True(off.Length == 0, $"@types/node is not on the {expected}x line NodeRequirement states:\n  " + string.Join("\n  ", off));
    }

    private static IEnumerable<(string Name, string Version)> Dependencies(string manifest)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        return
        [
            .. new[] { "dependencies", "devDependencies" }
                .Where(section => document.RootElement.TryGetProperty(section, out _))
                .SelectMany(section => document.RootElement.GetProperty(section).EnumerateObject())
                .Select(p => (p.Name, p.Value.GetString() ?? "")),
        ];
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

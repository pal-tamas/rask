using System.Text.Json;
using Rask.Cli.Scaffolding;
using Semver;

namespace Rask.Cli.Tests;

// `rask new` writes the newest front-end versions that install together, not the bare latest of each: the day vite
// ships a major its plugins still ask for the last one, and npm refuses the pair.
public sealed class NpmCompatibleVersionsTests
{
    private static readonly SemVersion Node = new(22, 12, 0);

    [Fact]
    public void A_new_vite_major_waits_for_the_plugin_that_does_not_accept_it_yet()
    {
        var releases = Releases(
            ("vite", [Release("9.0.0"), Release("8.4.1"), Release("8.3.0")]),
            ("@vitejs/plugin-react", [Release("6.2.0", ("vite", "^8.0.0"))]));

        var resolution = NpmCompatibleVersions.Choose(releases, Node);

        Assert.Null(resolution.Conflict);
        Assert.Equal("8.4.1", resolution.Versions["vite"].ToString());
        Assert.Equal("6.2.0", resolution.Versions["@vitejs/plugin-react"].ToString());
    }

    [Fact]
    public void Once_the_plugin_accepts_the_new_major_both_move_up()
    {
        var releases = Releases(
            ("vite", [Release("9.0.2"), Release("8.4.1")]),
            ("@vitejs/plugin-react", [Release("7.0.0", ("vite", "^9.0.0")), Release("6.2.0", ("vite", "^8.0.0"))]));

        var resolution = NpmCompatibleVersions.Choose(releases, Node);

        Assert.Equal("9.0.2", resolution.Versions["vite"].ToString());
        Assert.Equal("7.0.0", resolution.Versions["@vitejs/plugin-react"].ToString());
    }

    [Fact]
    public void A_plugin_that_already_wants_the_new_major_steps_down_for_one_that_does_not()
    {
        var releases = Releases(
            ("vite", [Release("9.0.0"), Release("8.4.1")]),
            ("@vitejs/plugin-vue", [Release("7.0.0", ("vite", "^9.0.0")), Release("6.0.8", ("vite", "^8.0.0"))]),
            ("vite-plugin-solid", [Release("2.11.14", ("vite", "^8.0.0"))]));

        var resolution = NpmCompatibleVersions.Choose(releases, Node);

        Assert.Equal("8.4.1", resolution.Versions["vite"].ToString());
        Assert.Equal("6.0.8", resolution.Versions["@vitejs/plugin-vue"].ToString());
    }

    [Fact]
    public void A_family_that_peers_on_its_own_exact_version_lands_on_one_version()
    {
        var releases = Releases(
            ("@angular/core", [Release("22.2.0"), Release("22.1.6")]),
            ("@angular/router", [Release("22.2.0", ("@angular/core", "22.2.0")), Release("22.1.6", ("@angular/core", "22.1.6"))]),
            ("@angular/forms", [Release("22.1.6", ("@angular/core", "22.1.6"))]));

        var resolution = NpmCompatibleVersions.Choose(releases, Node);

        Assert.Null(resolution.Conflict);
        Assert.All(resolution.Versions.Values, v => Assert.Equal("22.1.6", v.ToString()));
    }

    [Fact]
    public void A_release_that_refuses_the_Node_the_app_builds_on_is_never_chosen()
    {
        var releases = Releases(("vite", [Release("8.5.0", node: ">=24"), Release("8.4.1", node: "^20.19.0 || >=22.12.0")]));

        var resolution = NpmCompatibleVersions.Choose(releases, Node);

        Assert.Equal("8.4.1", resolution.Versions["vite"].ToString());
    }

    [Fact]
    public void Peers_that_no_release_can_satisfy_are_a_conflict_naming_them()
    {
        var releases = Releases(
            ("typescript", [Release("6.0.3")]),
            ("vue-tsc", [Release("3.3.11", ("typescript", ">=7.0.0"))]));

        var resolution = NpmCompatibleVersions.Choose(releases, Node);

        Assert.NotNull(resolution.Conflict);
        Assert.Contains("typescript", resolution.Conflict, StringComparison.Ordinal);
        Assert.Contains("vue-tsc@3.3.11", resolution.Conflict, StringComparison.Ordinal);
    }

    [Fact]
    public void A_registry_document_yields_its_stable_undeprecated_releases_newest_first()
    {
        using var document = JsonDocument.Parse("""
            {"versions": {
              "8.3.0": {"peerDependencies": {"sass": "*"}, "engines": {"node": ">=22.12.0"}},
              "9.0.0-beta.1": {},
              "8.4.0": {"deprecated": "broken build, use 8.4.1"},
              "8.4.1": {}
            }}
            """);

        var releases = NpmRegistry.Installable(document.RootElement);

        Assert.NotNull(releases);
        Assert.Equal(["8.4.1", "8.3.0"], releases.Select(r => r.Version.ToString()));
        Assert.Equal("*", releases[1].Peers["sass"]);
        Assert.Equal(">=22.12.0", releases[1].Node);
    }

    [Theory]
    [InlineData("""{ "version": "1.3.0", "bundledVersions": { "vite": "8.3.4", "rolldown": "1.2.14" } }""", "8.3.4")]
    [InlineData("""{ "version": "1.3.0" }""", null)]
    [InlineData("""{ "version": "1.3.0", "bundledVersions": { "vite": "8.x" } }""", null)]
    public void The_vite_a_vite_plus_core_bundles_is_read_from_its_version_document(string json, string? expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, NpmRegistry.BundledVite(document.RootElement)?.ToString());
    }

    internal static NpmRelease Release(string version, params (string Package, string Range)[] peers) =>
        new(SemVersion.Parse(version, SemVersionStyles.Strict), peers.ToDictionary(p => p.Package, p => p.Range, StringComparer.Ordinal));

    private static NpmRelease Release(string version, string node) =>
        new(SemVersion.Parse(version, SemVersionStyles.Strict), new Dictionary<string, string>(StringComparer.Ordinal), node);

    private static Dictionary<string, IReadOnlyList<NpmRelease>> Releases(params (string Package, NpmRelease[] Releases)[] packages) =>
        packages.ToDictionary(p => p.Package, p => (IReadOnlyList<NpmRelease>)p.Releases, StringComparer.Ordinal);
}

using System.Reflection;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Every package a template can reference must be in <see cref="CliBuildE2E.FeedPackages" />.
/// </summary>
/// <remarks>
///     <para>
///         The build gates pack <see cref="CliBuildE2E.FeedPackages" /> to a local feed and restore the
///         scaffolded projects against it. A package missing from that list therefore cannot be restored,
///         which means no case exercising it can exist — and nothing anywhere says so. The gate simply
///         covers less than it appears to, and the shortfall is invisible in a green run.
///     </para>
///     <para>
///         Not hypothetical: <c>Rask.Tailwind</c> was absent, so <c>--tailwind</c> — the styling option
///         that adds an MSBuild step and a downloaded binary, i.e. the one most in need of a real build —
///         had no way to be proven to compile at all. Anyone writing that case would have hit NU1101 and
///         had to discover why.
///     </para>
///     <para>
///         This runs unconditionally rather than behind <c>RASK_CLI_BUILD_E2E</c>: it asserts on strings,
///         needs no feed, and the thing it guards is the coverage of a gate that is itself opt-in. A check
///         on an opt-in gate must not be opt-in too, or the same hole reopens one level up.
///     </para>
/// </remarks>
public sealed class FeedCoverageTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    /// <summary>
    ///     Every styling option, on every template that has one. Styling is the axis that decides which
    ///     package the project references, so covering it is the point.
    /// </summary>
    /// <remarks>
    ///     Parameterised by template key only, with the stylings looped inside: <c>Styling</c> is internal,
    ///     and a public xUnit theory parameter cannot expose it (CS0051).
    /// </remarks>
    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public void Every_package_a_template_references_can_be_restored_from_the_local_feed(string template)
    {
        foreach (var styling in Enum.GetValues<Styling>())
        {
            var batteries = new ServerBatteries { Styling = styling };

            var result = template switch
            {
                "wasm" => ProjectGenerator.GenerateWasm(
                    Root, "App", auth: false, pwa: false, docker: false, Version, batteries),
                _ => ProjectGenerator.GenerateServer(Root, "App", batteries, Version),
            };

            AssertFeedCovers(result, $"template '{template}' with {styling} styling");
        }
    }

    /// <summary>
    ///     The batteries, all at once. Each pillar adds its own package, and <c>--all-batteries</c> is the
    ///     combination a user reaches for most readily.
    /// </summary>
    [Fact]
    public void Every_package_the_batteries_reference_can_be_restored_from_the_local_feed()
    {
        var batteries = new ServerBatteries
        {
            Auth = true,
            Pwa = true,
            Cqrs = true,
            Data = true,
            Docker = true,
            Jobs = true,
            Mail = true,
            Cache = true,
            Outbox = true,
            Push = true,
            Snapshots = true,
            Logs = true,
            Ops = true,
            Wasm = true,
            Localization = true,
            CultureList = "en",
        };

        // The set above is hand-written, and a hand-written set of everything is exactly what goes
        // stale — which is the failure this whole file exists to catch, one level up. So it checks
        // itself: every flag `rask new` understands must be switched on here, or a new battery gets
        // added, references a package nobody packs, and every build gate keeps passing without ever
        // restoring it.
        var uncovered = NewCommand.FeatureFlags
            .Where(flag => typeof(ServerBatteries)
                .GetProperty(flag, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?.GetValue(batteries) is not true)
            .ToArray();

        Assert.True(
            uncovered.Length == 0,
            $"rask new understands --{string.Join(", --", uncovered)}, which this test leaves off. A "
            + "battery that is never switched on here can pull in a package the local feed does not "
            + "pack, and nothing would say so.");

        AssertFeedCovers(
            ProjectGenerator.GenerateServer(Root, "App", batteries, Version), "the server template with every battery");
    }

    /// <summary>Every front-end template, since each contributes the same host-side packages.</summary>
    [Fact]
    public void Every_package_a_front_end_template_references_can_be_restored_from_the_local_feed()
    {
        foreach (var framework in SpaFramework.All)
        {
            AssertFeedCovers(
                ProjectGenerator.GenerateSpa(Root, "App", framework, new ServerBatteries(), Version),
                $"the {framework.Key} template");
        }
    }

    private static void AssertFeedCovers(ScaffoldResult result, string what)
    {
        var feed = new HashSet<string>(CliBuildE2E.FeedPackages, StringComparer.Ordinal);
        var missing = result.Packages.Where(package => !feed.Contains(package)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"{what} references {string.Join(", ", missing)}, which CliBuildE2E.FeedPackages does not pack. "
            + "The build gates restore from that feed, so no case covering this can run — it would fail "
            + "with NU1101, and until someone writes one the gate silently proves less than it looks like "
            + "it does. Add the package to CliBuildE2E.FeedPackages.");
    }
}

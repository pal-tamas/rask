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
    ///     Every template, in its only configuration. This used to loop the styling axis, which was the
    ///     thing that decided which package a project referenced; there is no axis now, and Tailwind is
    ///     not a package a project references at all — it ships inside the host package, so it is
    ///     deliberately absent from the feed.
    /// </summary>
    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void Every_package_a_template_references_can_be_restored_from_the_local_feed(string template)
    {
        var batteries = new ServerBatteries();

        var result = template switch
        {
            "wasm" => ProjectGenerator.GenerateWasm(
                Root, "App", auth: false, pwa: false, docker: false, Version, batteries),
            _ => ProjectGenerator.GenerateServer(Root, "App", batteries, Version),
        };

        AssertFeedCovers(result, $"template '{template}'");
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

    /// <summary>Every meta framework template, which swaps one host package for another.</summary>
    [Fact]
    public void Every_package_a_meta_template_references_can_be_restored_from_the_local_feed()
    {
        foreach (var framework in MetaTemplate.All)
        {
            AssertFeedCovers(
                ProjectGenerator.GenerateMeta(Root, "App", framework, new ServerBatteries(), Version),
                $"the {framework.Key} template");
        }
    }

    [Fact]
    public void Browser_only_packages_are_in_the_local_feed_too()
    {
        // RaskBrowserPackageReference is NOT a PackageReference, so it never reaches
        // ScaffoldResult.Packages and AssertFeedCovers cannot see it. That makes the coverage guard
        // blind to exactly the references the one-project build adds for the bundle — the class of gap
        // where a missing package means no build case can exist rather than one that fails.
        //
        // Asserted by name because there is only one today. A second would be the moment to make the
        // generator report them instead.
        var feed = new HashSet<string>(CliBuildE2E.FeedPackages, StringComparer.Ordinal);

        Assert.True(
            feed.Contains("Rask.Cqrs.Client"),
            "Rask.Cqrs.Client is declared as a browser-only reference by `rask new --wasm --cqrs`, but "
            + "CliBuildE2E.FeedPackages does not pack it — so the browser companion could not restore "
            + "and no build gate covering it can exist.");
    }

    [Fact]
    public void The_feed_is_closed_over_the_packages_its_own_packages_depend_on()
    {
        // The guards above ask what a TEMPLATE references. NuGet asks for more than that: restoring a
        // package also restores its dependencies, and a scaffolded project never names those. So a feed
        // that covers every direct reference can still fail to restore — which is exactly what happened
        // when Rask.Dashboard grew a dependency on Rask.Ui. Every template still referenced only
        // packages the feed packed, every assertion above stayed green, and the gate died on NU1101 for
        // a package no template mentions.
        //
        // Read off the project files rather than a second hand-written list, because a hand-written
        // list of everything is the thing this file exists to catch going stale.
        var root = CliBuildE2E.FindRepoRoot();
        var feed = new HashSet<string>(CliBuildE2E.FeedPackages, StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var package in CliBuildE2E.FeedPackages)
        {
            var csproj = Path.Combine(root, "src", package, package + ".csproj");
            if (!File.Exists(csproj))
            {
                continue;
            }

            foreach (var dependency in PackableProjectDependencies(root, csproj))
            {
                if (!feed.Contains(dependency))
                {
                    missing.Add($"{dependency} (a dependency of {package})");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"The local feed does not pack {string.Join(", ", missing)}. NuGet restores a package's "
            + "dependencies as well as the package, so a scaffolded project that never names these still "
            + "cannot restore without them — it fails with NU1101, and no build gate can run. Add them to "
            + "CliBuildE2E.FeedPackages.");
    }

    /// <summary>
    ///     The packable projects a project reference-depends on, i.e. the ones that become real nuspec
    ///     dependencies. <c>PrivateAssets="all"</c> references are excluded because they deliberately do
    ///     not: that is how the framework keeps un-packable projects like Rask.Core out of a nuspec.
    /// </summary>
    private static IEnumerable<string> PackableProjectDependencies(string root, string csproj)
    {
        foreach (var line in File.ReadLines(csproj))
        {
            if (!line.Contains("<ProjectReference", StringComparison.Ordinal)
                || line.Contains("PrivateAssets=\"all\"", StringComparison.Ordinal)
                || line.Contains("OutputItemType=\"Analyzer\"", StringComparison.Ordinal))
            {
                continue;
            }

            var start = line.IndexOf("Include=\"", StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += "Include=\"".Length;
            var end = line.IndexOf('"', start);
            if (end < 0)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(line[start..end].Replace('\\', Path.DirectorySeparatorChar));
            var referenced = Path.Combine(root, "src", name, name + ".csproj");

            if (File.Exists(referenced)
                && File.ReadAllText(referenced).Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
            {
                yield return name;
            }
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

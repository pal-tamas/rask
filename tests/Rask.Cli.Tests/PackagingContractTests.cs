using System.Xml.Linq;

namespace Rask.Cli.Tests;

/// <summary>
///     Structural contract for the build integration that every <c>rask new</c> app depends on.
///     <para>
///         <b>Why this lives here, and why it is structural.</b> <c>src/Rask.Core/build/Rask.Core.targets</c>
///         is the only thing that registers scoped <c>.css</c>/<c>.js</c> siblings as
///         <c>@(AdditionalFiles)</c> — and both scoped-asset generators read <i>only</i>
///         <c>AdditionalTextsProvider</c>. For a year it reached no NuGet consumer at all: Rask.Core is
///         <c>IsPackable=false</c>, so its own <c>Pack="true"</c> item was inert, and the host packages
///         packed only their own <c>build/</c> folder. Scoped CSS and JS silently did nothing in every
///         scaffolded app, and RASK015/RASK017 could never fire (#544).
///     </para>
///     <para>
///         Nothing caught it because every in-repo project imports the file directly via
///         <c>Directory.Build.targets</c>, so samples, tests and E2E are all immune — the defect existed
///         only on the far side of a <c>dotnet pack</c>. These assertions are cheap and run in the
///         <i>default</i> gate, which is the point: the behavioural proof lives in
///         <see cref="ProjectGeneratorBuildE2ETests" /> behind <c>RASK_CLI_BUILD_E2E=1</c>, and a guard
///         that only runs opt-in is a guard that runs approximately never.
///     </para>
///     <para>
///         <b>Honest limitation.</b> This <i>models</i> two NuGet rules rather than observing them —
///         that <c>PackagePath="build\"</c> lands at <c>build/</c>, and that <c>build/&lt;PackageId&gt;</c>
///         is the only file NuGet auto-imports. Both are stable and documented; the build-E2E observes
///         them for real by compiling a scaffolded app against a packed feed.
///     </para>
/// </summary>
public sealed class PackagingContractTests
{
    private static readonly string _repoRoot = CliBuildE2E.FindRepoRoot();

    /// <summary>The packages that ship the generators, and so must also ship what feeds them.</summary>
    public static TheoryData<string> HostPackages() => new() { "Rask.Server", "Rask.Wasm", "Rask.Native" };

    [Theory]
    [MemberData(nameof(HostPackages))]
    public void Each_host_package_has_the_entry_point_NuGet_will_auto_import(string package)
    {
        // NuGet auto-imports build/<PackageId>.{props,targets} and nothing else. A packed
        // build/Rask.Core.targets with no <PackageId>.targets to import it is dead bytes.
        var entry = Path.Combine(_repoRoot, "src", package, "build", $"{package}.targets");

        Assert.True(File.Exists(entry), $"{package} has no build/{package}.targets, so NuGet will never import its build integration.");
    }

    [Theory]
    [MemberData(nameof(HostPackages))]
    public void The_entry_point_imports_the_shared_core_build_integration(string package)
    {
        var entry = XDocument.Load(Path.Combine(_repoRoot, "src", package, "build", $"{package}.targets"));

        var import = entry.Descendants("Import")
            .SingleOrDefault(e => (e.Attribute("Project")?.Value ?? string.Empty)
                .EndsWith("Rask.Core.targets", StringComparison.Ordinal));

        Assert.True(import is not null, $"build/{package}.targets does not import Rask.Core.targets.");

        // The guard is load-bearing, not cosmetic: in the source tree there is no Rask.Core.targets
        // sibling (it is packed from Rask.Core's own build/ folder), and for Rask.Wasm — which
        // Directory.Build.targets imports in-repo — an unguarded import of a missing file is MSB4019
        // across every project in the repo.
        Assert.Contains("Exists(", import!.Attribute("Condition")?.Value ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HostPackages))]
    public void Each_host_package_packs_the_shared_core_build_integration(string package)
    {
        var csproj = XDocument.Load(Path.Combine(_repoRoot, "src", package, $"{package}.csproj"));

        Assert.Contains(
            csproj.Descendants("Import"),
            e => (e.Attribute("Project")?.Value ?? string.Empty).EndsWith("RaskCoreBuildPack.targets", StringComparison.Ordinal));

        // build/** is what carries the <PackageId>.targets entry point into the package.
        Assert.Contains(
            csproj.Descendants("None"),
            e => (e.Attribute("Include")?.Value ?? string.Empty).StartsWith("build\\", StringComparison.Ordinal)
                 && e.Attribute("Pack")?.Value == "true");
    }

    [Fact]
    public void The_shared_pack_fragment_ships_core_targets_into_the_build_folder()
    {
        var pack = XDocument.Load(Path.Combine(_repoRoot, "src", "RaskCoreBuildPack.targets"));

        var item = pack.Descendants("None")
            .SingleOrDefault(e => (e.Attribute("Include")?.Value ?? string.Empty)
                .EndsWith(@"Rask.Core\build\Rask.Core.targets", StringComparison.Ordinal));

        Assert.True(item is not null, "RaskCoreBuildPack.targets does not pack Rask.Core.targets.");
        Assert.Equal("true", item!.Attribute("Pack")?.Value);
        Assert.Equal(@"build\", item.Attribute("PackagePath")?.Value);
    }

    [Fact]
    public void Rask_Core_does_not_pretend_to_pack_its_own_build_integration()
    {
        // IsPackable=false makes any Pack="true" item here inert. Re-adding one would restore exactly
        // the false signal that hid #544 for a year — it looks like the file ships, and it does not.
        var csproj = XDocument.Load(Path.Combine(_repoRoot, "src", "Rask.Core", "Rask.Core.csproj"));

        Assert.Equal("false", csproj.Descendants("IsPackable").Single().Value);
        Assert.DoesNotContain(csproj.Descendants("None"), e => e.Attribute("Pack")?.Value == "true");
    }

    [Fact]
    public void The_core_build_integration_still_feeds_the_scoped_asset_generators()
    {
        // The other arm: someone renames or drops the globs and every assertion above stays green
        // while consumers silently lose the feature again. Both generators read only AdditionalFiles.
        var targets = XDocument.Load(Path.Combine(_repoRoot, "src", "Rask.Core", "build", "Rask.Core.targets"));

        var globs = targets.Descendants("AdditionalFiles")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains(@"**\*.css", globs);
        Assert.Contains(@"**\*.js", globs);

        var visible = targets.Descendants("CompilerVisibleProperty")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains("RaskGlobalUsings", visible);
        Assert.Contains("RaskScopedJsAutoInclude", visible);
        Assert.Contains("RaskFactoryNavigation", visible);
    }
}

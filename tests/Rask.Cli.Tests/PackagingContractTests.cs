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
    public static TheoryData<string> HostPackages() => new() { "Rask.Server", "Rask.Wasm" };

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

    /// <summary>
    ///     Every MSBuild task assembly a packed <c>.targets</c> will try to load, paired with the package
    ///     that has to ship it.
    /// </summary>
    /// <remarks>
    ///     Derived from the <c>UsingTask</c> that loads it rather than hand-listed, so a package that
    ///     grows a task is covered the day it does. Hand-kept lists are how the neighbouring gates rotted
    ///     — <c>Rask.Tailwind</c> was absent from <c>CliBuildE2E.FeedPackages</c> from the day it shipped,
    ///     and it is one of the three packages this finds.
    /// </remarks>
    public static TheoryData<string, string> PackedTaskAssemblies()
    {
        const string here = "$(MSBuildThisFileDirectory)";
        var data = new TheoryData<string, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(_repoRoot, "src"))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var package = Path.GetFileName(dir);
            var targets = Path.Combine(dir, "build", $"{package}.targets");

            if (!File.Exists(targets) || !File.Exists(Path.Combine(dir, $"{package}.csproj")))
            {
                continue;
            }

            foreach (var task in XDocument.Load(targets).Descendants("UsingTask"))
            {
                var assembly = task.Attribute("AssemblyFile")?.Value ?? string.Empty;

                if (assembly.StartsWith(here, StringComparison.Ordinal)
                    && assembly.EndsWith(".dll", StringComparison.Ordinal)
                    && seen.Add(package + "/" + assembly))
                {
                    data.Add(package, assembly[here.Length..]);
                }
            }
        }

        return data;
    }

    /// <summary>
    ///     A task assembly produced during the build is packed by NAME, never left to <c>build\**</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A glob is expanded at project <b>evaluation</b>, before any target runs — including the
    ///         build of the <c>ProjectReference</c> that produces the DLL. On a tree where it is not
    ///         already on disk the glob matches nothing, <c>dotnet pack</c> <b>succeeds</b>, and the
    ///         package ships with a <c>UsingTask</c> pointing at a file that is not in it. The consumer
    ///         fails with MSB4062; packing again on the same machine is correct, because the first build
    ///         left the DLL behind — so it reads as flakiness rather than as a fact about ordering
    ///         (<see href="https://github.com/pal-tamas/rask/issues/852" />).
    ///     </para>
    ///     <para>
    ///         A literal <c>Include</c> needs the file only when the item is copied, which is after the
    ///         reference has built. It also turns the failure loud: a genuinely missing file is NU5019
    ///         and a non-zero pack, verified by pointing one at a name nothing produces.
    ///     </para>
    ///     <para>
    ///         Structural, like the rest of this class, and for the same reason — the defect exists only
    ///         on the far side of a <c>dotnet pack</c>, and only on a tree that has not built yet, which
    ///         no in-repo build ever is by the time a suite runs.
    ///     </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PackedTaskAssemblies))]
    public void A_generated_task_assembly_is_packed_by_name_rather_than_left_to_a_glob(string package, string dll)
    {
        var csproj = XDocument.Load(Path.Combine(_repoRoot, "src", package, $"{package}.csproj"));
        var packItems = csproj.Descendants("None")
            .Where(e => (e.Attribute("Include")?.Value ?? string.Empty)
                .StartsWith(@"build\", StringComparison.Ordinal))
            .ToArray();

        var literal = packItems.SingleOrDefault(e => e.Attribute("Include")?.Value == $@"build\{dll}");

        Assert.True(
            literal is not null,
            $"{package}.csproj leaves build/{dll} to a glob, but build/{package}.targets loads it with a "
            + "UsingTask. A glob is expanded at evaluation, before the ProjectReference that produces the "
            + "DLL has built — so on a tree where it is not already on disk, pack SUCCEEDS and ships a "
            + $"package whose UsingTask points at a file that is not in it. Add: <None Include=\"build\\{dll}\" "
            + "Pack=\"true\" PackagePath=\"build\\\"/>.");

        Assert.Equal("true", literal!.Attribute("Pack")?.Value);
        Assert.Equal(@"build\", literal.Attribute("PackagePath")?.Value);

        // Any glob beside it must exclude the DLL, or once the file IS on disk it is collected twice.
        foreach (var glob in packItems.Where(e =>
                     (e.Attribute("Include")?.Value ?? string.Empty).Contains('*', StringComparison.Ordinal)))
        {
            Assert.Contains(dll, glob.Attribute("Exclude")?.Value ?? string.Empty, StringComparison.Ordinal);
        }
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

        // Scoped TypeScript reaches the generator by a different route than CSS, so the assertion has
        // to follow it. The .ts is globbed into a private item, compiled to obj/, and only the OUTPUT
        // becomes an AdditionalFile — carrying the source path as metadata. Asserting on the glob
        // alone would stay green if the AdditionalFiles contribution were dropped, and every consumer
        // would silently lose scoped assets with a build that still succeeds.
        var privateGlobs = targets.Descendants("_RaskScopedTs")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains(@"**\*.ts", privateGlobs);
        Assert.Contains(globs, g => g is not null && g.Contains("_RaskCompiledScopedJs", StringComparison.Ordinal));

        var metadata = targets.Descendants("CompilerVisibleItemMetadata")
            .Select(e => (e.Attribute("Include")?.Value, e.Attribute("MetadataName")?.Value))
            .ToList();

        Assert.Contains(("AdditionalFiles", "RaskTsSource"), metadata);

        // The framework's own ambient declarations have to be compiled alongside a consumer's scoped
        // files, or every app calling a [JSInvokable] has to redeclare window.DotNet itself.
        var typeGlobs = targets.Descendants("_RaskScopedTsTypes")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains(@"**\*.d.ts", typeGlobs);
        Assert.Contains(typeGlobs, g => g is not null && g.EndsWith("rask-globals.d.ts", StringComparison.Ordinal));

        // An author's .js must never reach csc: Rask no longer compiles one, and the only reason it
        // is globbed at all is so the generator can name it in RASK054.
        //
        // EndsWith rather than Contains, because `Resources\**\*.json` contains ".js" — the classic
        // substring assertion that matches the thing it was meant to exclude.
        Assert.DoesNotContain(globs, g => g is not null && g.EndsWith(".js", StringComparison.Ordinal));

        var visible = targets.Descendants("CompilerVisibleProperty")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains("RaskScopedTsAutoInclude", visible);
        Assert.Contains("RaskStrayScopedJs", visible);
        Assert.Contains("RaskBuilderEntryInjection", visible);
    }

    /// <summary>
    ///     The TypeScript compile ships with the build integration, and pack fails loudly if it does not.
    /// </summary>
    /// <remarks>
    ///     A <c>&lt;None Include&gt;</c> of an absent file packs nothing and reports nothing, so the
    ///     first sign of a missing task assembly would be MSB4062 in a consumer's build, naming a file
    ///     they have never heard of. That is the same shape as #544, which hid for a year.
    /// </remarks>
    [Fact]
    public void The_typescript_compiler_task_ships_with_the_build_integration()
    {
        var pack = XDocument.Load(Path.Combine(_repoRoot, "src", "RaskCoreBuildPack.targets"));

        var packed = pack.Descendants("None")
            .Where(e => e.Attribute("Pack")?.Value == "true")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(packed, p => p.EndsWith("Rask.TypeScript.Tasks.dll", StringComparison.Ordinal));
        Assert.Contains(packed, p => p.EndsWith("rask-globals.d.ts", StringComparison.Ordinal));

        // And the guard that turns a missing one into an error at pack time rather than at a
        // consumer's first build.
        var guarded = pack.Descendants("Error")
            .Select(e => e.Attribute("Condition")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(guarded, c => c.Contains("Rask.TypeScript.Tasks.dll", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Every host package ships its own copy of the analyzer payload, so an app that references two
    ///     of them hands csc the generator twice — from two package paths, which Roslyn reads as two
    ///     distinct generators. Both run, both emit <c>RaskBuilderSetters.g.cs</c>, and the build dies on
    ///     <c>CS0101 ... already contains a definition for RaskBuilderSetters&lt;Assembly&gt;</c>, pointing
    ///     at generated code the author never wrote.
    ///     <para>
    ///         Referencing two hosts is not hypothetical: a wasm-hosted app whose <c>.Server</c> mounts the
    ///         operator dashboard pulls in Rask.Wasm.Hosting (and with it Rask.Wasm) alongside Rask.Server.
    ///         <c>_RaskDeduplicateAnalyzers</c> in the shared core targets is what keeps that buildable.
    ///     </para>
    ///     <para>
    ///         Structural, and cheap, for the same reason as everything else here: the behavioural proof is
    ///         <c>Generated_all_batteries_wasm_hosted_solution_builds</c>, which only runs behind
    ///         <c>RASK_CLI_BUILD_E2E=1</c>.
    ///     </para>
    /// </summary>
    [Fact]
    public void The_core_build_integration_deduplicates_the_analyzer_payload()
    {
        var targets = XDocument.Load(Path.Combine(_repoRoot, "src", "Rask.Core", "build", "Rask.Core.targets"));

        var target = Assert.Single(
            targets.Descendants("Target"),
            e => e.Attribute("Name")?.Value == "_RaskDeduplicateAnalyzers");

        // Must run before the compiler reads @(Analyzer), and after NuGet has contributed every
        // package's payload — BeforeTargets="CoreCompile" is the only point that is both.
        Assert.Equal("CoreCompile", target.Attribute("BeforeTargets")?.Value);

        // Both halves have to be there. Removing without re-adding would strip the generator entirely
        // and every factory would silently vanish; re-adding without removing is the bug itself.
        Assert.Contains(target.Descendants("Analyzer"), e => e.Attribute("Remove") is not null);
        Assert.Contains(target.Descendants("Analyzer"), e => e.Attribute("Include") is not null);
    }

    /// <summary>
    ///     The other arm of the same guard: the dedupe above names the two analyzer assemblies literally,
    ///     so renaming or adding one silently leaves it duplicated again. Asserted against the pack
    ///     fragment that decides what actually ships.
    /// </summary>
    [Fact]
    public void The_dedupe_covers_every_analyzer_assembly_the_host_packages_ship()
    {
        var pack = XDocument.Load(Path.Combine(_repoRoot, "src", "RaskAnalyzerPack.targets"));
        var packed = pack.Descendants("None")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null && v.Contains(".dll", StringComparison.Ordinal))
            // The MSBuild paths use backslashes, which are not separators on Unix — so split on the
            // literal rather than asking Path, which would hand back the whole string here and make
            // every assertion below trivially compare against a path that contains the name anyway.
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
            .ToList();

        Assert.NotEmpty(packed);

        var targets = File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "build", "Rask.Core.targets"));
        foreach (var assembly in packed)
        {
            Assert.True(
                targets.Contains($"'{assembly}'", StringComparison.Ordinal),
                $"{assembly}.dll is packed into analyzers/dotnet/cs/ by every host package, but "
                + "_RaskDeduplicateAnalyzers in Rask.Core.targets does not name it — an app referencing "
                + "two host packages will load it twice and fail to compile with CS0101.");
        }
    }
}

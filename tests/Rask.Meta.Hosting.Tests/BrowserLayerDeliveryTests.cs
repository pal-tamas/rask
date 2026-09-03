using System.Text.RegularExpressions;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     How a meta framework front end receives Rask's browser layer.
/// </summary>
/// <remarks>
///     <para>
///         The same modules the Server and WASM clients bundle, packed from <c>Rask.Core</c> into this
///         package and copied into the app's own source directory — with a <c>tsconfig.rask.json</c>
///         beside them so they are imported as <c>@rask/browser/geolocation</c> rather than by a
///         relative path that would differ per framework.
///     </para>
///     <para>
///         Asserted on the pack item and the targets rather than by packing, following the reasoning in
///         <c>PackageDependencyTests</c>: the silently-packs-nothing failure mode needs a glob that runs
///         before its files exist, and these are committed source. The built <c>.nupkg</c> was checked
///         by hand and carries 38 modules under <c>client/browser/</c> with <c>globals.ts</c> absent.
///     </para>
/// </remarks>
public sealed class BrowserLayerDeliveryTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string Targets => File.ReadAllText(Path.Combine(
        _repoRoot, "src", "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.targets"));

    [Fact]
    public void The_package_ships_the_browser_modules_but_not_the_globals_entry()
    {
        var csproj = File.ReadAllText(Path.Combine(
            _repoRoot, "src", "Rask.Meta.Hosting", "Rask.Meta.Hosting.csproj"));

        Assert.Contains(@"..\Rask.Core\Resources\browser\*.ts", csproj, StringComparison.Ordinal);
        Assert.Contains(@"PackagePath=""client\browser""", csproj, StringComparison.Ordinal);

        // globals.ts publishes the window.__rask* namespaces .NET resolves dotted identifiers against.
        // A front end has no use for it, and it is the one module here with an import-time side effect —
        // which in a Node SSR pass is a ReferenceError before the page renders.
        Assert.Contains(
            @"Exclude=""..\Rask.Core\Resources\browser\globals.ts""", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_framework_names_the_source_directory_it_actually_uses()
    {
        // The six do not agree — Nuxt 4 and Next's App Router keep source in app/, the rest in src/ —
        // and the copy lands in whichever this table names. A framework added to the entry table
        // without a source directory silently gets no browser layer at all: _RaskMetaGeneratedDir stays
        // empty, so the copy target's Condition is false and nothing is written. Nothing else would
        // notice, so this asserts every framework the entry table knows also has a source directory.
        var targets = Targets;

        var frameworks = Regex.Matches(targets, @"'\$\(RaskMetaFramework\)' == '(?<name>[a-z-]+)'")
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(6, frameworks.Count);

        foreach (var framework in frameworks)
        {
            var block = FrameworkBlock(targets, framework);
            Assert.True(
                block.Contains("_RaskMetaSourceDir>", StringComparison.Ordinal),
                $"'{framework}' has a server entry but no _RaskMetaSourceDir, so the browser layer "
                + "would be copied nowhere and no build would say so.");
        }

        // The split itself, so a wrong move here is a failing test rather than a support thread.
        Assert.Contains("<_RaskMetaSourceDir>app</_RaskMetaSourceDir>", targets, StringComparison.Ordinal);
        Assert.Contains("<_RaskMetaSourceDir>src</_RaskMetaSourceDir>", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void The_copy_runs_before_the_front_end_build_and_does_not_retrigger_it()
    {
        var targets = Targets;

        // Both targets hang off CoreCompile, so ordering is DependsOnTargets or nothing — and the wrong
        // order means the first build of a fresh checkout compiles an app whose imports are not there.
        Assert.Contains(
            @"DependsOnTargets=""_RaskMetaInstallDeps;_RaskMetaCopyBrowserModules""",
            targets,
            StringComparison.Ordinal);

        // The copy writes INTO _RaskMetaInput's own glob (src/**/* and app/**/*). Copying
        // unconditionally would leave every build looking dirty to the next one and re-run
        // `npm run build` — a full Nuxt or Next production build, every time.
        Assert.Contains(@"SkipUnchangedFiles=""true""", targets, StringComparison.Ordinal);
        Assert.Contains(@"WriteOnlyWhenDifferent=""true""", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_wire_is_generated_and_the_dispatcher_ships_with_it()
    {
        var csproj = File.ReadAllText(Path.Combine(
            _repoRoot, "src", "Rask.Meta.Hosting", "Rask.Meta.Hosting.csproj"));
        var props = File.ReadAllText(Path.Combine(
            _repoRoot, "src", "Rask.Meta.Hosting", "build", "Rask.Meta.Hosting.props"));
        var targets = Targets;

        // Without this the CqrsCodecGenerator emits NOTHING — it reads the flag through Roslyn's
        // analyzer config, so an MSBuild property that is merely set is invisible to it. That was this
        // lane's state: no contracts, no error, no hint that a feature had not run.
        Assert.Contains(@"<CompilerVisibleProperty Include=""RaskEmitTypeScript""/>", props, StringComparison.Ordinal);
        Assert.Contains("<RaskEmitTypeScript", targets, StringComparison.Ordinal);

        Assert.Contains("WriteGeneratedTypeScriptTask", targets, StringComparison.Ordinal);
        Assert.Contains(@"AssemblyPath=""@(IntermediateAssembly)""", targets, StringComparison.Ordinal);

        // The dispatcher the generated messages import from. Packed from the SPA lane's client/ rather
        // than copied into this project, so the two lanes cannot drift into two wires.
        Assert.Contains(@"..\Rask.Spa.Hosting\client\*.ts", csproj, StringComparison.Ordinal);

        // #852: a Pack glob is expanded at evaluation, so the task assembly is named on its own line
        // and excluded from build\**. A glob alone silently packs nothing on a tree where the DLL is
        // not yet built, and the consumer's <UsingTask> then points at a file that never shipped.
        Assert.Contains(@"Exclude=""build\Rask.Spa.Tasks.dll""", csproj, StringComparison.Ordinal);
        Assert.Contains(@"<None Include=""build\Rask.Spa.Tasks.dll""", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_alias_is_written_beside_the_modules()
    {
        // `@rask/browser/geolocation` rather than a relative path, because the physical directory
        // differs per framework and the import specifier should not.
        var targets = Targets;

        Assert.Contains("tsconfig.rask.json", targets, StringComparison.Ordinal);
        Assert.Contains("@rask/*", targets, StringComparison.Ordinal);
    }

    /// <summary>The <c>&lt;When&gt;</c> block for one framework, up to the next one.</summary>
    private static string FrameworkBlock(string targets, string framework)
    {
        var start = targets.IndexOf($"'$(RaskMetaFramework)' == '{framework}'", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{framework} is not in the framework table.");

        var next = targets.IndexOf("</When>", start, StringComparison.Ordinal);
        return next < 0 ? targets[start..] : targets[start..next];
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}

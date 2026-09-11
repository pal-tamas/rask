using System.Text.Json;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask new --islands &lt;runtime&gt;…</c> scaffolds a front-end component as an ordinary Rask
///     component, and refuses the combinations the build would refuse later.
/// </summary>
/// <remarks>
///     Islands are the one thing here that is assembled rather than materialised: eight runtimes across
///     three host shapes, several at a time, so what is committed is a fragment per runtime. These
///     assert the assembly — which files appear, what the merged manifest says, and what the tsconfig
///     has to carry for each runtime's own compiler to accept the file.
/// </remarks>
public sealed class IslandScaffoldTests
{
    private const string Target = "/proj/Shop";

    public static TheoryData<string> Runtimes() => [.. IslandRuntimes.All];

    private static IReadOnlyList<ScaffoldFile> Scaffold(params string[] runtimes) =>
        TemplateMaterializer.Files(Target, "server", "Shop", new ServerBatteries(), "9.9.9", runtimes);

    [Theory]
    [MemberData(nameof(Runtimes))]
    public void Every_runtime_scaffolds_a_pair(string runtime)
    {
        var files = Scaffold(runtime);

        // A pair, always: the .cs that declares the component (its base class IS the declaration) and
        // the front-end file that renders it. One without the other is not an island.
        var declaration = files
            .Where(f => f.Path.Contains("Islands", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.Path))
            .ToArray();

        Assert.Contains(declaration, f => f.EndsWith(".cs", StringComparison.Ordinal));

        if (runtime == IslandRuntimes.Blazor)
        {
            // Blazor's half of the pair is a .razor in a REFERENCED Razor Class Library, not beside the
            // declaration: a .razor in the same project is generated during the same compilation, so
            // its [Parameter]s cannot be read and no chain steps are generated for them (RASK066).
            Assert.Single(declaration);
            Assert.Contains(files, f => f.Path.EndsWith("BlazorCounter.razor", StringComparison.Ordinal));
            Assert.Contains(files, f => f.Path.EndsWith(".Components.csproj", StringComparison.Ordinal));
            return;
        }

        Assert.Equal(2, declaration.Length);
        Assert.Contains(declaration, f => !f.EndsWith(".cs", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Runtimes))]
    public void Every_runtime_gets_a_directory_of_its_own(string runtime)
    {
        // Three runtimes pair with a .tsx and two with a .ts, so a shared or nested directory is
        // ambiguous — ExternalBuildPlan refuses overlapping trees for two runtimes on one extension.
        // Nesting counts: Features/Islands/Angular inside Features/Islands is exactly that case, which
        // is why every runtime gets its own folder rather than only the JSX three.
        var directory = Scaffold(runtime)
            .Select(f => Path.GetDirectoryName(f.Path)!)
            .First(d => d.Contains("Islands", StringComparison.Ordinal));

        Assert.EndsWith(runtime, directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Several_runtimes_coexist_without_sharing_a_directory()
    {
        var directories = Scaffold("react", "vue", "svelte", "lit", "angular", "blazor")
            .Where(f => f.Path.Contains("Islands", StringComparison.Ordinal))
            .Select(f => Path.GetDirectoryName(f.Path)!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(6, directories.Length);
        Assert.DoesNotContain(
            directories,
            outer => directories.Any(inner =>
                !string.Equals(inner, outer, StringComparison.Ordinal)
                && inner.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_merged_manifest_carries_every_chosen_runtime_and_nothing_else()
    {
        var manifest = Scaffold("react", "vue").Single(f =>
            string.Equals(Path.GetFileName(f.Path), "package.json", StringComparison.Ordinal));

        using var document = JsonDocument.Parse(manifest.Content);
        var deps = document.RootElement.GetProperty("devDependencies");

        Assert.True(deps.TryGetProperty("@vitejs/plugin-react", out _));
        Assert.True(deps.TryGetProperty("@vitejs/plugin-vue", out _));
        // Every island is bundled by Vite and checked by TypeScript, whichever runtime it is.
        Assert.True(deps.TryGetProperty("vite", out _));
        Assert.True(deps.TryGetProperty("typescript", out _));
        // Not asked for, so not installed: an unused framework in the tree is a slower install and a
        // larger lockfile for something the app never imports.
        Assert.False(deps.TryGetProperty("svelte", out _));
    }

    [Fact]
    public void Blazor_alone_needs_no_node()
    {
        // The one island kind with no npm side: the Razor SDK compiles the .razor and Rask renders it
        // server-side. Writing a package.json for it would make `dotnet build` probe for node and run
        // npm to install nothing.
        var files = Scaffold("blazor");

        Assert.DoesNotContain(
            files, f => string.Equals(Path.GetFileName(f.Path), "package.json", StringComparison.Ordinal));
    }

    [Fact]
    public void A_jsx_runtime_gets_the_setting_without_which_a_tsx_cannot_compile()
    {
        var tsconfig = TsConfig(Scaffold("react"));

        Assert.Equal(
            "react-jsx",
            tsconfig.GetProperty("compilerOptions").GetProperty("jsx").GetString());
    }

    [Fact]
    public void Lit_and_Angular_can_share_a_project()
    {
        // They used to disagree: Lit 3's `accessor` form needs experimentalDecorators OFF and Angular
        // needs it ON, so a project holding both could not type-check either way. The Lit fragment is
        // written in the legacy form, which works under ON.
        var tsconfig = TsConfig(Scaffold("lit", "angular"));

        Assert.True(tsconfig.GetProperty("compilerOptions").GetProperty("experimentalDecorators").GetBoolean());

        var lit = Scaffold("lit").Single(f => f.Path.EndsWith("LitBadge.ts", StringComparison.Ordinal));
        Assert.DoesNotContain("accessor ", lit.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_island_prop_types_resolve()
    {
        // The extends is what makes `@rask/<Name>.props` resolve. A fragment under obj/ rather than
        // settings written here, because nothing generated is committed.
        var tsconfig = TsConfig(Scaffold("react"));

        Assert.Equal(
            "./obj/rask-external/tsconfig.paths.json", tsconfig.GetProperty("extends").GetString());
    }

    [Fact]
    public void A_project_with_no_islands_does_not_extend_a_file_that_will_not_exist()
    {
        // The extended fragment is generated by the island build. Adding it unconditionally would break
        // every project without islands, on its first type-check, with a missing-file error.
        var tsconfig = TsConfig(
            TemplateMaterializer.Files(Target, "server", "Shop", new ServerBatteries(), "9.9.9"));

        Assert.False(tsconfig.TryGetProperty("extends", out _));
    }

    [Theory]
    [InlineData("react", "Rask.External")]
    [InlineData("blazor", "Rask.Blazor")]
    public void The_host_references_the_package_the_runtime_needs(string runtime, string package)
    {
        var csproj = Scaffold(runtime).Single(f => f.Path.EndsWith("Shop.csproj", StringComparison.Ordinal));

        Assert.Contains($"Include=\"{package}\"", csproj.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_with_no_islands_references_neither()
    {
        var csproj = TemplateMaterializer
            .Files(Target, "server", "Shop", new ServerBatteries(), "9.9.9")
            .Single(f => f.Path.EndsWith("Shop.csproj", StringComparison.Ordinal));

        Assert.DoesNotContain("Rask.External", csproj.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Blazor", csproj.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void React_and_Preact_are_refused_by_name()
    {
        var refusal = IslandRuntimes.Refuse(["react", "preact"]);

        Assert.NotNull(refusal);
        Assert.Contains("preact/compat", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_runtime_the_flag_offers_has_a_committed_fragment()
    {
        // The choice list and the fragments are two places that can disagree, and the failure is a
        // flag the parser accepts and the scaffolder then cannot honour — the #830 shape.
        foreach (var runtime in IslandRuntimes.All)
        {
            Assert.True(
                TemplateAssets.Has($"_islands/{runtime}"),
                $"--islands offers '{runtime}' with no fragment under src/Rask.Templates/_islands/.");
        }
    }

    [Theory]
    [InlineData("react", "preact")]
    [InlineData("PREACT", "React")]
    public async Task The_command_refuses_the_pair_npm_cannot_install(string first, string second)
    {
        var console = new StringConsole();
        var command = new NewCommand(console, new FakeFileSystem(), new FakeProcessRunner(), "/proj");

        var exit = await command.ExecuteAsync(
            ["Shop", "--islands", first, second, "--no-restore", "--no-git"], CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("cannot take both react and preact", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Islands_are_refused_on_a_template_that_is_already_a_front_end()
    {
        var console = new StringConsole();
        var command = new NewCommand(console, new FakeFileSystem(), new FakeProcessRunner(), "/proj");

        var exit = await command.ExecuteAsync(
            ["Shop", "--template", "react", "--islands", "vue", "--no-restore", "--no-git"],
            CancellationToken.None);

        Assert.Equal(2, exit);
        Assert.Contains("is not available on --template react", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_flag_takes_several_runtimes_without_swallowing_the_app_name()
    {
        // `--islands` consumes following tokens only while they are declared choices, so a name written
        // after the flag is still the name rather than a tenth runtime.
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        var command = new NewCommand(console, fs, new FakeProcessRunner(), "/proj");

        var exit = await command.ExecuteAsync(
            ["--islands", "react", "vue", "Shop", "--no-restore", "--no-git"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(fs.Files.Keys, p => p.Contains("Shop", StringComparison.Ordinal));
        Assert.Contains(fs.Files.Keys, p => p.EndsWith("ReactCounter.tsx", StringComparison.Ordinal));
        Assert.Contains(fs.Files.Keys, p => p.EndsWith("VueCounter.vue", StringComparison.Ordinal));
    }

    [Fact]
    public void An_island_version_never_contradicts_the_template_that_ships_the_same_package()
    {
        // The island fragments and the SPA/meta client manifests both name react, vue, svelte, the
        // Angular packages and Vite. Written twice, they drift — and the drift is invisible offline:
        // the first island version table said @angular/build ^20.4.1 while the Angular template said
        // ^22.1.8, and npm answered ETARGET only when something actually installed it.
        var islands = new Dictionary<string, (string Range, string Where)>(StringComparer.Ordinal);

        foreach (var runtime in IslandRuntimes.All)
        {
            var fragment = TemplateAssets.Load($"_islands/{runtime}")
                .SingleOrDefault(a => string.Equals(a.Path, "island.json", StringComparison.Ordinal));

            if (fragment is null)
            {
                continue;
            }

            using var document = JsonDocument.Parse(fragment.Bytes);
            if (!document.RootElement.TryGetProperty("devDependencies", out var deps))
            {
                continue;
            }

            foreach (var entry in deps.EnumerateObject())
            {
                islands[entry.Name] = (entry.Value.GetString() ?? "", runtime);
            }
        }

        // Compared against ITS OWN framework's template, not against every template. Two templates
        // disagreeing is normal and not ours to reconcile — Analog ships Angular 20 because that is
        // what create-analog wrote, while the Angular SPA template ships 22. What must not differ is a
        // runtime's island and that same runtime's template, because they install the same packages
        // into projects this repository generates from one place.
        var conflicts = new List<string>();

        foreach (var runtime in IslandRuntimes.All)
        {
            foreach (var (package, range) in TemplateRanges(runtime))
            {
                if (islands.TryGetValue(package, out var island)
                    && string.Equals(island.Where, runtime, StringComparison.Ordinal)
                    && !string.Equals(island.Range, range, StringComparison.Ordinal))
                {
                    conflicts.Add(
                        $"{package}: _islands/{runtime} says {island.Range}, "
                        + $"the {runtime} template says {range}");
                }
            }
        }

        Assert.True(
            conflicts.Count == 0,
            "An island and its own framework's template disagree about a package both install:\n  "
            + string.Join("\n  ", conflicts));
    }

    private static IEnumerable<(string Package, string Range)> TemplateRanges(string runtime)
    {
        var manifest = Path.Combine(
            CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates", runtime, "client", "package.json");

        // blazor has no template of its own and no npm side; nothing to compare.
        if (!File.Exists(manifest))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            if (!document.RootElement.TryGetProperty(section, out var deps))
            {
                continue;
            }

            foreach (var entry in deps.EnumerateObject())
            {
                yield return (entry.Name, entry.Value.GetString() ?? "");
            }
        }
    }

    private static JsonElement TsConfig(IReadOnlyList<ScaffoldFile> files) =>
        JsonDocument.Parse(files
            .Single(f => string.Equals(Path.GetFileName(f.Path), "tsconfig.json", StringComparison.Ordinal))
            .Content).RootElement;
}

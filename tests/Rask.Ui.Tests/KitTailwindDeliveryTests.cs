using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     The kit reaches an app's own Tailwind build: the package hands over its Tailwind sources, the
///     app's stylesheet imports them with one line, and the app compiles ONE sheet.
/// </summary>
/// <remarks>
///     <para>
///         The kit's class names are string literals in a compiled assembly, where no scan finds them. So
///         the kit compiles a sheet of its own — and for a long time that was the only way an app got
///         them: link the precompiled sheet, then the app's own. Two sheets each carry an
///         <c>@layer utilities</c>, ranked by link order alone, and an app's <c>bg-white</c> beat the
///         kit's <c>dark:bg-zinc-800</c> (<see cref="OneStylesheetCascadeTests" />).
///     </para>
///     <para>
///         A Flux app has one Tailwind build that scans Flux's own views. This is the same arrangement
///         for an assembly: the build writes four files into <c>Styles/vendor/</c> — the entry an app
///         imports, the kit's theme and rules, the list of classes its components write, and daisyUI's
///         plugin while the kit still draws with it — with no npm, no <c>node_modules</c> and no
///         <c>package.json</c>.
///     </para>
/// </remarks>
public sealed class KitTailwindDeliveryTests
{
    private static readonly string _targets = ReadTargets();

    [Fact]
    public void The_sources_are_packed_beside_the_targets()
    {
        // An EmbeddedResource is only reachable at run time, and this copy has to happen at build
        // time — before Tailwind runs, not after the app has started.
        var csproj = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Rask.Ui.csproj"));

        Assert.Contains("""<None Include="Styles\rask-ui.css" Pack="true" PackagePath="build\" """, csproj, StringComparison.Ordinal);
        Assert.Contains("""<None Include="Styles\ui.css" Pack="true" PackagePath="build\rask-ui.kit.css" """, csproj, StringComparison.Ordinal);
        Assert.Contains("""<None Include="Styles\daisyui.mjs" Pack="true" PackagePath="build\" """, csproj, StringComparison.Ordinal);
        Assert.Contains("""PackagePath="build/rask-ui.classes.txt" """, csproj, StringComparison.Ordinal);

        // And transitively: a scaffold references Rask.Server alone, which brings the kit.
        foreach (var name in KitConsumer.Sources().Keys)
        {
            Assert.Matches(new Regex($@"PackagePath=""buildTransitive[\\/]{Regex.Escape(name)}"""), csproj);
        }
    }

    [Fact]
    public void The_in_repo_fallback_names_no_target_framework()
    {
        // The precompiled sheet's fallback named net10.0 once and shipped rask.sh grey from a checkout
        // that had only built the browser face (KitStylesheetResolutionTests). The class list is build
        // output too, so it resolves the same two ways: the consumer's own face, then any.
        var literals = Regex.Matches(_targets, @"obj/net\d+\.\d+[a-z-]*/rask-ui\.classes\.txt");

        Assert.True(literals.Count == 0, $"a literal TFM is findable on some machines only: {string.Join(", ", literals.Select(m => m.Value))}");
        Assert.Contains("../obj/$(TargetFramework)/rask-ui.classes.txt", _targets, StringComparison.Ordinal);
        Assert.Contains("../obj/*/rask-ui.classes.txt", _targets, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_that_names_only_a_host_still_counts_as_drawing_with_the_kit()
    {
        // A scaffold references Rask.Server alone, which brings the kit. Recognising only Rask.Ui skipped the
        // copy, and Tailwind then failed on the import in every new app.
        var gate = Regex.Match(_targets, @"<_RaskUiKitReference Include=""@\(PackageReference\)""\s+Condition=""([^""]*)""").Groups[1].Value;

        var named = Regex.Matches(gate, @"== '([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["Rask.Ui", "Rask.Server", "Rask.Wasm"], named);
    }

    [Fact]
    public void The_copy_is_ordered_before_the_Tailwind_compile()
    {
        // Rask.Tailwind also hooks BeforeBuild, and the order between two targets sharing one
        // BeforeTargets is import order — which this file does not get to decide. Naming the compile
        // directly is the only thing that guarantees the sources are on disk before the import is followed.
        var target = Regex.Match(_targets, @"<Target Name=""RaskUiWriteTailwindSources""[^>]*>").Value;

        Assert.Contains("BeforeTargets=\"_RaskTailwindBuild;BeforeBuild\"", target, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stylesheet_that_imports_the_kit_gets_its_sources_and_tells_the_host()
    {
        using var app = new ConsumerProject(KitConsumer.Import);

        var build = app.Build();

        Assert.True(build.ExitCode == 0, build.Text);
        foreach (var (name, source) in KitConsumer.Sources())
        {
            var written = Path.Combine(app.Directory, "Styles", "vendor", name);
            Assert.True(File.Exists(written), $"Styles/vendor/{name} was not written:\n{build.Text}");
            Assert.Equal(File.ReadAllText(source), File.ReadAllText(written));
        }

        // RaskDocument reads this and links the app's one sheet, not the precompiled kit beside it.
        Assert.Contains("\"Identity\": \"Rask.Ui.Stylesheet\"", build.Text, StringComparison.Ordinal);
        Assert.Contains("\"Value\": \"compiled-in\"", build.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stylesheet_that_does_not_import_the_kit_gets_nothing()
    {
        // Referencing the kit is not the same as compiling it: a comment that mentions the import, in a
        // sheet that never makes it, is still a sheet without the kit.
        using var app = new ConsumerProject("/* not yet: @import \"./vendor/rask-ui.css\"; */\n@import \"tailwindcss\";");

        var build = app.Build();

        Assert.True(build.ExitCode == 0, build.Text);
        Assert.False(Directory.Exists(Path.Combine(app.Directory, "Styles", "vendor")));
        Assert.DoesNotContain("Rask.Ui.Stylesheet", build.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_composing_the_entry_itself_counts_as_importing_the_kit()
    {
        // The Dashboard's shape, and any app's that wants Tailwind without its preflight: the three
        // lines of rask-ui.css written out by hand, naming the kit's own file.
        using var app = new ConsumerProject("@import \"tailwindcss/utilities.css\" layer(utilities);\n@import \"./vendor/rask-ui.kit.css\";");

        var build = app.Build();

        Assert.True(build.ExitCode == 0, build.Text);
        Assert.True(File.Exists(Path.Combine(app.Directory, "Styles", "vendor", "rask-ui.kit.css")));
    }

    [Fact]
    public void The_sources_land_in_vendor_beside_the_stylesheet_wherever_it_is()
    {
        // Tailwind resolves the import against the stylesheet, so that is what the folder follows — and
        // a stylesheet in the project root has no folder name to put in front of `vendor`.
        using var app = new ConsumerProject("@import \"tailwindcss\";", "<RaskTailwindInput>site.css</RaskTailwindInput>");
        File.WriteAllText(Path.Combine(app.Directory, "site.css"), KitConsumer.Import);

        var build = app.Build();

        Assert.True(build.ExitCode == 0, build.Text);
        Assert.True(File.Exists(Path.Combine(app.Directory, "vendor", "rask-ui.css")), build.Text);
        Assert.False(Directory.Exists(Path.Combine(app.Directory, "Styles", "vendor")));
    }

    [Fact]
    public void Compiling_the_kit_in_and_asking_for_the_precompiled_sheet_fails_the_build()
    {
        using var app = new ConsumerProject(KitConsumer.Import, "<RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>");

        var build = app.Build();

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("That is the kit twice", build.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Tailwind_sheet_without_the_kit_beside_the_precompiled_one_fails_the_build()
    {
        // The arrangement every app had, and the one the bug lives in. The message carries the fix.
        using var app = new ConsumerProject("@import \"tailwindcss\";", "<RaskUiWriteStylesheet>true</RaskUiWriteStylesheet>");

        var build = app.Build();

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("Compile ONE sheet instead", build.Text, StringComparison.Ordinal);
        Assert.Contains("@import \"./vendor/rask-ui.css\";", build.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_import_the_build_was_told_not_to_serve_fails_the_build()
    {
        using var app = new ConsumerProject(KitConsumer.Import, "<RaskUiTailwind>false</RaskUiTailwind>");

        var build = app.Build();

        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("RaskUiTailwind=false", build.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_precompiled_sheet_an_earlier_build_left_in_wwwroot_is_removed()
    {
        // Left there it is published and precached, and anything still linking it loads the kit twice
        // from a file no property asks for any more.
        using var app = new ConsumerProject(KitConsumer.Import);
        var stale = Path.Combine(app.Directory, "wwwroot", "css", "rask-ui.css");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, ".btn{}");

        var build = app.Build();

        Assert.True(build.ExitCode == 0, build.Text);
        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void An_apps_one_sheet_carries_every_class_the_kits_own_sheet_does()
    {
        // The claim the whole approach rests on, run for real: from the list alone — this app's page
        // writes nothing — Tailwind emits a rule for every class the kit's precompiled sheet defines.
        var defined = ClassesDefinedIn(_kitOnly.Value);

        var missing = ClassesDefinedIn(UiStylesheet.Css).Except(defined, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        Assert.True(ClassesDefinedIn(UiStylesheet.Css).Count > 500, "the precompiled sheet parsed to almost nothing.");
        Assert.True(
            missing.Length == 0,
            $"{missing.Length} class(es) the kit's components write are in its precompiled sheet and not in "
            + $"an app's own: {string.Join(" ", missing.Take(40))}");
    }

    [Fact]
    public void The_app_compiles_the_daisyUI_classes_it_writes_itself_in_the_same_pass()
    {
        // `rask new` writes daisyUI names in its own markup. They compile from the plugin the kit's file
        // loads, beside the app's own utilities — this is one stylesheet, not two.
        var css = _withPage.Value;

        foreach (var name in (string[])["navbar", "hero", "footer", "card-actions", "px-8"])
        {
            Assert.True(Regex.IsMatch(css, $@"^\s*\.{Regex.Escape(name)}\s*\{{", RegexOptions.Multiline), $".{name} is not in the compiled sheet.");
        }
    }

    [Fact]
    public void The_plugin_bundle_is_not_scanned_as_a_safelist()
    {
        // 348 KB of daisyUI's own code sits beside the kit's file, naming every class daisyUI defines.
        // Scanned, it is a safelist for the whole library and the sheet carries every component whether
        // or not anything uses one — which reads as correct, because a sheet containing too much looks
        // exactly like a sheet containing enough. `@source not "./daisyui.mjs"` is what stops it.
        // The kit draws with nearly all of daisyUI, so the candidates are the few names it leaves alone.
        var unused = ((string[])["glass", "link-hover", "skeleton-text", "react-day-picker", "pika-single", "timeline", "carousel"])
            .Where(name => !KitConsumer.Classes.Contains(name))
            .ToArray();

        Assert.NotEmpty(unused);
        foreach (var name in unused)
        {
            Assert.False(
                Regex.IsMatch(_withPage.Value, $@"^\s*\.{name}\s*\{{", RegexOptions.Multiline),
                $".{name} is in a sheet whose sources never name it — the bundle is being scanned.");
        }
    }

    [Fact]
    public void The_class_list_carries_candidates_as_markup_writes_them()
    {
        // Unescaped, variants and arbitrary values included: Tailwind reads the list the way it reads a
        // page, and `.dark\:bg-white\/10` is a selector, not a class name.
        Assert.Contains("dark:bg-white/10", KitConsumer.Classes);
        Assert.Contains("btn", KitConsumer.Classes);
        Assert.DoesNotContain(KitConsumer.Classes, name => name.Contains('\\', StringComparison.Ordinal));
    }

    // One compile per question, shared: each run of the engine costs most of a second, and a slow engine
    // would otherwise pay its timeout once per fact (#1079).
    private static readonly Lazy<string> _kitOnly = new(() => KitConsumer.Compile(KitConsumer.Import, markup: string.Empty));

    private static readonly Lazy<string> _withPage = new(() => KitConsumer.Compile(
        KitConsumer.Import,
        "navbar bg-base-100 shadow-sm hero bg-base-200 py-16 card card-body card-actions btn btn-primary footer px-8"));

    // Every class with a rule of its own, read the way a reader would: `.name` opening a selector.
    private static HashSet<string> ClassesDefinedIn(string css) =>
        Regex.Matches(css, @"(?<![\w\\-])\.((?:\\.|[A-Za-z0-9_-])+)(?=[^{};]*\{)")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"))
            .Where(name => !char.IsDigit(name[0]))
            .ToHashSet(StringComparer.Ordinal);

    private static string ReadTargets()
    {
        var path = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "build", "Rask.Ui.targets");
        Assert.True(File.Exists(path), $"the kit's targets moved: {path}");

        // Comments stripped: this file documents the hazards it guards against, and a test that reads
        // prose fails on the explanation of the thing it is checking for.
        return Regex.Replace(File.ReadAllText(path), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
    }

    /// <summary>
    ///     A project with nothing in it but the kit's targets, a reference that says it draws with the kit,
    ///     and a stylesheet — so what is run is the delivery and none of the SDK.
    /// </summary>
    private sealed class ConsumerProject : IDisposable
    {
        public ConsumerProject(string sheet, string properties = "")
        {
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "Styles"));
            File.WriteAllText(Path.Combine(Directory, "Styles", "app.css"), sheet);
            File.WriteAllText(Path.Combine(Directory, "App.proj"), $"""
                <Project>
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IntermediateOutputPath>obj/</IntermediateOutputPath>
                    <RaskTailwindInput>Styles/app.css</RaskTailwindInput>
                    {properties}
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Rask.Ui"/>
                  </ItemGroup>
                  <Import Project="{Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "build", "Rask.Ui.targets")}"/>
                  <Target Name="BeforeBuild"/>
                  <Target Name="GetAssemblyAttributes"/>
                  <Target Name="Build" DependsOnTargets="BeforeBuild;GetAssemblyAttributes"/>
                </Project>
                """);
        }

        public string Directory { get; } =
            Path.Combine(Path.GetTempPath(), "rask-kit-delivery-" + Guid.NewGuid().ToString("N")[..8]);

        public KitConsumer.Finished Build() =>
            KitConsumer.Run("dotnet", Directory, "msbuild", "App.proj", "-nologo", "-nodeReuse:false", "-t:Build", "-getItem:AssemblyMetadata");

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { /* left behind on a locked file */ }
        }
    }
}

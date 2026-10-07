using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.E2E.Tests;

/// <summary>
/// The build-the-output gate: generate every build-affecting flag combination and prove it actually compiles
/// against <b>this commit's</b> Rask packages, packed to a local feed. This packs the repo, restores, and runs
/// the full C# build, so it's opt-in — set <c>RASK_CLI_BUILD_E2E=1</c> to run it, which
/// <c>scripts/run-cli-build-e2e.sh</c> does, by hand and as CI's "CLI build" job.
/// The exhaustive file/shape assertions live in <see cref="ProjectGeneratorTests"/>
/// and always run. The pack + build plumbing lives in <see cref="CliBuildE2E"/>, shared with
/// <see cref="TutorialWalkthroughE2ETests"/> so the feed is packed once per session.
/// </summary>
public sealed class ProjectGeneratorBuildE2ETests
{
    // docker doesn't affect the build (just adds Dockerfile/.dockerignore), so the 2 build-relevant flags
    // give 2² = 4 combinations — every scenario, per the "test every scenario" directive. Auth used to be
    // a third: it is not a flag any more, and a parameter that no longer changes the output is worse than
    // no parameter, because it doubles the runs while pretending to cover something.
    public static IEnumerable<object[]> BuildAffectingCombinations()
    {
        for (var mask = 0; mask < 4; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0];
        }
    }

    [Theory]
    [MemberData(nameof(BuildAffectingCombinations))]
    public async Task Generated_server_project_builds(bool pwa, bool cqrs)
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = $"E2E{(pwa ? "P" : "")}{(cqrs ? "Q" : "")}";
        if (name == "E2E")
        {
            name = "E2ENone";
        }

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Pwa = pwa, Cqrs = cqrs }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[pwa={pwa},cqrs={cqrs}] generated project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// A bare <c>rask new</c> promises a green <c>dotnet test</c>: the scaffolded <c>&lt;name&gt;.Tests</c>
    /// project restores, builds against the app, and its one test passes. Only a real run proves the
    /// chain entry for the app's <c>HomePage</c> reaches the test project and that the app's own build
    /// keeps the test folder out of its globs.
    /// </summary>
    [Fact]
    public async Task Generated_server_project_passes_its_own_test()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "E2ETests";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var batteries = BatterySelection.ToBatteries(TemplateCatalog.Default, []);
            Assert.True(batteries.Tests, "a bare rask new no longer scaffolds a test project");

            var result = ProjectGenerator.GenerateServer(projectDir, name, batteries, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"test \"{Path.Combine(projectDir, name + ".slnx")}\" -m:1");
            Assert.True(exit == 0, $"the scaffolded tests did not pass.{CliBuildE2E.Diagnostics(output)}\n{output}");
            Assert.Matches(@"Passed:\s+1\b", output);
            Assert.Matches(@"Total:\s+1\b", output);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// Plain styling — what you get without <c>--bootstrap</c> — swaps every generated page body for plain
    /// elements and drops the Rask.Bootstrap reference. That is the one flag where the *code* differs rather than the wiring, so
    /// it is the one a string assertion proves least about: the Bs-free bodies have to compile without the
    /// package that supplies <c>BsCard</c> and <c>BootstrapStyles</c>, on both the welcome page and the
    /// error page, and the reference has to actually be gone rather than merely unused.
    /// </summary>
    [Fact]
    public async Task Generated_project_without_bootstrap_builds()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "E2ENoBs";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries(), version);

            Assert.DoesNotContain("Rask.Bootstrap", result.Packages);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            Assert.DoesNotContain("Rask.Bootstrap", fs.ReadAllText(Path.Combine(projectDir, name + ".csproj")), StringComparison.Ordinal);

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"--no-bootstrap project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// <c>--data</c> leaves the database to RaskApp: no context file, one <c>Rask.Server</c> reference. Only a
    /// real compile proves the scaffold's pages, its <c>User</c> and the Program.cs off-switches resolve
    /// against the package as shipped.
    /// </summary>
    [Fact]
    public async Task Generated_data_server_project_builds()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "DE2ESQ";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Data = true }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[data] generated project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// A localized browser-WASM project, compiled for real.
    /// </summary>
    /// <remarks>
    /// The part no unit test can reach. Emitting the catalogs is only half of it: the typed <c>Strings</c>
    /// members come from a source generator fed by an <c>&lt;AdditionalFiles&gt;</c> glob that
    /// <c>Rask.Core.targets</c> owns, and nothing had ever run that path on a <c>net10.0-browser</c> TFM
    /// where the compilation is trimmed, invariant-globalization-adjacent and built by the WebAssembly SDK
    /// rather than the web one. A compile is the only thing that proves the catalog reached the generator
    /// and that <c>&lt;RaskGlobalization&gt;</c> did not upset the rest of the build (#846).
    /// </remarks>
    [Fact]
    public async Task Generated_localized_wasm_project_builds()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "WLocE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            _ = TemplateCatalog.TryGet("wasm", out var template);

            // Two languages rather than one, because a single neutral catalog would not exercise the
            // fallback the second one is there to prove (RASK052 on a key it does not carry).
            //
            // Constructed rather than flagged: there is no --culture any more (#854), and a browser app
            // that wants a second language adds it in Program.cs and uncomments RaskGlobalization. This
            // is the state that produces, and it still has to BUILD.
            var batteries = new ServerBatteries
            {
                Localization = true,
                CultureList = "en,hu",
                Docker = BatterySelection.ToBatteries(template, []).Docker,
            }.Normalized();
            Assert.True(batteries.Localization, "the localized wasm shape was not constructed");

            var result = ProjectGenerator.GenerateWasm(
                projectDir, name, batteries.Pwa, batteries.Docker, version, batteries);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var target = Path.Combine(projectDir, name + ".csproj");

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{target}\" -warnaserror -m:1");
            Assert.True(
                exit == 0,
                $"a localized WASM project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    // pwa is the only build-affecting flag left for wasm (docker only adds files). Auth used to be the
    // other one; a standalone browser app has no endpoints of its own to authenticate against, so it
    // pairs with a Rask server and calls AddRaskAuthClient() rather than scaffolding a token store.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generated_wasm_project_builds(bool pwa)
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = pwa ? "WE2EP" : "WE2ENone";

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateWasm(projectDir, name, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[pwa={pwa}] generated wasm project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// A default project: every battery on, and a one-line <c>Program.cs</c>. Only a real compile proves the
    /// scaffold's pages, accounts and <c>RaskApp.Create(args).Run&lt;App&gt;()</c> resolve together.
    /// </summary>
    [Fact]
    public async Task Generated_default_server_project_builds()
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "AllBatteriesE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name,
                BatterySelection.ToBatteries(TemplateCatalog.Default, []), version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"a default `rask new` project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }


    /// <summary>
    /// Scoped CSS, proven from the far side of a <c>dotnet pack</c> — the only place it can be proven.
    /// <para>
    /// The generator reads nothing but <c>@(AdditionalFiles)</c>, and the globs that populate it live in
    /// <c>Rask.Core.targets</c>, which for a long time reached no consumer at all: Rask.Core is
    /// <c>IsPackable=false</c> so its own pack item was inert, and the host packages packed only their own
    /// <c>build/</c> folder. Every in-repo project imports the file directly through
    /// <c>Directory.Build.targets</c>, so samples, tests and E2E were all immune, and scoped CSS silently
    /// did nothing in every scaffolded app (#544). The structural half of the guard is
    /// <see cref="PackagingContractTests"/>, in the default gate; this is the behavioural half.
    /// </para>
    /// <para>
    /// Both directions are asserted, because each catches a different way of getting it wrong. The positive
    /// proves the glob reached the consumer and the generator emitted a registration. The negative — an
    /// orphan <c>.css</c> must fail with <b>RASK015</b>, which is a <c>DiagnosticSeverity.Error</c> and so
    /// cannot be masked — proves the glob is actually feeding the analyzer rather than the build merely
    /// happening to succeed. Before the fix the positive silently failed and the negative silently passed.
    /// </para>
    /// </summary>
    /// <summary>
    /// A scoped <c>.ts</c> sibling compiles and registers in a scaffolded project built against the
    /// PACKED framework, and a stray <c>.js</c> sibling fails the build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The TypeScript half of <see cref="Scoped_css_sibling_is_picked_up_from_the_package" />, and it has
    /// more moving parts to lose: the <c>**\*.ts</c> glob has to reach the consumer, the packed
    /// <c>Rask.TypeScript.Tasks.dll</c> has to load, its resolver has to fetch tsgo, the compile has to run
    /// before <c>CoreCompile</c>, and the compiled output has to arrive as an <c>AdditionalFile</c> carrying
    /// the original <c>.ts</c> path as metadata. In-repo <c>ProjectReference</c>s hide every one of those
    /// failures, because in-repo everything is already on disk and already built.
    /// </para>
    /// <para>
    /// Both directions again. The positive proves the chain end to end, down to the emitted registration
    /// containing the compiled body — a compile that silently produced nothing would still register a
    /// class, so the assertion is on the CONTENT. The negative proves RASK055 actually fires for a consumer:
    /// the whole point of the no-opt-out decision is that a <c>.js</c> sibling stops the build, and a rule
    /// that only fires in-repo would be the decision in name only.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_typescript_sibling_compiles_and_registers_from_the_package(bool wasm)
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = wasm ? "WTsE2E" : "TsE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = wasm
                ? ProjectGenerator.GenerateWasm(projectDir, name, pwa: false, docker: false, version)
                : ProjectGenerator.GenerateServer(projectDir, name, new ServerBatteries(), version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            // Written here rather than relied on from the scaffold, for the same reason as the .css case: a
            // guard that survives only as a side effect of scaffold contents is one a future trim deletes in
            // silence.
            //
            // The annotation is the point. It has to be STRIPPED by the compile — if the raw TypeScript
            // reached the browser it would be a syntax error at load, and nothing on the .NET side would
            // notice.
            var typescript = Path.Combine(projectDir, "Features", "Home", "HomePage.ts");
            fs.WriteAllText(
                typescript,
                """
                export function scopedProbe(label: string): string {
                    return `rask-scoped-probe:${label}`;
                }
                """);

            var generated = Path.Combine(temp, "generated");
            var csproj = Path.Combine(projectDir, name + ".csproj");
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{csproj}\" -warnaserror -m:1 -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=\"{generated}\"");
            Assert.True(exit == 0, $"[wasm={wasm}] project with a scoped .ts failed to build.{CliBuildE2E.Diagnostics(output)}");

            var registration = Directory
                .EnumerateFiles(generated, "__RaskScopedJsRegistration.g.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            Assert.True(
                registration is not null,
                $"[wasm={wasm}] no __RaskScopedJsRegistration.g.cs was emitted — the **\\*.ts glob never "
                + "reached the consumer, so scoped TypeScript is dead in scaffolded apps.");

            var emitted = await File.ReadAllTextAsync(registration!, TestContext.Current.CancellationToken);
            Assert.Contains("RegisterJs(typeof(", emitted, StringComparison.Ordinal);
            Assert.Contains("rask-scoped-probe", emitted, StringComparison.Ordinal);

            // Compiled, not copied. `: string` surviving would mean the raw .ts was registered — a syntax
            // error in every browser that loaded it, and invisible to every assertion above this one.
            Assert.DoesNotContain("label: string", emitted, StringComparison.Ordinal);

            // And still the form ScopedAssetRegistry parses: it strips a leading `export` and collects the
            // names to hang on window.Rask[Type]. esbuild's output would rewrite this into a trailing
            // `export { ... }` clause and register nothing at all, silently, in the browser only.
            Assert.Contains("export function scopedProbe(", emitted, StringComparison.Ordinal);

            // Negative control. A .js sibling is RASK055, which has no opt-out — so a consumer who was
            // writing scoped JavaScript yesterday is told, rather than finding their asset ignored.
            fs.WriteAllText(Path.Combine(projectDir, "Features", "Home", "HomePage.js"), "export function stale() {}");

            var (strayExit, strayOutput) = await CliBuildE2E.RunDotnet($"build \"{csproj}\" -m:1");
            Assert.True(
                strayExit != 0,
                $"[wasm={wasm}] a .js sibling built cleanly — RASK055 never fired for a consumer.{CliBuildE2E.Diagnostics(strayOutput)}");
            Assert.Contains("RASK055", strayOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("RASK_KEEP_E2E_TEMP") == "1")
            {
                Console.WriteLine($"[kept] {temp}");
            }
            else
            {
                CliBuildE2E.TryDeleteDirectory(temp);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_css_sibling_is_picked_up_from_the_package(bool wasm)
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = wasm ? "WCssE2E" : "CssE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = wasm
                ? ProjectGenerator.GenerateWasm(projectDir, name, pwa: false, docker: false, version)
                : ProjectGenerator.GenerateServer(projectDir, name, new ServerBatteries(), version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            // The scaffold ships no .css of its own (HomePage is styled with Bootstrap), so the sibling is
            // written here rather than relied on from the template — a guard that survives only as a side
            // effect of scaffold contents is one a future scaffold trim deletes in silence.
            var css = Path.Combine(projectDir, "Features", "Home", "HomePage.css");
            fs.WriteAllText(css, ".rask-scoped-probe { color: rebeccapurple; }");

            var generated = Path.Combine(temp, "generated");
            var csproj = Path.Combine(projectDir, name + ".csproj");
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{csproj}\" -warnaserror -m:1 -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=\"{generated}\"");
            Assert.True(exit == 0, $"[wasm={wasm}] project with a scoped .css failed to build.{CliBuildE2E.Diagnostics(output)}");

            var registration = Directory
                .EnumerateFiles(generated, "__RaskScopedCssRegistration.g.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            Assert.True(
                registration is not null,
                $"[wasm={wasm}] no __RaskScopedCssRegistration.g.cs was emitted — the **\\*.css glob never reached the consumer, so scoped CSS is dead in scaffolded apps.");

            var emitted = await File.ReadAllTextAsync(registration!, TestContext.Current.CancellationToken);
            Assert.Contains("RegisterCss(typeof(", emitted, StringComparison.Ordinal);
            Assert.Contains("rask-scoped-probe", emitted, StringComparison.Ordinal);

            // Negative control. RASK015 fires only if the orphan .css is actually in @(AdditionalFiles);
            // a build that succeeds here means the glob is absent, which is the defect wearing a green tick.
            fs.WriteAllText(Path.Combine(projectDir, "Orphan.css"), ".orphan { color: red; }");

            var (orphanExit, orphanOutput) = await CliBuildE2E.RunDotnet($"build \"{csproj}\" -m:1");
            Assert.True(
                orphanExit != 0,
                $"[wasm={wasm}] an orphan .css built cleanly — RASK015 never fired, so the glob is not feeding the analyzer.{CliBuildE2E.Diagnostics(orphanOutput)}");
            Assert.Contains("RASK015", orphanOutput, StringComparison.Ordinal);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    ///     Tailwind actually compiles, on an ASP.NET host and on a browser-WASM one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The WASM half is the reason this exists. <c>Rask.Tailwind</c> hooks <c>BeforeBuild</c> and
    ///         shells out to a native compiler with the project directory as its working directory; that
    ///         this survives <c>Microsoft.NET.Sdk.WebAssembly</c> — a different SDK, a different target
    ///         framework, and a publish pipeline that rewrites <c>wwwroot</c> — was an assumption until
    ///         something built it (#838).
    ///     </para>
    ///     <para>
    ///         The assertion is a <b>utility class from the scaffolded page</b>, not the file's existence
    ///         and not the exit code. Tailwind v4 detects its own sources relative to where it runs, so the
    ///         way this fails is an almost-empty stylesheet from a build that reported success — which is
    ///         indistinguishable from working unless something reads the output.
    ///     </para>
    ///     <para>
    ///         Needs the network on a cold cache: the compiler is fetched once from Tailwind's releases and
    ///         cached per user. Gated with the other build E2Es, so a plain `dotnet test` never reaches it.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tailwind_compiles_the_scaffolded_pages_utilities(bool wasm)
    {
        Assert.SkipUnless(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = wasm ? "WTwE2E" : "TwE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var batteries = new ServerBatteries();
            var result = wasm
                ? ProjectGenerator.GenerateWasm(
                    projectDir, name, pwa: false, docker: false, version, batteries)
                : ProjectGenerator.GenerateServer(projectDir, name, batteries, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var csproj = Path.Combine(projectDir, name + ".csproj");
            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{csproj}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[wasm={wasm}] a --tailwind project failed to build.{CliBuildE2E.Diagnostics(output)}");

            var stylesheet = Path.Combine(projectDir, "wwwroot", "css", "app.css");
            Assert.True(
                File.Exists(stylesheet),
                $"[wasm={wasm}] the build reported success but wrote no {stylesheet} — the Tailwind target never ran.{CliBuildE2E.Diagnostics(output)}");

            var css = await File.ReadAllTextAsync(stylesheet, TestContext.Current.CancellationToken);

            // From HomePage.cs's own markup. If v4 scanned the wrong tree this file is still written, still
            // valid CSS, and carries none of the classes the page actually uses.
            Assert.Contains("max-w-md", css, StringComparison.Ordinal);
            Assert.Contains("tracking-tight", css, StringComparison.Ordinal);

            // A class nothing in the project writes must NOT be there: the positive alone would also pass
            // against a stylesheet that shipped all of Tailwind, which is the other way to get this wrong.
            Assert.DoesNotContain("skew-x-12", css, StringComparison.Ordinal);

            // daisyUI itself, compiled HERE from the plugin Rask.Ui ships — no npm, no node_modules.
            // This is the whole claim: without it the page's card/btn/navbar are correct strings in the
            // markup naming rules that exist nowhere, and the app renders as unstyled text.
            foreach (var component in (string[])["card", "card-body", "btn", "navbar", "hero", "footer"])
            {
                Assert.True(
                    Regex.IsMatch(css, $@"(^|[\s,}}]) *\.{component}\s*\{{", RegexOptions.Multiline),
                    $"[wasm={wasm}] .{component} is not in the compiled sheet, so the starter page "
                    + $"renders unstyled.{CliBuildE2E.Diagnostics(output)}");
            }

            // And the plugin must not have been scanned as a safelist: nothing here, and nothing in the
            // kit, writes `glass`.
            Assert.False(
                Regex.IsMatch(css, @"(^|[\s,}]) *\.glass\s*\{", RegexOptions.Multiline),
                $"[wasm={wasm}] the sheet carries components nothing names, so vendor/daisyui.mjs is "
                + "being scanned and this stylesheet is the whole library.");

            // ONE sheet, with the kit in it: a class only a kit component writes is compiled HERE, from
            // the list the kit ships, beside the starter page's own.
            Assert.True(
                Regex.IsMatch(css, @"\.dark\\:bg-white\\/10\b", RegexOptions.Multiline),
                $"[wasm={wasm}] the kit's classes are not in the app's sheet, so every Ui* component "
                + $"would render unstyled.{CliBuildE2E.Diagnostics(output)}");

            // The kit's Tailwind sources are copied into the tree by the build; they are not the author's.
            foreach (var source in (string[])["rask-ui.css", "rask-ui.kit.css", "rask-ui.classes.txt", "daisyui.mjs"])
            {
                Assert.True(
                    File.Exists(Path.Combine(projectDir, "Styles", "vendor", source)),
                    $"[wasm={wasm}] Styles/vendor/{source} was not written, so the import in Styles/app.css "
                    + $"resolved to a file that is not there.{CliBuildE2E.Diagnostics(output)}");
            }

            // And NOT the kit's precompiled sheet beside it: two sheets are two `@layer utilities` ranked
            // by link order, where the app's `bg-white` beats the kit's `dark:bg-zinc-800`.
            Assert.False(
                File.Exists(Path.Combine(projectDir, "wwwroot", "css", "rask-ui.css")),
                $"[wasm={wasm}] the kit's precompiled sheet was written into wwwroot beside the app's own.");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }
}

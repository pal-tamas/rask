using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The build-the-output gate: generate every build-affecting flag combination and prove it actually compiles
/// against <b>this commit's</b> Rask packages, packed to a local feed. This packs the repo, restores, and runs
/// the full C# build, so it's opt-in — set <c>RASK_CLI_BUILD_E2E=1</c> to run it (matches the repo's "tests run
/// locally, not in CI" model). The exhaustive file/shape assertions live in <see cref="ProjectGeneratorTests"/>
/// and always run. The pack + build plumbing lives in <see cref="CliBuildE2E"/>, shared with
/// <see cref="TutorialWalkthroughE2ETests"/> so the feed is packed once per session.
/// </summary>
public sealed class ProjectGeneratorBuildE2ETests
{
    // docker doesn't affect the build (just adds Dockerfile/.dockerignore), so the 3 build-relevant flags
    // give 2³ = 8 combinations — every scenario, per the "test every scenario" directive.
    public static IEnumerable<object[]> BuildAffectingCombinations()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0];
        }
    }

    [SkippableTheory]
    [MemberData(nameof(BuildAffectingCombinations))]
    public async Task Generated_server_project_builds(bool auth, bool pwa, bool cqrs)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = $"E2E{(auth ? "A" : "")}{(pwa ? "P" : "")}{(cqrs ? "Q" : "")}";
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
                projectDir, name, new ServerBatteries { Auth = auth, Pwa = pwa, Cqrs = cqrs }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa},cqrs={cqrs}] generated project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// <c>--no-bootstrap</c> swaps every generated page body for plain elements and drops the
    /// Rask.Bootstrap reference. That is the one flag where the *code* differs rather than the wiring, so
    /// it is the one a string assertion proves least about: the Bs-free bodies have to compile without the
    /// package that supplies <c>BsCard</c> and <c>BootstrapStyles</c>, on both the welcome page and the
    /// error page, and the reference has to actually be gone rather than merely unused.
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generated_project_without_bootstrap_builds(bool auth)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = auth ? "E2ENoBsAuth" : "E2ENoBs";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Bootstrap = false, Auth = auth }, version);

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
            Assert.True(exit == 0, $"[auth={auth}] --no-bootstrap project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// <c>--data</c> pre-wires the AppDbContext + AddRaskData + a UseRaskSqlite DbContext factory, and pulls
    /// Rask.Data / Rask.SQLite.EntityFrameworkCore into the csproj. Only a real compile proves the generated
    /// Program.cs (the config-driven connection string, the ISaveChangesInterceptor injection) and the
    /// AppDbContext resolve — both alone and composed with <c>--auth</c> (which shares the same Program.cs).
    /// </summary>
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generated_data_server_project_builds(bool auth)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = $"DE2E{(auth ? "A" : "")}SQ";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Auth = auth, Data = true }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[data,auth={auth}] generated project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    // wasm build-affecting flags are auth/pwa (docker only adds files) → 2² = 4 combinations.
    public static IEnumerable<object[]> WasmBuildAffectingCombinations()
    {
        for (var mask = 0; mask < 4; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0];
        }
    }

    [SkippableTheory]
    [MemberData(nameof(WasmBuildAffectingCombinations))]
    public async Task Generated_wasm_project_builds(bool auth, bool pwa)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = $"WE2E{(auth ? "A" : "")}{(pwa ? "P" : "")}";
        if (name == "WE2E")
        {
            name = "WE2ENone";
        }

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateWasm(projectDir, name, auth, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm project failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    [SkippableTheory]
    [MemberData(nameof(WasmBuildAffectingCombinations))]
    public async Task Generated_wasm_hosted_solution_builds(bool auth, bool pwa)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var name = $"HE2E{(auth ? "A" : "")}{(pwa ? "P" : "")}";
        if (name == "HE2E")
        {
            name = "HE2ENone";
        }

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateWasmHosted(projectDir, name, auth, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            // Build the Server project, not the .sln.
            //
            // The Server references both the Client and the Shared library, so all three still compile and
            // the WASM bundle is still baked — but building the solution put the Client in the restore graph
            // *twice*: once as a solution entry, and once as the Server's cross-TFM ProjectReference
            // (ReferenceOutputAssembly=false, SkipGetTargetFrameworkProperties=true, so its TFM is never
            // negotiated). Those two graph entries race on the Client's obj/ restore artefacts and fail with
            // "The file '…project.assets.json' already exists".
            //
            // `-m:1` doesn't help — it caps MSBuild nodes, not NuGet's own parallelism — and neither does
            // splitting restore from build, because both entries are present within the single restore.
            // Dropping the duplicate entry is what removes the racing writer. The .sln's own shape stays
            // covered by ProjectGeneratorTests.
            var server = Path.Combine(projectDir, name + ".Server", name + ".Server.csproj");
            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{server}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm-hosted solution failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }


    /// <summary>
    /// <c>--all-batteries</c>: every One Person Framework pillar wired into one app. Only a real compile
    /// proves the composed <c>Program.cs</c> — a dozen registrations, their usings, the config-gated
    /// Litestream block, the <c>await</c> in top-level statements, the push endpoints — and the
    /// <c>AppDbContext</c> that carries four framework schemas actually resolve together.
    /// </summary>
    [SkippableFact]
    public async Task Generated_all_batteries_server_project_builds()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "AllBatteriesE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, NewCommand.ToBatteries(["all-batteries", "auth", "docker"]), version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"generated --all-batteries project failed to build.{CliBuildE2E.Diagnostics(output)}");
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
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_css_sibling_is_picked_up_from_the_package(bool wasm)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = wasm ? "WCssE2E" : "CssE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = wasm
                ? ProjectGenerator.GenerateWasm(projectDir, name, auth: false, pwa: false, docker: false, version)
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

            var emitted = await File.ReadAllTextAsync(registration!);
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
}

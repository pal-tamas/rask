using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

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
    /// Plain styling — what you get without <c>--bootstrap</c> — swaps every generated page body for plain
    /// elements and drops the Rask.Bootstrap reference. That is the one flag where the *code* differs rather than the wiring, so
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
                projectDir, name, new ServerBatteries { Styling = Styling.Plain, Auth = auth }, version);

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
    [SkippableFact]
    public async Task Generated_localized_wasm_project_builds()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "WLocE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            _ = TemplateCatalog.TryGet("wasm", out var template);

            // Two languages rather than one, because a single neutral catalog would not exercise the
            // fallback the second one is there to prove (RASK052 on a key it does not carry).
            var batteries = NewCommand.ToBatteries(template, [], cultures: ["en", "hu"]);
            Assert.True(batteries.Localization, "--culture did not turn localization on for wasm");

            var result = ProjectGenerator.GenerateWasm(
                projectDir, name, batteries.Auth, batteries.Pwa, batteries.Docker, version, batteries);

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



    /// <summary>
    ///     The <c>react</c> template's host, built with the generated-TypeScript path live.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A stand-in <c>package.json</c> is written where <c>create-vite</c> would have put one. That is
    ///         what makes the <c>.Server</c>/<c>.Client</c> convention resolve, which is what turns the
    ///         TypeScript emit on — so this gate covers the generator writing its constants into the assembly
    ///         AND the MSBuild task reading them back out and landing the files in the client's sources. The
    ///         real scaffolder is not run: it needs node and a network, and what it produces is not what
    ///         this is testing.
    ///     </para>
    ///     <para>
    ///         <c>RaskSpaBuild=false</c> for the same reason — the bundler is somebody else's program, and
    ///         the emit is deliberately independent of it, because <c>rask dev</c> runs the bundler itself
    ///         and still needs contracts that match the server it is talking to.
    ///     </para>
    /// </remarks>
    [SkippableTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    // --push reaches the .Server half: the Rask.WebPush reference, the VAPID block, a re-namespaced
    // PushSubscriptions.cs and app.MapPushSubscriptions(). All four are C#, and a namespace rewritten
    // into the wrong project is a compile error nothing else in the suite would see.
    [InlineData(false, true)]
    public async Task Generated_react_solution_builds(bool data, bool push)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var name = push ? "RE2EPush" : data ? "RE2EData" : "RE2ENone";
        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateSpa(
                projectDir, name, SpaFramework.React,
                new ServerBatteries { Data = data, Push = push }.Normalized(), version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // Both files, because both are load-bearing: package.json is what makes the .Server/.Client
            // convention resolve, and tsconfig.json is what satisfies RASKSPA004 — the build's refusal to
            // generate TypeScript contracts into a client that is not a TypeScript project.
            var client = Path.Combine(projectDir, name + ".Client");
            fs.CreateDirectory(client);
            fs.WriteAllText(Path.Combine(client, "package.json"), """{ "name": "stand-in", "private": true }""");

            // A tsconfig.json beside it, because a TypeScript client is what this package supports and the
            // build says so (RASKSPA004). create-vite's react-ts template writes one; the stand-in has to
            // model that, or this gate would be testing a client the real template never produces.
            fs.WriteAllText(Path.Combine(client, "tsconfig.json"), """{ "compilerOptions": { "strict": true } }""");
            fs.WriteAllText(Path.Combine(client, "tsconfig.json"), """{ "files": [] }""");

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var server = Path.Combine(projectDir, name + ".Server", name + ".Server.csproj");
            var (exit, output) = await CliBuildE2E.RunDotnet(
                $"build \"{server}\" -warnaserror -m:1 -p:RaskSpaBuild=false");
            Assert.True(exit == 0, $"[data={data}] generated react solution failed to build.{CliBuildE2E.Diagnostics(output)}");

            // The whole point of the template: the front end's contracts come out of the server's own
            // message records. A build that compiles but writes nothing here leaves the client importing
            // the previous build's types, which type-checks and then breaks on the wire.
            var generated = Path.Combine(client, "src", "rask");
            Assert.True(
                File.Exists(Path.Combine(generated, "contracts.ts")),
                $"the build wrote no contracts.ts into {generated}.{CliBuildE2E.Diagnostics(output)}");
            Assert.True(File.Exists(Path.Combine(generated, "messages.ts")), "the build wrote no messages.ts.");
            Assert.True(
                File.Exists(Path.Combine(generated, "client.ts")),
                "the dispatcher was not refreshed from the package, so messages.ts imports nothing.");

            var contracts = await File.ReadAllTextAsync(Path.Combine(generated, "contracts.ts"));
            Assert.Contains("export interface Greeting", contracts, StringComparison.Ordinal);
            Assert.Contains("seenAt: Date;", contracts, StringComparison.Ordinal);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    ///     A resolved client that is not TypeScript fails the build, naming RASKSPA004.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Rask supports TypeScript single-page app clients. The refusal is the feature: a JavaScript
    ///         client can import the generated <c>.ts</c> — Vite transpiles it whatever the project is — and
    ///         receives none of what it is for, so the build would succeed, the types would be checked by
    ///         nobody, and a renamed C# property would surface on the wire instead of at a compiler.
    ///     </para>
    ///     <para>
    ///         Only a real build proves this one. The check lives in MSBuild, ahead of the compile, and a
    ///         test over the generator's output cannot see a target that never ran.
    ///     </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_client_that_is_not_typescript_is_refused()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string name = "RE2ENoTs";
        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateSpa(
                projectDir, name, SpaFramework.React, new ServerBatteries(), version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // package.json and no tsconfig.json: the convention resolves the client, the contract emit turns
            // itself on, and there is nothing on the other side able to check what it writes.
            var client = Path.Combine(projectDir, name + ".Client");
            fs.CreateDirectory(client);
            fs.WriteAllText(Path.Combine(client, "package.json"), """{ "name": "stand-in", "private": true }""");

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var server = Path.Combine(projectDir, name + ".Server", name + ".Server.csproj");
            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{server}\" -m:1 -p:RaskSpaBuild=false");

            Assert.True(exit != 0, $"a JavaScript client built anyway.{CliBuildE2E.Diagnostics(output)}");
            Assert.Contains("RASKSPA004", output, StringComparison.Ordinal);

            // And it fails BEFORE writing anything into the client: a half-generated src/rask is exactly the
            // state that makes the next build look like it succeeded.
            Assert.False(
                Directory.Exists(Path.Combine(client, "src", "rask")),
                "the refused build still wrote contracts into the client.");

            // The escape hatch is real: no contracts, no requirement, and the host still serves a bundle.
            var (hatchExit, hatchOutput) = await CliBuildE2E.RunDotnet(
                $"build \"{server}\" -warnaserror -m:1 -p:RaskSpaBuild=false -p:RaskEmitTypeScript=false");
            Assert.True(
                hatchExit == 0,
                $"RaskEmitTypeScript=false did not lift the requirement.{CliBuildE2E.Diagnostics(hatchOutput)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }



    /// <summary>
    /// A default project: every One Person Framework pillar wired into one app. Only a real compile
    /// proves the composed <c>Program.cs</c> — a dozen registrations, their usings, the config-gated
    /// Litestream block, the <c>await</c> in top-level statements, the push endpoints — and the
    /// <c>AppDbContext</c> that carries four framework schemas actually resolve together.
    /// </summary>
    [SkippableFact]
    public async Task Generated_default_server_project_builds()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "AllBatteriesE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name,
                NewCommand.ToBatteries(TemplateCatalog.Default, [], auth: true), version);

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
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_typescript_sibling_compiles_and_registers_from_the_package(bool wasm)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = wasm ? "WTsE2E" : "TsE2E";
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

            var emitted = await File.ReadAllTextAsync(registration!);
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
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tailwind_compiles_the_scaffolded_pages_utilities(bool wasm)
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var name = wasm ? "WTwE2E" : "TwE2E";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var batteries = new ServerBatteries { Styling = Styling.Tailwind };
            var result = wasm
                ? ProjectGenerator.GenerateWasm(
                    projectDir, name, auth: false, pwa: false, docker: false, version, batteries)
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

            var css = await File.ReadAllTextAsync(stylesheet);

            // From HomePage.cs's own markup. If v4 scanned the wrong tree this file is still written, still
            // valid CSS, and carries none of the classes the page actually uses.
            Assert.Contains("max-w-xl", css, StringComparison.Ordinal);
            Assert.Contains("tracking-tight", css, StringComparison.Ordinal);

            // A class nothing in the project writes must NOT be there: the positive alone would also pass
            // against a stylesheet that shipped all of Tailwind, which is the other way to get this wrong.
            Assert.DoesNotContain("max-w-3xl", css, StringComparison.Ordinal);
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }
}

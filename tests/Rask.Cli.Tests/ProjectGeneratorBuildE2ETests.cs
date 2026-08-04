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

        var name = $"DE2E{(auth ? "A" : "")}";
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

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, name + ".sln")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm-hosted solution failed to build.{CliBuildE2E.Diagnostics(output)}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// A feature run that names relationship targets emits several entities at once — each in its own folder
    /// and namespace, all sharing one DbContext that lives with the root. That shape is the first generated
    /// code to carry cross-namespace <c>using</c>s, which string assertions can't validate: a target's handlers
    /// name a DbContext declared in the root's namespace, and the DbContext names types in each target's.
    /// Only a real compile proves those resolve.
    /// </summary>
    [SkippableFact]
    public async Task Generated_multi_entity_feature_builds()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        const string Name = "FE2E";
        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, Name);
        try
        {
            var fs = new SystemFileSystem();

            var host = ProjectGenerator.GenerateServer(projectDir, Name, new ServerBatteries(), version);
            foreach (var file in host.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            var post = new EntitySpec("Post", "Posts", [new FieldSpec("Title", "string", IsNullable: false, MaxLength: 200)]);
            var comment = new EntitySpec("Comment", "Comments", [new FieldSpec("Body", "string", IsNullable: false, MaxLength: 200)]);
            var feature = FeatureGenerator.Generate(
                new ProjectContext(projectDir, Name),
                projectDir,
                new FeatureSpec(post, [new RelationshipSpec(Cardinality.OneToMany, IsOptional: false, post, comment)]),
                new FeatureOptions { IdType = "Guid", Validation = "valueobjects" });

            foreach (var file in feature.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // Compile-gate the WireProgramCs splice — the one edit that turns generated files into a running
            // app. Apply the real splice to the scaffolded Program.cs so the feature's DI (AddRaskCqrs /
            // AddRaskData / the DbContext factory + the usings they need) is actually built, not just
            // string-asserted. Without this the build proves the feature files compile but never the splice.
            var programPath = Path.Combine(projectDir, "Program.cs");
            var (splicedProgram, added) = GenerateCommand.SpliceProgramCs(
                fs.ReadAllText(programPath), feature.ProgramUsings, feature.ProgramRegistrations);
            Assert.NotEmpty(added); // the splice inserted the registrations
            fs.WriteAllText(programPath, splicedProgram);

            // `dotnet add package` is GenerateCommand's job, not the generator's — so add what the generator
            // says it needs. Driving off result.Packages keeps this from drifting from what the CLI does.
            CliBuildE2E.InjectPackages(fs, Path.Combine(projectDir, Name + ".csproj"), feature.Packages, version);
            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await CliBuildE2E.RunDotnet($"build \"{Path.Combine(projectDir, Name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"generated multi-entity feature failed to build.{CliBuildE2E.Diagnostics(output)}");
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
}

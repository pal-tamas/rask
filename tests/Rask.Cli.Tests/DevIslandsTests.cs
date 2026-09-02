using Rask.Cli;
using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask dev</c>'s island half: detecting a project worth running a Vite dev server for, and the
///     two things that tell the rest of the system about it — an MSBuild property and an environment
///     variable.
/// </summary>
/// <remarks>
///     Without this the edit loop for an island goes through MSBuild: <c>dotnet watch</c> sees the
///     file, rebuilds, and runs a production <c>vite build</c> over every island in the project. It
///     works, and it is far too slow to work in — and the page reloads, so whatever state the island
///     held is gone.
/// </remarks>
public class DevIslandsTests
{
    private const string ServerCsproj = """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""";

    [Fact]
    public void A_project_with_an_island_and_a_package_json_wants_a_dev_server()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/package.json", "{}");
        fs.Seed("/app/Features/Islands/Chart.tsx", "export default () => null");

        var target = DevTarget.Detect(fs, "/app", null);

        Assert.True(target!.HasIslands);

        // 5174, not Vite's 5173: that one belongs to the SPA client, and a solution with both would
        // have two dev servers fighting for one port.
        Assert.Equal("http://localhost:5174", target.IslandDevServerUrl);
    }

    [Fact]
    public void A_project_with_no_package_json_wants_nothing()
    {
        // The same gate the targets use. Without a package.json the bundler cannot run at all, so
        // there is nothing to serve however many .tsx files are lying around.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Features/Islands/Chart.tsx", "export default () => null");

        Assert.False(DevTarget.Detect(fs, "/app", null)!.HasIslands);
        Assert.Null(DevTarget.Detect(fs, "/app", null)!.IslandDevServerUrl);
    }

    [Fact]
    public void Build_output_does_not_count_as_an_island()
    {
        // obj/ holds the GENERATED entry modules, and node_modules holds every .tsx the world has ever
        // published. Counting either would start a dev server for a project that has no islands at all.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/package.json", "{}");
        fs.Seed("/app/obj/rask-external/entries/Chart.entry.ts", "");
        fs.Seed("/app/node_modules/react/index.tsx", "");
        fs.Seed("/app/wwwroot/vendor/thing.tsx", "");

        Assert.False(DevTarget.Detect(fs, "/app", null)!.HasIslands);
    }

    [Fact]
    public void A_lone_ts_file_only_counts_beside_a_cs_of_the_same_name()
    {
        // The Lit and Angular pairing rule. Without it every piece of scoped TypeScript in the project
        // would start a dev server.
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/package.json", "{}");
        fs.Seed("/app/Features/Home/Home.ts", "");

        Assert.False(DevTarget.Detect(fs, "/app", null)!.HasIslands);

        fs.Seed("/app/Features/Home/Home.cs", "");
        Assert.True(DevTarget.Detect(fs, "/app", null)!.HasIslands);
    }

    [Fact]
    public void An_explicit_port_in_the_csproj_wins()
    {
        var fs = new FakeFileSystem();
        fs.Seed(
            "/app/App.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><RaskExternalDevServerPort>6000</RaskExternalDevServerPort></PropertyGroup>
            </Project>
            """);
        fs.Seed("/app/package.json", "{}");
        fs.Seed("/app/Features/Islands/Chart.vue", "");

        Assert.Equal("http://localhost:6000", DevTarget.Detect(fs, "/app", null)!.IslandDevServerUrl);
    }

    [Fact]
    public void A_dev_session_with_islands_skips_the_production_bundle()
    {
        var args = DevCommand.BuildDotnetArguments(
            "/app/App.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.Server, islands: true);

        // NOT RaskExternalBuild=false. That switch turns the feature off outright — no entry modules,
        // no manifest, no prop types — and an app run that way has islands that never mount. This one
        // skips exactly the bundling step and leaves the manifest being written, pointing at the dev
        // server.
        Assert.Contains("--property:RaskExternalDevServer=true", args);
        Assert.DoesNotContain("--property:RaskExternalBuild=false", args);
    }

    [Fact]
    public void A_project_without_islands_builds_normally()
    {
        var args = DevCommand.BuildDotnetArguments(
            "/app/App.csproj", once: false, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.Server);

        Assert.DoesNotContain("--property:RaskExternalDevServer=true", args);
    }

    [Fact]
    public void Running_once_does_not_start_a_dev_server()
    {
        // --once is deliberately a plain `dotnet run` with nothing beside it, so the app has to serve a
        // real bundle or there is nothing to look at.
        var args = DevCommand.BuildDotnetArguments(
            "/app/App.csproj", once: true, noHotReload: false, launchProfile: null,
            nonInteractive: false, passthrough: [], kind: DevTemplateKind.Server, islands: true);

        Assert.DoesNotContain("--property:RaskExternalDevServer=true", args);
    }

    [Fact]
    public void The_dev_server_url_reaches_the_app_through_the_environment()
    {
        var env = DevCommand.BuildEnvironment(
            DevTemplateKind.Server, restartOnRudeEdit: false, urls: null, readEnv: _ => null,
            islandDevServerUrl: "http://localhost:5174");

        // The server stamps this onto <body>, which is how the island runtime learns to load
        // @vite/client — and therefore how anything hot-replaces at all.
        Assert.Equal("http://localhost:5174", env["RASK_ISLANDS_DEV"]);
    }

    [Fact]
    public void Running_once_does_not_stamp_the_dev_server_on_the_page_either()
    {
        // The URL is handed in exactly as a real --once run would hand it in — the project HAS islands.
        // What must suppress it is `once`, and nothing else: the property is already withheld there, so
        // stamping the page anyway would leave it importing @vite/client from a port nothing is
        // listening on, and a stale dev.json from an earlier session could point it somewhere worse.
        var env = DevCommand.BuildEnvironment(
            DevTemplateKind.Server, restartOnRudeEdit: false, urls: null, readEnv: _ => null,
            islandDevServerUrl: "http://localhost:5174", once: true);

        Assert.False(env.ContainsKey("RASK_ISLANDS_DEV"));
    }

    [Fact]
    public void A_run_without_islands_sets_no_dev_server_variable()
    {
        var env = DevCommand.BuildEnvironment(
            DevTemplateKind.Server, restartOnRudeEdit: false, urls: null, readEnv: _ => null);

        Assert.False(env.ContainsKey("RASK_ISLANDS_DEV"));
    }
}

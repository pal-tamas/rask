using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
///     <c>rask dev</c>'s argv and environment are both pure functions, asserted directly. The
///     environment matters as much as the argv: <c>dotnet watch</c> has no <c>--property</c> switch, so
///     the environment is the only way to reach the MSBuild property that stops a rude edit blocking on
///     an interactive prompt.
/// </summary>
public sealed class DevCommandTests
{
    private const string ServerCsproj = """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""";

    // ---- argv ----

    [Fact]
    public void Default_uses_dotnet_watch_run()
    {
        Assert.Equal(["watch", "run"], Args());
    }

    [Fact]
    public void Project_is_passed_to_watch()
    {
        Assert.Equal(
            ["watch", "--project", "src/App/App.csproj", "run"],
            Args(project: "src/App/App.csproj"));
    }

    [Fact]
    public void No_hot_reload_keeps_watching_and_is_a_watch_option()
    {
        // Previously this degraded to a plain `dotnet run`, which stopped watching entirely AND cleared
        // DOTNET_WATCH — turning off more framework behaviour than the flag's name claims. `--once` is
        // the honest name for that. The flag is a watch option, so it must precede `run`.
        Assert.Equal(["watch", "--no-hot-reload", "run"], Args(noHotReload: true));
    }

    [Fact]
    public void Once_runs_without_watching()
    {
        Assert.Equal(["run"], Args(once: true));
        Assert.Equal(["run", "--project", "App.csproj"], Args(project: "App.csproj", once: true));
    }

    [Fact]
    public void Launch_profile_is_a_watch_option_and_precedes_run()
    {
        Assert.Equal(["watch", "-lp", "Foo", "run"], Args(launchProfile: "Foo"));
    }

    [Fact]
    public void Non_interactive_is_added_when_there_is_no_terminal_to_prompt_on()
    {
        // Without it, watch's rude-edit prompt has nobody to answer it and blocks forever.
        Assert.Equal(["watch", "--non-interactive", "run"], Args(nonInteractive: true));
    }

    [Fact]
    public void Passthrough_is_appended_after_separator()
    {
        Assert.Equal(
            ["watch", "run", "--", "--urls", "http://localhost:1234"],
            Args(passthrough: ["--urls", "http://localhost:1234"]));
    }

    [Fact]
    public void Passthrough_help_is_forwarded_to_the_app_not_swallowed()
    {
        Assert.Equal(["watch", "run", "--", "--help"], Args(passthrough: ["--help"]));
    }

    [Fact]
    public void Watch_options_precede_run_and_passthrough_follows_the_separator()
    {
        // Position is the failure mode here, so assert the whole ordered list.
        Assert.Equal(
            ["watch", "--project", "App.csproj", "--non-interactive", "--no-hot-reload", "-lp", "Dev", "run", "--", "--flag"],
            Args("App.csproj", noHotReload: true, launchProfile: "Dev", nonInteractive: true, passthrough: ["--flag"]));
    }

    // ---- environment ----

    [Fact]
    public void Development_is_set_when_the_user_has_set_no_environment()
    {
        var env = Env(readEnv: _ => null);

        Assert.Equal("Development", env["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public void An_explicit_environment_is_never_clobbered()
    {
        var aspnet = Env(readEnv: k => k == "ASPNETCORE_ENVIRONMENT" ? "Staging" : null);
        var dotnet = Env(readEnv: k => k == "DOTNET_ENVIRONMENT" ? "Staging" : null);

        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", aspnet.Keys);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", dotnet.Keys);
    }

    [Fact]
    public void A_standalone_wasm_app_gets_no_aspnet_environment()
    {
        // There is no ASP.NET host in that template — the variable would be inert noise.
        Assert.DoesNotContain(
            "ASPNETCORE_ENVIRONMENT",
            Env(DevTemplateKind.WasmStandalone, readEnv: _ => null).Keys);
    }

    [Fact]
    public void A_wasm_hosted_app_does_get_one_because_it_runs_the_server_host()
    {
        Assert.Equal("Development", Env(DevTemplateKind.WasmHosted, readEnv: _ => null)["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public void Auto_restart_is_on_by_default_and_off_with_no_restart()
    {
        // HotReloadAutoRestart is an MSBuild property read from watch's design-time build; MSBuild picks
        // properties up from the environment, which is why this is set here and not as an argument.
        Assert.Equal("true", Env(restartOnRudeEdit: true)["HotReloadAutoRestart"]);
        Assert.DoesNotContain("HotReloadAutoRestart", Env(restartOnRudeEdit: false).Keys);
    }

    [Fact]
    public void Urls_becomes_ASPNETCORE_URLS()
    {
        Assert.Equal("http://localhost:5000", Env(urls: "http://localhost:5000")["ASPNETCORE_URLS"]);
        Assert.DoesNotContain("ASPNETCORE_URLS", Env().Keys);
    }

    [Fact]
    public void The_environment_is_an_overlay_not_a_replacement()
    {
        // The child inherits PATH/HOME from the parent; the overlay must not try to carry them.
        var env = Env(readEnv: _ => null);

        Assert.DoesNotContain("PATH", env.Keys);
        Assert.DoesNotContain("HOME", env.Keys);
    }

    // ---- wiring ----

    [Fact]
    public async Task Execute_forwards_the_command_line_and_the_environment()
    {
        var (command, runner, _) = Build();

        var exit = await command.ExecuteAsync(["--", "--flag"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal("dotnet", runner.LastRun!.FileName);
        Assert.Contains("watch", runner.LastRun.Arguments);
        // Without this, the pure environment test proves nothing about the wiring.
        Assert.Equal("true", runner.LastRun.Environment!["HotReloadAutoRestart"]);
    }

    [Fact]
    public async Task Unknown_option_fails_without_running()
    {
        var (command, runner, _) = Build();

        var exit = await command.ExecuteAsync(["--bogus"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task No_project_anywhere_reports_it_without_running()
    {
        var console = new StringConsole();
        var command = new DevCommand(
            console, new FakeProcessRunner(), new FakeFileSystem(), new FakeBrowserLauncher(), "/app");

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Couldn't find a .csproj", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_watch_session_tells_the_app_where_to_point_the_browser_for_build_status()
    {
        // The app stamps this URL onto every page it serves, so the browser still has it after the app
        // that served it is gone — which is exactly when a build failure needs reporting.
        var (command, runner, _) = Build();

        await command.ExecuteAsync([], CancellationToken.None);

        var url = runner.LastRun!.Environment![DevCommand.DevStatusEnvironmentVariable];
        Assert.StartsWith("http://127.0.0.1:", url, StringComparison.Ordinal);
        Assert.EndsWith("/status", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_one_shot_run_gets_no_status_server_because_nothing_will_rebuild()
    {
        var (command, runner, _) = Build();

        await command.ExecuteAsync(["--once"], CancellationToken.None);

        Assert.DoesNotContain(DevCommand.DevStatusEnvironmentVariable, runner.LastRun!.Environment!.Keys);
    }

    [Fact]
    public async Task Watch_output_is_read_and_reaches_the_status_endpoint()
    {
        var (command, runner, _) = Build();
        var error = "/app/A.cs(1,1): error CS0103: nope [/app/App.csproj]";
        runner.TeeLines = ["  Determining projects to restore...", error];

        // Asked while the run is still in flight: `rask dev` owns the status server for exactly as long
        // as it owns the child process, which is the correct lifetime and an untestable one from outside.
        var status = string.Empty;
        runner.DuringRunAsync = async env =>
        {
            using var client = new System.Net.Http.HttpClient();
            status = await client.GetStringAsync(env![DevCommand.DevStatusEnvironmentVariable]);
        };

        await command.ExecuteAsync([], CancellationToken.None);

        Assert.Contains("\"state\":\"failed\"", status, StringComparison.Ordinal);
        Assert.Contains("CS0103", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teed_output_still_reaches_the_terminal()
    {
        // Reading watch's output is what makes the status endpoint know anything — but redirecting a
        // stream to read it is exactly how a tool accidentally swallows the console the developer is
        // watching. Asserted against a real child process, because that is where it could break.
        var lines = new List<string>();
        // Synchronized: the two pumps run concurrently, and the real sinks (Console.Out/Error) already
        // are. A bare StringWriter shared by both would be the test's own race, not the subject's.
        var buffer = new StringWriter();
        var terminal = TextWriter.Synchronized(buffer);
        var runner = new ProcessRunner(terminal, terminal);

        var exit = await runner.RunTeeAsync(
            "dotnet", ["--version"], null, lines.Add, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.NotEmpty(lines);
        Assert.Contains(lines[0], buffer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_observer_that_throws_does_not_truncate_the_terminal()
    {
        // The observer is a convenience over somebody else's output; it does not get to kill the pump.
        var buffer = new StringWriter();
        var terminal = TextWriter.Synchronized(buffer);
        var runner = new ProcessRunner(terminal, terminal);

        var exit = await runner.RunTeeAsync(
            "dotnet", ["--version"], null, _ => throw new InvalidOperationException("boom"), CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.NotEmpty(buffer.ToString().Trim());
    }

    // ---- browser ----

    [Fact]
    public async Task Without_open_no_browser_is_launched()
    {
        var (command, _, browser) = Build();

        await command.ExecuteAsync([], CancellationToken.None);

        Assert.Empty(browser.Opened);
    }

    [Fact]
    public async Task Open_is_skipped_when_the_launch_profile_already_opens_one()
    {
        // dotnet watch honours launchBrowser itself and .NET 10 has no environment variable to suppress
        // it, so opening as well would just produce two tabs.
        var fs = SeededServer(launchBrowser: true);
        var console = new StringConsole();
        var browser = new FakeBrowserLauncher();
        var command = new DevCommand(console, new FakeProcessRunner(), fs, browser, "/app");

        await command.ExecuteAsync(["--open"], CancellationToken.None);

        Assert.Empty(browser.Opened);
        Assert.Contains("already opens a browser", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_open_never_launches_one()
    {
        var (command, _, browser) = Build();

        await command.ExecuteAsync(["--open", "--no-open"], CancellationToken.None);

        Assert.Empty(browser.Opened);
    }

    [Theory]
    [InlineData(nameof(BrowserPlatform.MacOS), "open")]
    [InlineData(nameof(BrowserPlatform.Linux), "xdg-open")]
    [InlineData(nameof(BrowserPlatform.Windows), "cmd")]
    public void The_open_command_is_right_for_every_platform(string platformName, string expected)
    {
        var platform = Enum.Parse<BrowserPlatform>(platformName);
        // Takes the platform explicitly so all three branches are covered from any host OS — a test that
        // only exercises the developer's own platform covers a third of the matrix.
        var (fileName, args) = BrowserLauncher.CommandFor(platform, "https://example.test/a?x=1&y=2");

        Assert.Equal(expected, fileName);
        if (platform == BrowserPlatform.Windows)
        {
            // The empty window-title argument is required: without it a URL containing '&' is taken as
            // the title and nothing opens.
            Assert.Equal(["/c", "start", "", "https://example.test/a?x=1&y=2"], args);
        }
        else
        {
            Assert.Equal(["https://example.test/a?x=1&y=2"], args);
        }
    }

    /// <summary>
    ///     A wasm-hosted watch session must serve the client's <b>build</b> output. The published bundle
    ///     is republished by a nested emscripten relink on every save, and it is trimmed — and trimming
    ///     folds <c>MetadataUpdater.IsSupported</c> to false, so an applied delta could never reach the
    ///     browser session even if one arrived.
    /// </summary>
    [Fact]
    public void A_wasm_hosted_watch_session_asks_for_the_dev_bundle()
    {
        // --property:, not -p:, which is ambiguous with --project on `dotnet run`.
        Assert.Contains("--property:RaskWasmDevBundle=true", Args(kind: DevTemplateKind.WasmHosted));

        // …and only there: a plain Server host has no WASM bundle to switch.
        Assert.DoesNotContain("--property:RaskWasmDevBundle=true", Args(kind: DevTemplateKind.Server));
        Assert.DoesNotContain("--property:RaskWasmDevBundle=true", Args(kind: DevTemplateKind.WasmStandalone));
    }

    [Fact]
    public void The_dev_bundle_is_not_requested_when_there_is_nothing_to_apply()
    {
        // --no-hot-reload means "restart instead of applying", and --once is a plain run: in both, the
        // published bundle is the honest thing to serve.
        Assert.DoesNotContain(
            "--property:RaskWasmDevBundle=true", Args(kind: DevTemplateKind.WasmHosted, noHotReload: true));
        Assert.DoesNotContain(
            "--property:RaskWasmDevBundle=true", Args(kind: DevTemplateKind.WasmHosted, once: true));
    }

    // ---- helpers ----

    private static IReadOnlyList<string> Args(
        string? project = null,
        bool once = false,
        bool noHotReload = false,
        string? launchProfile = null,
        bool nonInteractive = false,
        IReadOnlyList<string>? passthrough = null,
        DevTemplateKind kind = DevTemplateKind.Server) =>
        DevCommand.BuildDotnetArguments(project, once, noHotReload, launchProfile, nonInteractive, passthrough ?? [], kind);

    private static IReadOnlyDictionary<string, string> Env(
        DevTemplateKind kind = DevTemplateKind.Server,
        bool restartOnRudeEdit = true,
        string? urls = null,
        Func<string, string?>? readEnv = null) =>
        DevCommand.BuildEnvironment(kind, restartOnRudeEdit, urls, readEnv ?? (_ => null));

    private static FakeFileSystem SeededServer(bool launchBrowser = false)
    {
        var fs = new FakeFileSystem();
        fs.Seed("/app/App.csproj", ServerCsproj);
        fs.Seed("/app/Properties/launchSettings.json", $$"""
            {
              "profiles": {
                "App": {
                  "commandName": "Project",
                  "launchBrowser": {{(launchBrowser ? "true" : "false")}},
                  "applicationUrl": "https://localhost:5001;http://localhost:5000"
                }
              }
            }
            """);
        return fs;
    }

    private static (DevCommand Command, FakeProcessRunner Runner, FakeBrowserLauncher Browser) Build()
    {
        var runner = new FakeProcessRunner { RunExitCode = 0 };
        var browser = new FakeBrowserLauncher();
        return (new DevCommand(new StringConsole(), runner, SeededServer(), browser, "/app"), runner, browser);
    }
}

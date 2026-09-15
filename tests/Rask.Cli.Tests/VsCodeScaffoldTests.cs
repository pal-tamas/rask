using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Hosting.Shared;

namespace Rask.Cli.Tests;

/// <summary>
///     Every host template ships the VS Code setup that makes F5 a debug session of the dev loop.
/// </summary>
/// <remarks>
///     These files are JSON a person never runs until they press F5, so everything they reference is held
///     to the thing it names: the build task passes the same dev-session property <c>rask dev</c> does, the
///     program is the dll the scaffolded project actually builds, and the line the launch configuration
///     waits for is the one the app logs.
/// </remarks>
public sealed class VsCodeScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    private static readonly string[] VsCodeFiles =
        [".vscode/launch.json", ".vscode/tasks.json", ".vscode/extensions.json"];

    private static readonly JsonDocumentOptions Jsonc = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Every scaffold that has an ASP.NET host, by a readable label.</summary>
    private static IEnumerable<(string Label, ScaffoldResult Result)> HostScaffolds()
    {
        yield return ("server", ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version));
        yield return ("server --wasm", ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries { Wasm = true }, Version));
        yield return ("server --islands react", ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version, ["react"]));

        foreach (var framework in SpaFramework.All)
        {
            yield return (framework.Key, ProjectGenerator.GenerateSpa(Root, "App", framework, new ServerBatteries(), Version));
        }

        foreach (var framework in MetaTemplate.All)
        {
            yield return (framework.Key, ProjectGenerator.GenerateMeta(Root, "App", framework, new ServerBatteries(), Version));
        }
    }

    [Fact]
    public void Every_host_template_ships_the_vscode_setup()
    {
        foreach (var (label, result) in HostScaffolds())
        {
            var files = Index(result);

            foreach (var file in VsCodeFiles)
            {
                Assert.True(files.ContainsKey(file), $"{label} does not scaffold {file}");
                using var _ = JsonDocument.Parse(files[file], Jsonc);
            }
        }
    }

    [Fact]
    public void The_wasm_template_debugs_in_the_browser_through_the_dev_servers_proxy()
    {
        // #1073. Its C# runs in the browser, which the coreclr debugger cannot reach: F5 starts the dev server and
        // a browser under the JavaScript debugger, whose inspectUri goes through /_framework/debug.
        var files = Index(ProjectGenerator.GenerateWasm(Root, "App", pwa: false, docker: false, Version));
        using var launch = JsonDocument.Parse(files[".vscode/launch.json"], Jsonc);
        using var tasks = JsonDocument.Parse(files[".vscode/tasks.json"], Jsonc);
        Assert.True(files.ContainsKey(".vscode/extensions.json"));

        var config = launch.RootElement.GetProperty("configurations").EnumerateArray().Single();
        Assert.Equal("chrome", config.GetProperty("type").GetString());
        AssertInspectsThroughTheProxy(config);

        var task = tasks.RootElement.GetProperty("tasks").EnumerateArray()
            .Single(t => t.GetProperty("label").GetString() == config.GetProperty("preLaunchTask").GetString());
        var args = task.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToList();

        // The dev server listens where the browser goes, builds the scaffolded project as a dev session, and stays up.
        Assert.Equal(config.GetProperty("url").GetString(), args[args.IndexOf("--urls") + 1]);
        Assert.Contains("${workspaceFolder}/App.csproj", args);
        Assert.Contains($"--property:{DevCommand.DevSessionProperty}=true", args);
        Assert.True(task.GetProperty("isBackground").GetBoolean());

        // The debug session waits for the line the SDK's dev server prints once its proxy is mapped.
        Assert.Equal("Debug at url:", task.GetProperty("problemMatcher").GetProperty("background").GetProperty("endsPattern").GetString());
    }

    [Fact]
    public void A_server_with_a_wasm_client_debugs_the_client_in_the_browser_once_the_host_is_up()
    {
        var files = Index(ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries { Wasm = true }, Version));
        using var launch = JsonDocument.Parse(files[".vscode/launch.json"], Jsonc);
        using var settings = JsonDocument.Parse(files["Properties/launchSettings.json"], Jsonc);
        var configurations = launch.RootElement.GetProperty("configurations").EnumerateArray().ToList();

        var ready = configurations[0].GetProperty("serverReadyAction");
        Assert.Equal("startDebugging", ready.GetProperty("action").GetString());
        Assert.Matches(ready.GetProperty("pattern").GetString()!, EditorDevSession.OpenLinePrefix + "https://app.test");

        var browser = configurations.Single(c => c.GetProperty("name").GetString() == ready.GetProperty("name").GetString());
        AssertInspectsThroughTheProxy(browser);

        // F5 runs the host on its launch profile's addresses, so that is where the browser has to go.
        var urls = settings.RootElement.GetProperty("profiles").EnumerateObject().Single().Value
            .GetProperty("applicationUrl").GetString()!.Split(';');
        Assert.Contains(browser.GetProperty("url").GetString(), urls);
    }

    [Fact]
    public void A_server_without_a_wasm_client_opens_a_plain_browser()
    {
        var files = Index(ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version));

        Assert.DoesNotContain("inspectUri", files[".vscode/launch.json"], StringComparison.Ordinal);
    }

    private static void AssertInspectsThroughTheProxy(JsonElement config)
    {
        Assert.Equal("launch", config.GetProperty("request").GetString());
        Assert.Equal(
            "{wsProtocol}://{url.hostname}:{url.port}/_framework/debug/ws-proxy?browser={browserInspectUri}",
            config.GetProperty("inspectUri").GetString());
    }

    [Fact]
    public void F5_builds_a_dev_session_of_the_scaffolded_project()
    {
        foreach (var (label, result) in HostScaffolds())
        {
            var files = Index(result);
            using var tasks = JsonDocument.Parse(files[".vscode/tasks.json"], Jsonc);
            using var launch = JsonDocument.Parse(files[".vscode/launch.json"], Jsonc);

            var config = launch.RootElement.GetProperty("configurations")[0];
            var preLaunch = config.GetProperty("preLaunchTask").GetString();
            var task = tasks.RootElement.GetProperty("tasks").EnumerateArray()
                .SingleOrDefault(t => t.GetProperty("label").GetString() == preLaunch);

            Assert.True(task.ValueKind == JsonValueKind.Object, $"{label}: preLaunchTask '{preLaunch}' names no task");

            var args = task.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ToList();

            // The same switch `rask dev` passes — the whole point of having one.
            Assert.Contains($"--property:{DevCommand.DevSessionProperty}=true", args);

            // The project it builds is the one the scaffold wrote, not the template's placeholder.
            Assert.Contains("${workspaceFolder}/App.csproj", args);
            Assert.True(files.ContainsKey("App.csproj"), $"{label}: the build task names App.csproj, which is not scaffolded");
        }
    }

    [Fact]
    public void F5_runs_the_dll_that_build_produces_with_just_my_code()
    {
        foreach (var (label, result) in HostScaffolds())
        {
            var files = Index(result);
            using var launch = JsonDocument.Parse(files[".vscode/launch.json"], Jsonc);
            var config = launch.RootElement.GetProperty("configurations")[0];

            Assert.Equal("coreclr", config.GetProperty("type").GetString());
            Assert.Equal("launch", config.GetProperty("request").GetString());

            // The program path assumes net10.0 and the default output layout; the csproj has to agree.
            Assert.Equal("${workspaceFolder}/bin/Debug/net10.0/App.dll", config.GetProperty("program").GetString());
            Assert.Contains("<TargetFramework>net10.0</TargetFramework>", files["App.csproj"], StringComparison.Ordinal);

            // What makes a handler's exception stop on its throw line rather than inside Rask.
            Assert.True(config.GetProperty("justMyCode").GetBoolean(), label);
            Assert.False(files[".vscode/launch.json"].Contains("Company.RaskServer", StringComparison.Ordinal), label);
        }
    }

    [Fact]
    public void Nothing_promises_hot_reload_under_the_debugger()
    {
        // Tried under VS Code's F5: C# Dev Kit reports hot reload unavailable for this launch, and a saved
        // edit never reached the running app. So the scaffold turns nothing on that would suggest otherwise;
        // edits under the debugger need a restart, and `rask dev` is the live-edit loop.
        var files = Index(ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version));

        Assert.DoesNotContain(".vscode/settings.json", files.Keys);
        Assert.DoesNotContain(files, f => f.Key.StartsWith(".vscode/", StringComparison.Ordinal)
                                          && (f.Value.Contains("hotReload", StringComparison.OrdinalIgnoreCase)
                                              || f.Value.Contains("DOTNET_MODIFIABLE_ASSEMBLIES", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_browser_opens_on_the_line_the_app_logs()
    {
        var files = Index(ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version));
        using var launch = JsonDocument.Parse(files[".vscode/launch.json"], Jsonc);
        var ready = launch.RootElement.GetProperty("configurations")[0].GetProperty("serverReadyAction");

        var pattern = ready.GetProperty("pattern").GetString()!;
        var match = Regex.Match(EditorDevSession.OpenLinePrefix + "https://app.test", pattern);

        Assert.True(match.Success, $"serverReadyAction.pattern '{pattern}' does not match what the app logs");
        Assert.Equal("https://app.test", match.Groups[1].Value);
        Assert.Equal("%s", ready.GetProperty("uriFormat").GetString());
    }

    private static Dictionary<string, string> Index(ScaffoldResult result) =>
        result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content,
            StringComparer.Ordinal);
}

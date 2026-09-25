using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class ProjectGeneratorTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    // Files the server template always emits, whatever the flags. A new project is deliberately minimal:
    // the shell (Features/Shared) + welcome page (Features/Home), the entry point, csproj and launch profile.
    private static readonly string[] AlwaysPresent =
    [
        "App.csproj", "Program.cs", "GlobalUsings.cs", "Features/Shared/App.cs", "Features/Home/HomePage.cs",
        "Features/Shared/ErrorPage.cs",
        "Properties/launchSettings.json", "appsettings.json", "appsettings.Production.json",
        // For the editor, not the build: scoped TypeScript is compiled by tsgo with explicit flags,
        // but without a tsconfig an author gets no checking and no completion while writing it.
        "Styles/app.css",
        "tsconfig.json",
        // F5 in VS Code: build as a dev session, run under the C# debugger (VsCodeAssembly).
        ".vscode/launch.json", ".vscode/tasks.json", ".vscode/extensions.json", ".vscode/settings.json",
    ];

    // Demo content `rask new` used to scaffold and deliberately no longer does — a new project ships one
    // welcome slice, not a folder of samples to delete. Guards against any of it creeping back (bare-root
    // paths — the real welcome page lives at Features/Home/HomePage.cs).
    private static readonly string[] NeverPresent =
    [
        "Counter.cs", "Weather.cs", "WeatherForecast.cs",
        "LocalWeatherForecastService.cs", "README.md", "AGENTS.md",
    ];

    /// <summary>
    /// A template's own files plus the hygiene set every template writes regardless of flags — the
    /// .gitignore, the .editorconfig, the solution, and the global.json that pins the SDK band
    /// (TemplateMaterializer.WithGlobalJson).
    /// </summary>
    private static string[] WithHygiene(IEnumerable<string> files) =>
        [.. files, ".gitignore", ".editorconfig", "App.slnx", "global.json"];

    [Fact]
    public void Base_project_emits_the_core_files_and_packages_with_no_flags()
    {
        var (files, result) = Generate();

        Assert.Equal(WithHygiene(AlwaysPresent).Order(), files.Keys.Order());

        // Just the framework -- and the framework is all a styled app needs. Tailwind is built INTO
        // Rask.Server (RaskTailwindBuildPack), so a scaffolded csproj names no styling package at all:
        // naming one would import the same targets a second time and run the compiler twice.
        Assert.Equal(["Rask.Server", "Rask.DevTools"], result.Packages);
        // No opt-in artifacts leak in.
        Assert.DoesNotContain("Features/Auth/CredentialStore.cs", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
    }

    /// <summary>
    /// Health checks, forwarded headers, the exception handler, HSTS, auth and every battery's endpoints are
    /// RaskApp's, where every app gets them — and a scaffold that wired any of them again would wire it twice.
    /// </summary>
    [Fact]
    public void Program_cs_leaves_the_pipeline_to_RaskApp()
    {
        var everyBattery = Full();

        var on = Index(ProjectGenerator.GenerateServer(Root, "App", everyBattery, Version))["Program.cs"];
        var off = Generate().Files["Program.cs"];

        foreach (var program in new[] { on, off })
        {
            Assert.DoesNotContain("builder.", program, StringComparison.Ordinal);
            Assert.DoesNotContain("app.Use", program, StringComparison.Ordinal);
            Assert.DoesNotContain("app.Map", program, StringComparison.Ordinal);
            Assert.DoesNotContain("AddRask", program, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The scaffold no longer writes a <c>HostOptions</c> block, because <c>AddRask</c> applies the budget.
    /// </summary>
    /// <remarks>
    /// Scaffolding it was only ever a way of reaching apps one at a time, and it reached only the ones
    /// generated after the block existed — which is how nine of the ten samples came to be sitting on
    /// .NET's 30s default against a 20s SIGKILL. This asserts the ABSENCE; that the budget an app actually
    /// gets still fits the deploy window is <see cref="SamplesShutdownBudgetTests"/>, which resolves the
    /// options rather than reading source text.
    /// </remarks>
    [Fact]
    public void The_scaffold_no_longer_hand_rolls_the_shutdown_budget()
    {
        var (files, _) = Generate();
        var program = files["Program.cs"];

        Assert.DoesNotContain("ShutdownTimeout", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ServicesStopConcurrently", program, StringComparison.Ordinal);
    }

    /// <summary>The ladder itself has to be sane — this is what fails if someone edits one rung alone.</summary>
    [Fact]
    public void The_app_budget_leaves_room_inside_the_docker_stop_grace()
    {
        Assert.True(ShutdownBudget.HostShutdownSeconds < ShutdownBudget.DockerStopSeconds,
            "the app must finish before docker SIGKILLs it");
        Assert.True(ShutdownBudget.DockerStopSeconds - ShutdownBudget.HostShutdownSeconds >= 5,
            "leave margin for container teardown and log flush after Host.StopAsync returns");
        Assert.True(ShutdownBudget.PreStopDrainSeconds < ShutdownBudget.HostShutdownSeconds,
            "the pre-stop pause must not dominate the deploy");
    }

    /// <summary>
    /// Every deploy replaces the container. With the default key ring — written inside that container —
    /// the replacement mints new keys and every auth cookie already issued stops validating, so a deploy
    /// silently signs out every user. The ring has to live on the volume `rask deploy` mounts.
    /// </summary>
    /// <remarks>
    /// The scaffold used to carry that fix as sixteen lines of `PersistKeysToFileSystem` wiring, which meant
    /// only freshly generated apps had it — an app written by hand, or one generated before the block
    /// existed, silently signed its users out on every deploy with nothing in the logs. `AddRask` does it
    /// now, so this asserts the ABSENCE: what the scaffold must not do is hand-roll it again. The guarantee
    /// itself is covered where it now lives, in
    /// <c>Rask.Server.Tests.Security.DataProtectionKeyRingTests</c>.
    /// </remarks>
    [Fact]
    public void The_scaffold_no_longer_hand_rolls_the_data_protection_key_ring()
    {
        var (files, _) = Generate();
        var program = files["Program.cs"];

        Assert.DoesNotContain(".PersistKeysToFileSystem(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Rask:DataProtection:KeyPath", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Microsoft.AspNetCore.DataProtection;", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// docs/observability.md tells the reader to set Logging:LogLevel:Rask.Live — which needs somewhere
    /// to put it. The files must also be real JSON to the provider that will load them (it skips comments).
    /// </summary>
    [Fact]
    public void Configuration_files_are_scaffolded_and_parse()
    {
        var (files, _) = Generate();

        Assert.Contains("Rask.Live", files["appsettings.json"], StringComparison.Ordinal);

        // Production overrides live in their own file, selected by ASPNETCORE_ENVIRONMENT, which
        // `rask deploy` sets on the container.
        Assert.Contains("Logging", files["appsettings.Production.json"], StringComparison.Ordinal);

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (var name in new[] { "appsettings.json", "appsettings.Production.json" })
        {
            var error = Record.Exception(() => JsonDocument.Parse(files[name], options));
            Assert.True(error is null, $"{name} isn't valid JSON: {error?.Message}");
        }
    }

    /// <summary>
    /// A database-backed app gets continuous backup, inert until a replica URL is configured — RaskApp only
    /// starts the replicator (and restores from it) once one is named, so an app without one still starts.
    /// </summary>
    [Fact]
    public void A_database_app_leaves_continuous_backup_inert_until_a_replica_is_named()
    {
        var (files, _) = Generate(data: true);

        var settings = files["appsettings.json"];

        Assert.Contains("\"ReplicaUrl\": \"\"", settings, StringComparison.Ordinal);
        Assert.Contains("Rask__Litestream__ReplicaUrl", settings, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wiring is useless without the binary it drives, so the image carries one — but only when there
    /// is a database to replicate.
    /// </summary>
    [Fact]
    public void The_image_carries_the_replicator_binary_only_when_there_is_a_database()
    {
        var (withData, _) = Generate(data: true, docker: true);

        Assert.Contains("COPY --from=litestream/litestream:", withData["Dockerfile"], StringComparison.Ordinal);

        var (without, _) = Generate(docker: true);

        Assert.DoesNotContain("litestream", without["Dockerfile"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_shell_and_welcome_page_are_feature_slices_and_no_demo_files_are_scaffolded()
    {
        var (files, _) = Generate(pwa: true, cqrs: true, docker: true);

        foreach (var gone in NeverPresent)
        {
            Assert.DoesNotContain(gone, files.Keys);
        }

        // The shell is the Features/Shared bucket; it hosts the Router but not the welcome page.
        var shell = files["Features/Shared/App.cs"];
        Assert.Contains("public sealed partial class App : Component", shell, StringComparison.Ordinal);
        Assert.Contains("Render() => Router;", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class HomePage", shell, StringComparison.Ordinal);

        // The welcome page is its own Features/Home slice (no scoped .css to pair with).
        var home = files["Features/Home/HomePage.cs"];
        Assert.Contains("[Route(\"/\")]", home, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class HomePage : Component", home, StringComparison.Ordinal);

        // The welcome copy points at the file it actually lives in.
        Assert.Contains("HomePage.cs", home, StringComparison.Ordinal);

        // The page is written in daisyUI's own class names, which is what every project gets: the
        // classes here are the ones its own build scans this file for, and the same ones every other
        // `rask new` template draws so a project looks the same whichever front end it was scaffolded
        // with.
        var page = Generate().Files["Features/Home/HomePage.cs"];
        Assert.Contains("card bg-base-100", page, StringComparison.Ordinal);
        Assert.Contains("btn btn-primary", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_registers_no_service_whose_file_is_not_scaffolded()
    {
        var (files, _) = Generate();

        // The weather demo service went with its files; a stale registration would not compile.
        Assert.DoesNotContain("IWeatherForecastService", files["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Project_name_replaces_the_placeholder_namespace_everywhere()
    {
        var (files, _) = Generate(pwa: true, cqrs: true, docker: true);

        // The csproj is renamed to the project, and nothing retains the placeholder.
        Assert.True(files.ContainsKey("App.csproj"));
        foreach (var (path, content) in files)
        {
            Assert.DoesNotContain("Company.RaskServer", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Company.RaskServer", path, StringComparison.Ordinal);
        }

        // Program.cs uses top-level statements (no namespace) but references the shell's namespace.
        Assert.Contains("using App.Features.Shared;", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("namespace App.Features.Shared;", files["Features/Shared/App.cs"], StringComparison.Ordinal);
        Assert.Contains("namespace App.Features.Home;", files["Features/Home/HomePage.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Csproj_pins_every_package_to_the_supplied_version()
    {
        // With every battery on, so the pin is checked on the whole reference list the template can emit —
        // the version has to be stamped on every reference, not most.
        var result = ProjectGenerator.GenerateServer(Root, "App", Full(), Version);

        var references = Regex.Matches(
            Index(result)["App.csproj"], """<PackageReference Include="([^"]+)" Version="([^"]+)"/>""");

        Assert.Equal(["Rask.Server", "Rask.DevTools"], references.Select(m => m.Groups[1].Value));
        Assert.All(references, m => Assert.Equal(Version, m.Groups[2].Value));
        Assert.Equal(["Rask.Server", "Rask.DevTools"], result.Packages);
    }

    [Fact]
    public void Data_flag_keeps_the_database_on_and_names_its_file_in_appsettings()
    {
        var (on, _) = Generate(data: true, cqrs: true);

        var program = on["Program.cs"];

        // RaskApp maps the app's entities and every battery's tables in a context of its own, so the scaffold
        // writes no AppDbContext; the file the database lives in is a setting `rask deploy` redirects.
        Assert.DoesNotContain("c.Data.Off()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Features/Shared/AppDbContext.cs", on.Keys);
        Assert.Contains("\"App\": \"Data Source=app.db\"", on["appsettings.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void Data_flag_off_leaves_no_database_wiring()
    {
        var (off, _) = Generate(data: false);

        Assert.Contains("c.Data.Off();", off["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Features/Shared/User.cs", off.Keys);
        Assert.DoesNotContain("\"App\": \"Data Source=app.db\"", off["appsettings.json"], StringComparison.Ordinal);
    }

    [Fact]
    public void Pwa_flag_toggles_the_manifest_assets_and_wiring()
    {
        var (on, _) = Generate(pwa: true);

        Assert.True(on.ContainsKey("wwwroot/icon.svg"));
        Assert.True(on.ContainsKey("wwwroot/offline.html"));
        Assert.DoesNotContain("c.Pwa.Off()", on["Program.cs"], StringComparison.Ordinal);

        var (off, _) = Generate(pwa: false);

        Assert.DoesNotContain("wwwroot/icon.svg", off.Keys);
        Assert.Contains("c.Pwa.Off();", off["Program.cs"], StringComparison.Ordinal);
    }

    // --cqrs is a switch, not a package: Rask.Server carries the mediator, and no sample slice is scaffolded.
    [Fact]
    public void Cqrs_flag_toggles_the_off_switch_but_scaffolds_no_sample()
    {
        var (on, onResult) = Generate(cqrs: true);

        Assert.DoesNotContain("c.Cqrs.Off()", on["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs", onResult.Packages);
        Assert.DoesNotContain("Cqrs/GreetingQuery.cs", on.Keys);
        Assert.DoesNotContain("Cqrs/GreetingPage.cs", on.Keys);

        var (off, _) = Generate(cqrs: false);

        Assert.Contains("c.Cqrs.Off();", off["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Docker_flag_toggles_only_the_container_files()
    {
        var (on, _) = Generate(docker: true);

        Assert.True(on.ContainsKey("Dockerfile"));
        Assert.True(on.ContainsKey(".dockerignore"));
        Assert.Contains("App.dll", on["Dockerfile"], StringComparison.Ordinal); // name substituted
        // A writable /data dir owned by the non-root runtime user, so `rask deploy`'s named volume (mounted
        // there) is writable and the SQLite DB survives redeploys.
        Assert.Contains("mkdir -p /data && chown $APP_UID:$APP_UID /data", on["Dockerfile"], StringComparison.Ordinal);

        var (off, _) = Generate(docker: false);

        Assert.DoesNotContain("Dockerfile", off.Keys);
        Assert.DoesNotContain(".dockerignore", off.Keys);
    }

    // "test every scenario" — all 8 flag combinations keep the invariants: core files always present,
    // no placeholder leakage, packages always include the framework, opt-in files exactly track their flag.
    [Theory]
    [MemberData(nameof(AllFlagCombinations))]
    public void Every_flag_combination_holds_the_invariants(bool pwa, bool cqrs, bool docker)
    {
        var (files, result) = Generate(pwa, cqrs, docker);

        foreach (var expected in AlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"[{pwa},{cqrs},{docker}] missing {expected}");
        }

        Assert.Equal(["Rask.Server", "Rask.DevTools"], result.Packages);
        Assert.Equal(!cqrs, files["Program.cs"].Contains("c.Cqrs.Off();", StringComparison.Ordinal));

        // Tailwind is built in, so no combination REFERENCES it and every combination still gets it --
        // it rides inside Rask.Server. This assertion has now been all four things in turn: Bootstrap
        // always present, then neither present, then Tailwind always referenced, now Tailwind never
        // referenced because it is not a reference any more. Which is exactly why it is asserted on
        // every combination rather than assumed.
        Assert.DoesNotContain("Rask.Tailwind", result.Packages);
        Assert.DoesNotContain("Rask.Bootstrap", result.Packages);

        Assert.Equal(pwa, files.ContainsKey("wwwroot/icon.svg"));
        Assert.Equal(docker, files.ContainsKey("Dockerfile"));

        foreach (var gone in NeverPresent)
        {
            Assert.DoesNotContain(gone, files.Keys);
        }

        foreach (var content in files.Values)
        {
            Assert.DoesNotContain("Company.RaskServer", content, StringComparison.Ordinal);
        }
    }

    public static IEnumerable<object[]> AllFlagCombinations()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0];
        }
    }

    [Fact]
    public void The_server_template_leaves_the_browser_to_rask()
    {
        // The template and `rask dev`'s open default are one change (#1099). dotnet watch honours
        // launchBrowser itself and cannot be suppressed from the environment, so leaving this true hands
        // watch the browser and every run lands on the profile's https://localhost:5001 — not the
        // https://appname.test the command just configured, and not what the certificate is issued for.
        var (files, _) = Generate();

        using var document = JsonDocument.Parse(files["Properties/launchSettings.json"]);
        var profile = document.RootElement.GetProperty("profiles").EnumerateObject().First().Value;

        Assert.False(profile.GetProperty("launchBrowser").GetBoolean());
    }

    // ---- Program.cs: the batteries an app does without ----

    [Fact]
    public void An_app_with_every_battery_on_is_one_line_of_Program_cs()
    {
        var everyBattery = Full();

        var program = Index(ProjectGenerator.GenerateServer(Root, "App", everyBattery, Version))["Program.cs"];

        Assert.Empty(ProjectGenerator.OffSwitches(everyBattery));
        Assert.Contains("RaskApp.Create(args).Run<App>();", program, StringComparison.Ordinal);
        Assert.DoesNotContain("var app =", program, StringComparison.Ordinal);
    }

    [Fact]
    public void A_battery_turned_off_is_one_Configure_line_in_Program_cs()
    {
        var withoutJobs = Full() with { Jobs = false };

        var program = Index(ProjectGenerator.GenerateServer(Root, "App", withoutJobs, Version))["Program.cs"];

        Assert.Contains("var app = RaskApp.Create(args);", program, StringComparison.Ordinal);
        Assert.Contains("app.Configure(c => c.Jobs.Off());", program, StringComparison.Ordinal);
        Assert.Contains("app.Run<App>();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_batteries_turned_off_share_one_Configure_block()
    {
        var withoutJobsOrMail = Full() with { Jobs = false, Mail = false };

        var program = Index(ProjectGenerator.GenerateServer(Root, "App", withoutJobsOrMail, Version))["Program.cs"];

        Assert.Contains(
            "app.Configure(c =>\n{\n    c.Jobs.Off();\n    c.Mail.Off();\n});",
            program.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_the_database_off_does_not_list_the_batteries_it_takes_with_it()
    {
        var withoutData = Full() with { Data = false, Jobs = false, Mail = false, Storage = false };

        var off = ProjectGenerator.OffSwitches(withoutData);

        Assert.Equal(["Data"], off);
    }

    [Fact]
    public void Turning_the_PWA_off_does_not_list_push()
    {
        var withoutPwa = Full() with { Pwa = false, Push = false };

        var off = ProjectGenerator.OffSwitches(withoutPwa);

        Assert.Equal(["Pwa"], off);
    }

    [Fact]
    public void Off_switches_are_listed_outermost_first()
    {
        var nothing = new ServerBatteries();

        var off = ProjectGenerator.OffSwitches(nothing);

        Assert.Equal(["Cqrs", "Data", "Logs", "Pwa"], off);
    }

    [Fact]
    public void The_generated_Program_cs_names_the_apps_namespace()
    {
        var withoutJobs = Full() with { Jobs = false };

        var program = Index(ProjectGenerator.GenerateServer(Root, "Shop", withoutJobs, Version))["Program.cs"];

        Assert.StartsWith("using Shop.Features.Shared;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Company.RaskServer", program, StringComparison.Ordinal);
    }

    private static ServerBatteries Full() => new()
    {
        Pwa = true,
        Cqrs = true,
        Data = true,
        Docker = true,
        Jobs = true,
        Mail = true,
        Cache = true,
        Storage = true,
        Outbox = true,
        Push = true,
        Snapshots = true,
        Logs = true,
        Ops = true,
    };

    private static (Dictionary<string, string> Files, ScaffoldResult Result) Generate(
        bool pwa = false, bool cqrs = false, bool docker = false, bool data = false)
    {
        var result = ProjectGenerator.GenerateServer(
            Root,
            "App",
            new ServerBatteries { Pwa = pwa, Cqrs = cqrs, Data = data, Docker = docker },
            Version);
        return (Index(result), result);
    }

    private static Dictionary<string, string> Index(ScaffoldResult result) =>
        result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content,
            StringComparer.Ordinal);

    // ---- wasm template ----

    private static readonly string[] WasmAlwaysPresent =
    [
        "App.csproj", "Program.cs", "GlobalUsings.cs", "Features/Shared/App.cs", "Features/Home/HomePage.cs",
        "wwwroot/index.html", "runtimeconfig.template.json",
        // For the editor, not the build — see AlwaysPresent.
        "Styles/app.css",
        "tsconfig.json",
        // F5 in VS Code: the dev server, and a browser under the JavaScript debugger through its proxy (#1073).
        ".vscode/launch.json", ".vscode/tasks.json", ".vscode/extensions.json", ".vscode/settings.json",
    ];

    [Fact]
    public void Wasm_base_emits_core_files_and_the_wasm_packages()
    {
        var result = ProjectGenerator.GenerateWasm(Root, "App", pwa: false, docker: false, Version);
        var files = Index(result);

        Assert.Equal(WithHygiene(WasmAlwaysPresent).Order(), files.Keys.Order());

        Assert.Equal(["Rask.Wasm", "Rask.Ui", "Rask.DevTools"], result.Packages);
        Assert.Contains("Microsoft.NET.Sdk.WebAssembly", files["App.csproj"], StringComparison.Ordinal);
        // A standalone SPA never carries the auth/pwa/docker opt-ins by default.
        Assert.DoesNotContain("Features/Auth/Auth.cs", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
    }

    // The browser template compiles its own stylesheet like every other host. It was the one worth
    // spelling out while styling was a choice, because the WASM SDK publishes wwwroot as it finds it and a
    // stylesheet written after the publish would simply not be there.
    [Fact]
    public void Wasm_compiles_its_own_stylesheet()
    {
        var result = ProjectGenerator.GenerateWasm(
            Root, "App", pwa: false, docker: false, Version, new ServerBatteries());
        var files = Index(result);

        Assert.Equal(["Rask.Wasm", "Rask.Ui", "Rask.DevTools"], result.Packages);
        Assert.Contains("@import \"tailwindcss\";", files["Styles/app.css"], StringComparison.Ordinal);

        // The csproj names Rask.Wasm and nothing else for styling: the Tailwind build ships inside it.
        Assert.DoesNotContain("Rask.Tailwind", files["App.csproj"], StringComparison.Ordinal);

        // The head has to point at what the build writes, or the stylesheet is compiled and never served.
        Assert.Contains("/css/app.css", files["Features/Shared/App.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Wasm_pwa_and_docker_toggle_their_files()
    {
        var pwa = Index(ProjectGenerator.GenerateWasm(Root, "App", pwa: true, docker: false, Version));

        Assert.True(pwa.ContainsKey("wwwroot/icon.svg"));
        Assert.Contains("serviceWorker", pwa["wwwroot/index.html"], StringComparison.Ordinal);

        var noPwa = Index(ProjectGenerator.GenerateWasm(Root, "App", pwa: false, docker: false, Version));

        Assert.DoesNotContain("serviceWorker", noPwa["wwwroot/index.html"], StringComparison.Ordinal);

        var docker = Index(ProjectGenerator.GenerateWasm(Root, "App", pwa: false, docker: true, Version));

        Assert.True(docker.ContainsKey("Dockerfile"));
        Assert.True(docker.ContainsKey("nginx.conf"));
        Assert.True(docker.ContainsKey(".dockerignore"));
    }

    [Theory]
    [MemberData(nameof(WasmFlagCombinations))]
    public void Every_wasm_flag_combination_holds_the_invariants(bool pwa, bool docker)
    {
        var result = ProjectGenerator.GenerateWasm(Root, "App", pwa, docker, Version);
        var files = Index(result);

        foreach (var expected in WasmAlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"[{pwa},{docker}] missing {expected}");
        }

        Assert.Contains("public sealed partial class HomePage : Component", files["Features/Home/HomePage.cs"], StringComparison.Ordinal);

        // Plain is what you get by not choosing, here as everywhere else — so the base package set is
        // Rask.Wasm alone. Bootstrap and Tailwind are covered by their own case below.
        Assert.Equal(["Rask.Wasm", "Rask.Ui", "Rask.DevTools"], result.Packages);
        Assert.Equal(pwa, files.ContainsKey("wwwroot/icon.svg"));
        Assert.Equal(docker, files.ContainsKey("Dockerfile"));

        foreach (var gone in NeverPresent)
        {
            Assert.DoesNotContain(gone, files.Keys);
        }

        foreach (var content in files.Values)
        {
            Assert.DoesNotContain("Company.RaskServer", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Company.RaskWasm", content, StringComparison.Ordinal);
        }
    }

    public static IEnumerable<object[]> WasmFlagCombinations()
    {
        for (var mask = 0; mask < 4; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0];
        }
    }
}

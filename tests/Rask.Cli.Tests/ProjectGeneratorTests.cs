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
        "App.csproj", "Program.cs", "Features/Shared/App.cs", "Features/Home/HomePage.cs",
        "Features/Shared/ErrorPage.cs",
        "Properties/launchSettings.json", "appsettings.json", "appsettings.Production.json",
    ];

    // Demo content `rask new` used to scaffold and deliberately no longer does — a new project ships one
    // welcome slice, not a folder of samples to delete. Guards against any of it creeping back (bare-root
    // paths — the real welcome page lives at Features/Home/HomePage.cs).
    private static readonly string[] NeverPresent =
    [
        "Counter.cs", "Weather.cs", "WeatherForecast.cs",
        "LocalWeatherForecastService.cs", "README.md", "AGENTS.md",
    ];

    [Fact]
    public void Base_project_emits_the_core_files_and_packages_with_no_flags()
    {
        var (files, result) = Generate();

        Assert.Equal(AlwaysPresent.Order(), files.Keys.Order());

        Assert.Equal(["Rask.Server", "Rask.Bootstrap"], result.Packages);
        // No opt-in artifacts leak in.
        Assert.DoesNotContain("Features/Auth/CredentialStore.cs", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
    }

    /// <summary>
    /// The endpoint `rask deploy` gates its blue-green swap on has to be able to say "full". A bare
    /// AddHealthChecks() answers 200 while the host is refusing new sessions with 503, so a deploy would
    /// happily switch traffic onto a server that can't take it.
    /// </summary>
    [Fact]
    public void Health_checks_report_live_session_capacity()
    {
        var (files, _) = Generate();

        Assert.Contains("AddHealthChecks().AddRaskLiveSessions()", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("using Rask.Server.Diagnostics;", files["Program.cs"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Behind the proxy `rask deploy` runs, without this the app sees plain HTTP from the proxy's own
    /// address — so UseHsts never emits and every client IP is the proxy's.
    /// </summary>
    [Fact]
    public void Forwarded_headers_are_honoured_before_anything_reads_the_request()
    {
        var (files, _) = Generate();
        var program = files["Program.cs"];

        Assert.Contains("ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto", program, StringComparison.Ordinal);
        Assert.Contains("options.KnownIPNetworks.Clear();", program, StringComparison.Ordinal);

        // Must run before UseHsts, which only emits when the request already looks like HTTPS.
        Assert.True(
            program.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal) < program.IndexOf("app.UseHsts();", StringComparison.Ordinal),
            "UseForwardedHeaders must come before UseHsts, or the scheme it corrects is read too late.");
    }

    /// <summary>
    /// The scaffold emits whatever the ladder says, whatever that is — asserted against
    /// <see cref="ShutdownBudget"/> rather than the literal, because the app's budget and the deploy's
    /// SIGKILL grace used to be two hardcoded numbers coupled only by a comment, free to drift apart.
    /// </summary>
    [Fact]
    public void Shutdown_budget_is_scaffolded_from_the_deploy_ladder()
    {
        var (files, _) = Generate();

        Assert.Contains(
            FormattableString.Invariant($"ShutdownTimeout = TimeSpan.FromSeconds({ShutdownBudget.HostShutdownSeconds})"),
            files["Program.cs"],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Without this, each hosted service's own shutdown grace (Litestream's WAL flush, an in-flight email
    /// send, a running job) SUMS inside the one budget instead of overlapping — and whichever service stops
    /// last gets none of it, decided by the order of AddRaskX calls in Program.cs.
    /// </summary>
    [Fact]
    public void Hosted_services_are_scaffolded_to_stop_concurrently()
    {
        var (files, _) = Generate();

        Assert.Contains("ServicesStopConcurrently = true", files["Program.cs"], StringComparison.Ordinal);
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
    [Fact]
    public void Data_protection_keys_outlive_the_container_a_deploy_replaces()
    {
        var (files, _) = Generate();
        var program = files["Program.cs"];

        Assert.Contains("using Microsoft.AspNetCore.DataProtection;", program, StringComparison.Ordinal);
        Assert.Contains(".PersistKeysToFileSystem(", program, StringComparison.Ordinal);

        // The volume `rask deploy` mounts (DeployCommand mounts {slug}-data at /data), overridable for
        // hosts that put it elsewhere.
        Assert.Contains("\"/data/keys\"", program, StringComparison.Ordinal);
        Assert.Contains("Rask:DataProtection:KeyPath", program, StringComparison.Ordinal);

        // Load-bearing alongside the path: the default discriminator comes from the content root, which
        // differs between the build and runtime images — so a persisted ring alone still wouldn't validate.
        Assert.Contains(".SetApplicationName(", program, StringComparison.Ordinal);

        // A plain `dotnet run` has no /data and no override, and must not try to create one at the
        // filesystem root — the block is skipped entirely there.
        Assert.Contains("Directory.Exists(\"/data\")", program, StringComparison.Ordinal);
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
    /// A database-backed app gets continuous backup wired in, inert until a replica URL is configured.
    /// The framework's own deploy comments already assumed a replicator was running; before this, nothing
    /// scaffolded ever started one.
    /// </summary>
    [Fact]
    public void A_database_app_is_wired_for_continuous_backup()
    {
        var (files, result) = Generate(data: true);
        var program = files["Program.cs"];

        Assert.Contains("Rask.SQLite.Litestream", result.Packages);
        Assert.Contains("Rask.SQLite.Litestream", files["App.csproj"], StringComparison.Ordinal);

        // Inert by default: no replica URL, no replicator, and the app still starts.
        Assert.Contains("""builder.Configuration["Litestream:ReplicaUrl"]""", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskSqliteLitestream", program, StringComparison.Ordinal);

        // The restore must be guarded — RestoreSqliteFromLitestreamAsync throws when nothing is registered,
        // so an unguarded call would stop every app without a replica from starting at all.
        var restore = program.IndexOf("RestoreSqliteFromLitestreamAsync", StringComparison.Ordinal);
        Assert.True(restore > 0, "the restore call is missing.");
        Assert.Contains(
            "if (!string.IsNullOrWhiteSpace(replicaUrl))",
            program[..restore],
            StringComparison.Ordinal);

        // ...and it must run before anything opens the database.
        Assert.True(
            restore < program.IndexOf("app.UseRask<App>()", StringComparison.Ordinal),
            "the restore must happen before the app starts serving.");
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
        var (files, _) = Generate(auth: true, pwa: true, cqrs: true, docker: true);

        foreach (var gone in NeverPresent)
        {
            Assert.DoesNotContain(gone, files.Keys);
        }

        // The shell is the Features/Shared bucket; it hosts the Router but not the welcome page.
        var shell = files["Features/Shared/App.cs"];
        Assert.Contains("public sealed partial class App : Component", shell, StringComparison.Ordinal);
        Assert.Contains("Render() => Router;", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("public sealed partial class HomePage", shell, StringComparison.Ordinal);

        // The welcome page is its own Features/Home slice, Bootstrap-styled (no scoped .css to pair with).
        var home = files["Features/Home/HomePage.cs"];
        Assert.Contains("[Route(\"/\")]", home, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class HomePage : Component", home, StringComparison.Ordinal);
        Assert.Contains("BsCard", home, StringComparison.Ordinal);
        // The welcome copy points at the file it actually lives in.
        Assert.Contains("Code[\"HomePage.cs\"]", home, StringComparison.Ordinal);
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
        var (files, _) = Generate(auth: true, pwa: true, cqrs: true, docker: true);

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
        var (files, _) = Generate(cqrs: true);
        var csproj = files["App.csproj"];

        Assert.Contains("<PackageReference Include=\"Rask.Server\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.Bootstrap\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.Cqrs\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_flag_pre_wires_the_app_db_context_and_sqlite()
    {
        var (on, result) = Generate(data: true);

        // The AppDbContext file, applying Rask conventions so generated feature configs are picked up.
        Assert.True(on.ContainsKey("Features/Shared/AppDbContext.cs"));
        var context = on["Features/Shared/AppDbContext.cs"];
        Assert.Contains("public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)", context, StringComparison.Ordinal);
        Assert.Contains("modelBuilder.ApplyRaskConventions();", context, StringComparison.Ordinal);

        // Program.cs wires AddRaskData + a UseRaskSqlite DbContext factory that honours a ConnectionStrings:App
        // override so `rask deploy` can redirect it to a mounted volume.
        var program = on["Program.cs"];
        Assert.Contains("builder.Services.AddRaskData();", program, StringComparison.Ordinal);
        Assert.Contains("AddDbContextFactory<AppDbContext>", program, StringComparison.Ordinal);
        Assert.Contains(".UseRaskSqlite(", program, StringComparison.Ordinal);
        Assert.Contains("builder.Configuration.GetConnectionString(\"App\")", program, StringComparison.Ordinal);

        // --data implies --cqrs (feature handlers dispatch through the mediator).
        Assert.Contains("builder.Services.AddRaskCqrs();", program, StringComparison.Ordinal);

        // The packages the generated csproj needs, pinned to the supplied version.
        Assert.Contains("Rask.Data", result.Packages);
        Assert.Contains("Rask.SQLite.EntityFrameworkCore", result.Packages);
        Assert.Contains("Rask.Cqrs", result.Packages);
        var csproj = on["App.csproj"];
        Assert.Contains("<PackageReference Include=\"Rask.Data\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.SQLite.EntityFrameworkCore\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_flag_off_leaves_no_database_wiring()
    {
        var (off, result) = Generate(data: false);

        Assert.DoesNotContain("Features/Shared/AppDbContext.cs", off.Keys);
        Assert.DoesNotContain("AddRaskData", off["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("UseRaskSqlite", off["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Data", result.Packages);
        Assert.DoesNotContain("Rask.SQLite.EntityFrameworkCore", result.Packages);
    }

    [Fact]
    public void Auth_flag_toggles_the_auth_files_and_wiring()
    {
        var (on, _) = Generate(auth: true);
        Assert.True(on.ContainsKey("Features/Auth/CredentialStore.cs"));
        Assert.True(on.ContainsKey("Features/Auth/LoginPage.cs"));
        Assert.True(on.ContainsKey("Features/Auth/MembersPage.cs"));
        Assert.Contains("AddAuthentication", on["Program.cs"], StringComparison.Ordinal);

        var (off, _) = Generate(auth: false);
        Assert.DoesNotContain("Features/Auth/CredentialStore.cs", off.Keys);
        Assert.DoesNotContain("AddAuthentication", off["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Pwa_flag_toggles_the_manifest_assets_and_wiring()
    {
        var (on, _) = Generate(pwa: true);
        Assert.True(on.ContainsKey("wwwroot/icon.svg"));
        Assert.True(on.ContainsKey("wwwroot/offline.html"));
        Assert.Contains("AddRaskPwa", on["Program.cs"], StringComparison.Ordinal);

        var (off, _) = Generate(pwa: false);
        Assert.DoesNotContain("wwwroot/icon.svg", off.Keys);
        Assert.DoesNotContain("AddRaskPwa", off["Program.cs"], StringComparison.Ordinal);
    }

    // Every server app ships a /health endpoint so `rask deploy` can probe readiness out of the box. It must
    // be mapped BEFORE UseHttpsRedirection so the deploy probe (plain HTTP inside the container) gets a 200,
    // not a 307 to a dead HTTPS port.
    [Fact]
    public void Health_endpoint_is_always_wired_before_https_redirection()
    {
        var (files, _) = Generate();
        var program = files["Program.cs"];

        Assert.Contains("AddHealthChecks()", program, StringComparison.Ordinal);
        var health = program.IndexOf("UseHealthChecks(\"/health\")", StringComparison.Ordinal);
        var redirect = program.IndexOf("UseHttpsRedirection()", StringComparison.Ordinal);
        Assert.True(health >= 0, "the /health endpoint is mapped");
        Assert.True(redirect >= 0 && health < redirect, "/health precedes UseHttpsRedirection so the probe gets plain-HTTP 200");
    }

    // --cqrs is wiring-only: the mediator call + the package ref, and no sample slice to delete.
    [Fact]
    public void Cqrs_flag_toggles_the_wiring_and_package_but_scaffolds_no_sample()
    {
        var (on, onResult) = Generate(cqrs: true);
        Assert.Contains("AddRaskCqrs", on["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("Rask.Cqrs", onResult.Packages);
        Assert.DoesNotContain("Cqrs/GreetingQuery.cs", on.Keys);
        Assert.DoesNotContain("Cqrs/GreetingPage.cs", on.Keys);

        var (off, offResult) = Generate(cqrs: false);
        Assert.DoesNotContain("AddRaskCqrs", off["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs", offResult.Packages);
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

    // "test every scenario" — all 16 flag combinations keep the invariants: core files always present,
    // no placeholder leakage, packages always include the framework, opt-in files exactly track their flag.
    [Theory]
    [MemberData(nameof(AllFlagCombinations))]
    public void Every_flag_combination_holds_the_invariants(bool auth, bool pwa, bool cqrs, bool docker)
    {
        var (files, result) = Generate(auth, pwa, cqrs, docker);

        foreach (var expected in AlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"[{auth},{pwa},{cqrs},{docker}] missing {expected}");
        }

        Assert.Contains("Rask.Server", result.Packages);
        Assert.Contains("Rask.Bootstrap", result.Packages);
        Assert.Equal(cqrs, result.Packages.Contains("Rask.Cqrs"));

        Assert.Equal(auth, files.ContainsKey("Features/Auth/CredentialStore.cs"));
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
        for (var mask = 0; mask < 16; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0];
        }
    }

    private static (Dictionary<string, string> Files, ScaffoldResult Result) Generate(
        bool auth = false, bool pwa = false, bool cqrs = false, bool docker = false, bool data = false)
    {
        var result = ProjectGenerator.GenerateServer(
            Root,
            "App",
            new ServerBatteries { Auth = auth, Pwa = pwa, Cqrs = cqrs, Data = data, Docker = docker },
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
        "App.csproj", "Program.cs", "Features/Shared/App.cs", "Features/Home/HomePage.cs",
        "wwwroot/index.html", "runtimeconfig.template.json",
    ];

    [Fact]
    public void Wasm_base_emits_core_files_and_the_wasm_packages()
    {
        var result = ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: false, Version);
        var files = Index(result);

        Assert.Equal(WasmAlwaysPresent.Order(), files.Keys.Order());

        Assert.Equal(["Rask.Wasm", "Rask.Bootstrap"], result.Packages);
        Assert.Contains("Microsoft.NET.Sdk.WebAssembly", files["App.csproj"], StringComparison.Ordinal);
        // A standalone SPA never carries the auth/pwa/docker opt-ins by default.
        Assert.DoesNotContain("Features/Auth/Auth.cs", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
    }

    [Fact]
    public void Wasm_auth_adds_the_jwt_files_and_the_framework_package_refs()
    {
        var on = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: true, pwa: false, docker: false, Version));
        Assert.True(on.ContainsKey("Features/Auth/Auth.cs"));
        Assert.True(on.ContainsKey("Features/Auth/LoginPage.cs"));
        Assert.True(on.ContainsKey("Features/Auth/MembersPage.cs"));
        // WASM has no Microsoft.AspNetCore.App framework ref, so the JWT scaffold pins these directly.
        Assert.Contains("<PackageReference Include=\"Microsoft.JSInterop\"", on["App.csproj"], StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Microsoft.AspNetCore.Authorization\"", on["App.csproj"], StringComparison.Ordinal);

        var off = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: false, Version));
        Assert.DoesNotContain("Features/Auth/Auth.cs", off.Keys);
        Assert.DoesNotContain("<PackageReference Include=\"Microsoft.JSInterop\"", off["App.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void Wasm_pwa_and_docker_toggle_their_files()
    {
        var pwa = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: true, docker: false, Version));
        Assert.True(pwa.ContainsKey("wwwroot/icon.svg"));
        Assert.Contains("serviceWorker", pwa["wwwroot/index.html"], StringComparison.Ordinal);

        var noPwa = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: false, Version));
        Assert.DoesNotContain("serviceWorker", noPwa["wwwroot/index.html"], StringComparison.Ordinal);

        var docker = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: true, Version));
        Assert.True(docker.ContainsKey("Dockerfile"));
        Assert.True(docker.ContainsKey("nginx.conf"));
        Assert.True(docker.ContainsKey(".dockerignore"));
    }

    [Theory]
    [MemberData(nameof(WasmFlagCombinations))]
    public void Every_wasm_flag_combination_holds_the_invariants(bool auth, bool pwa, bool docker)
    {
        var result = ProjectGenerator.GenerateWasm(Root, "App", auth, pwa, docker, Version);
        var files = Index(result);

        foreach (var expected in WasmAlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"[{auth},{pwa},{docker}] missing {expected}");
        }

        Assert.Contains("public sealed partial class HomePage : Component", files["Features/Home/HomePage.cs"], StringComparison.Ordinal);

        Assert.Equal(["Rask.Wasm", "Rask.Bootstrap"], result.Packages);
        Assert.Equal(auth, files.ContainsKey("Features/Auth/Auth.cs"));
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
        for (var mask = 0; mask < 8; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0];
        }
    }

    // ---- native template ----

    // Files both native hosts always emit.
    private static readonly string[] NativeShared =
    [
        "App.csproj", "Platforms/Android/AndroidManifest.xml", "Platforms/iOS/Info.plist",
        "Platforms/iOS/Main.cs",
    ];

    // The local-only component code + in-process platform heads.
    private static readonly string[] NativeLocalOnly =
    [
        "Features/Shared/App.cs", "Features/Home/HomePage.cs",
        "Platforms/iOS/AppDelegate.cs", "Platforms/Android/MainActivity.cs",
    ];

    // The server-only thin-shell platform heads.
    private static readonly string[] NativeServerOnly =
    [
        "Platforms/iOS/ServerAppDelegate.cs", "Platforms/Android/ServerActivity.cs",
    ];

    private static Dictionary<string, string> GenerateNative(string host) =>
        Index(ProjectGenerator.GenerateNative(Root, "App", host, Version));

    [Fact]
    public void Native_local_emits_the_shared_and_local_files_and_the_native_package()
    {
        var result = ProjectGenerator.GenerateNative(Root, "App", "local", Version);
        var files = Index(result);

        foreach (var expected in NativeShared.Concat(NativeLocalOnly))
        {
            Assert.True(files.ContainsKey(expected), $"expected {expected} to be generated");
        }

        // The server-host heads never appear in a local app.
        foreach (var serverOnly in NativeServerOnly)
        {
            Assert.DoesNotContain(serverOnly, files.Keys);
        }

        Assert.Equal(["Rask.Native"], result.Packages);
        // Multi-targets the two native TFMs.
        Assert.Contains("<TargetFrameworks>net10.0-ios;net10.0-android</TargetFrameworks>", files["App.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void Native_server_emits_the_server_heads_and_omits_the_local_files()
    {
        var files = GenerateNative("server");

        foreach (var expected in NativeShared.Concat(NativeServerOnly))
        {
            Assert.True(files.ContainsKey(expected), $"expected {expected} to be generated");
        }

        // None of the local-only component code / heads leak into the thin server shell.
        foreach (var localOnly in NativeLocalOnly)
        {
            Assert.DoesNotContain(localOnly, files.Keys);
        }
    }

    // The geolocation backend was a device-API demo; it went with the rest of the sample content. Its
    // permissions and registrations have to go with it, or the heads request a grant nothing consumes and
    // register a type that is not scaffolded (which would not compile).
    [Fact]
    public void Native_local_carries_no_geolocation_backend_permission_or_registration()
    {
        var local = GenerateNative("local");

        Assert.DoesNotContain("Platforms/iOS/NativeGeolocation.cs", local.Keys);
        Assert.DoesNotContain("Platforms/Android/NativeGeolocation.cs", local.Keys);
        Assert.DoesNotContain("NSLocationWhenInUseUsageDescription", local["Platforms/iOS/Info.plist"], StringComparison.Ordinal);
        Assert.DoesNotContain("ACCESS_FINE_LOCATION", local["Platforms/Android/AndroidManifest.xml"], StringComparison.Ordinal);

        foreach (var (path, content) in local)
        {
            Assert.DoesNotContain("NativeGeolocation", content, StringComparison.Ordinal);
            Assert.DoesNotContain("AccessFineLocation", content, StringComparison.Ordinal);
            _ = path;
        }

        // The notification backend is still registered inline by the heads, so its permission stays.
        Assert.Contains("POST_NOTIFICATIONS", local["Platforms/Android/AndroidManifest.xml"], StringComparison.Ordinal);
        Assert.DoesNotContain("POST_NOTIFICATIONS", GenerateNative("server")["Platforms/Android/AndroidManifest.xml"], StringComparison.Ordinal);
    }

    [Fact]
    public void Native_local_welcome_page_is_a_feature_slice_and_scaffolds_no_demo_pages()
    {
        var local = GenerateNative("local");

        Assert.DoesNotContain("Counter.cs", local.Keys);

        // The welcome page is its own Features/Home slice; the shell hosts the native chrome + Router.
        var home = local["Features/Home/HomePage.cs"];
        Assert.Contains("[Route(\"/\")]", home, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class HomePage : Component", home, StringComparison.Ordinal);

        // The tab bar linked Counter, which is gone. It may survive as a commented-out suggestion, but
        // nothing may still render it — an unresolved Counter() reference would not compile.
        var shell = local["Features/Shared/App.cs"];
        Assert.DoesNotContain("Counter()", shell, StringComparison.Ordinal);
        foreach (var line in shell.Split('\n').Where(l => l.Contains("NativeTab", StringComparison.Ordinal)))
        {
            Assert.StartsWith("//", line.Trim(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Native_conditional_markers_are_resolved_away_in_both_hosts()
    {
        foreach (var host in new[] { "local", "server" })
        {
            var files = GenerateNative(host);
            foreach (var (path, content) in files)
            {
                Assert.DoesNotContain("#if", content, StringComparison.Ordinal);
                Assert.DoesNotContain("#endif", content, StringComparison.Ordinal);
                Assert.DoesNotContain("(IsLocal)", content, StringComparison.Ordinal);
                _ = path;
            }
        }
    }

    [Fact]
    public void Native_replaces_the_placeholder_namespace_everywhere_for_both_hosts()
    {
        foreach (var host in new[] { "local", "server" })
        {
            var files = GenerateNative(host);
            foreach (var (path, content) in files)
            {
                Assert.DoesNotContain("Company.RaskNative", content, StringComparison.Ordinal);
                Assert.DoesNotContain("Company.RaskServer", content, StringComparison.Ordinal);
                Assert.DoesNotContain("Company.RaskNative", path, StringComparison.Ordinal);
                Assert.DoesNotContain("Company.RaskServer", path, StringComparison.Ordinal);
            }
        }
    }

    // ---- wasm-hosted template ----

    // The three-project trio a hosted app always emits: a solution, a Shared class library, the WASM Client
    // (SPA shell + welcome page + host), and the ASP.NET Server. Slim by default — no demo pages.
    private static readonly string[] WasmHostedAlwaysPresent =
    [
        "App.sln",
        "App.Shared/App.Shared.csproj", "App.Shared/Contracts.cs",
        "App.Client/App.Client.csproj", "App.Client/Program.cs",
        "App.Client/Features/Shared/App.cs", "App.Client/Features/Home/HomePage.cs",
        "App.Client/wwwroot/index.html", "App.Client/runtimeconfig.template.json",
        "App.Server/App.Server.csproj", "App.Server/Program.cs", "App.Server/Properties/launchSettings.json",
        "App.Server/appsettings.json", "App.Server/appsettings.Production.json",
    ];

    private static Dictionary<string, string> GenerateWasmHosted(bool auth = false, bool pwa = false, bool docker = false) =>
        Index(ProjectGenerator.GenerateWasmHosted(Root, "App", auth, pwa, docker, Version));

    [Fact]
    public void WasmHosted_base_emits_the_three_projects_and_restores_the_solution()
    {
        var result = ProjectGenerator.GenerateWasmHosted(Root, "App", auth: false, pwa: false, docker: false, Version);
        var files = Index(result);

        Assert.Equal(WasmHostedAlwaysPresent.Order(), files.Keys.Order());

        // Restore targets the solution (no root csproj), which pulls all three projects.
        Assert.Equal("App.sln", result.RestoreTarget);
        Assert.Equal(["Rask.Wasm", "Rask.Bootstrap", "Rask.Wasm.Hosting"], result.Packages);

        // No opt-in artifacts leak in, and no demo content survives the slimming.
        Assert.DoesNotContain("App.Client/Features/Auth/Auth.cs", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
        Assert.DoesNotContain("App.Client/wwwroot/icon.svg", files.Keys);
        foreach (var demo in new[] { "Counter.cs", "Weather.cs", "WeatherForecast.cs", "LocalWeatherForecastService.cs" })
        {
            Assert.DoesNotContain(files.Keys, k => k.Contains(demo, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void WasmHosted_wires_the_projects_with_the_right_namespaces_and_references()
    {
        var files = GenerateWasmHosted();

        // Each project owns its Client/Server/Shared namespace; the shell + welcome page are the shared ones, re-homed.
        Assert.Contains("namespace App.Client.Features.Shared;", files["App.Client/Features/Shared/App.cs"], StringComparison.Ordinal);
        Assert.Contains("public sealed partial class HomePage : Component", files["App.Client/Features/Home/HomePage.cs"], StringComparison.Ordinal);
        Assert.Contains("namespace App.Client.Features.Home;", files["App.Client/Features/Home/HomePage.cs"], StringComparison.Ordinal);
        Assert.Contains("namespace App.Shared;", files["App.Shared/Contracts.cs"], StringComparison.Ordinal);

        // Client references Shared.
        Assert.Contains("App.Shared\\App.Shared.csproj", files["App.Client/App.Client.csproj"], StringComparison.Ordinal);

        // Server references Shared normally + the Client cross-TFM (bundle publish), and pins the version.
        var server = files["App.Server/App.Server.csproj"];
        Assert.Contains("<PackageReference Include=\"Rask.Wasm.Hosting\" Version=\"9.9.9\"/>", server, StringComparison.Ordinal);
        Assert.Contains("App.Shared\\App.Shared.csproj", server, StringComparison.Ordinal);
        Assert.Contains("App.Client\\App.Client.csproj", server, StringComparison.Ordinal);
        Assert.Contains("ReferenceOutputAssembly=\"false\"", server, StringComparison.Ordinal);
        Assert.Contains("<StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>", server, StringComparison.Ordinal);
        // The static-file host serves the bundle without running components — non-generic UseRask.
        Assert.Contains("app.UseRask();", files["App.Server/Program.cs"], StringComparison.Ordinal);

        // The sln lists all three projects.
        var sln = files["App.sln"];
        foreach (var proj in new[] { "App.Client", "App.Server", "App.Shared" })
        {
            Assert.Contains(proj, sln, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WasmHosted_auth_puts_the_shared_dtos_in_the_shared_project()
    {
        var on = GenerateWasmHosted(auth: true);
        Assert.True(on.ContainsKey("App.Client/Features/Auth/Auth.cs"));
        Assert.True(on.ContainsKey("App.Client/Features/Auth/LoginPage.cs"));
        Assert.True(on.ContainsKey("App.Client/Features/Auth/MembersPage.cs"));
        Assert.True(on.ContainsKey("App.Server/Features/Auth/CredentialStore.cs"));

        // The dedup win: LoginRequest/MeDto live in Shared, referenced by both sides (not redefined).
        var contracts = on["App.Shared/Contracts.cs"];
        Assert.Contains("record LoginRequest", contracts, StringComparison.Ordinal);
        Assert.Contains("record MeDto", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("record LoginRequest", on["App.Server/Features/Auth/CredentialStore.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("record LoginRequest", on["App.Client/Features/Auth/Auth.cs"], StringComparison.Ordinal);
        Assert.Contains("using App.Shared;", on["App.Server/Program.cs"], StringComparison.Ordinal);
        Assert.Contains("AddAuthentication", on["App.Server/Program.cs"], StringComparison.Ordinal);

        var off = GenerateWasmHosted(auth: false);
        Assert.DoesNotContain("App.Client/Features/Auth/Auth.cs", off.Keys);
        Assert.DoesNotContain("record LoginRequest", off["App.Shared/Contracts.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("AddAuthentication", off["App.Server/Program.cs"], StringComparison.Ordinal);
    }

    // The hosted Server also ships /health before UseHttpsRedirection — same deploy-probe contract as server.
    [Fact]
    public void WasmHosted_server_wires_health_before_https_redirection()
    {
        var program = GenerateWasmHosted()["App.Server/Program.cs"];

        Assert.Contains("AddHealthChecks()", program, StringComparison.Ordinal);
        var health = program.IndexOf("UseHealthChecks(\"/health\")", StringComparison.Ordinal);
        var redirect = program.IndexOf("UseHttpsRedirection()", StringComparison.Ordinal);
        Assert.True(health >= 0 && redirect >= 0 && health < redirect, "/health precedes UseHttpsRedirection");
    }

    // ...and the same key-ring contract: this host issues the auth cookie for the WASM client, so a
    // deploy that replaces the container would sign every user out without a persisted ring.
    [Fact]
    public void WasmHosted_server_persists_data_protection_keys_to_the_mounted_volume()
    {
        var program = GenerateWasmHosted()["App.Server/Program.cs"];

        Assert.Contains("using Microsoft.AspNetCore.DataProtection;", program, StringComparison.Ordinal);
        Assert.Contains(".PersistKeysToFileSystem(", program, StringComparison.Ordinal);
        Assert.Contains("\"/data/keys\"", program, StringComparison.Ordinal);
        Assert.Contains(".SetApplicationName(", program, StringComparison.Ordinal);
        Assert.Contains("Directory.Exists(\"/data\")", program, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmHosted_pwa_and_docker_toggle_their_files()
    {
        var pwa = GenerateWasmHosted(pwa: true);
        Assert.True(pwa.ContainsKey("App.Client/wwwroot/icon.svg"));
        Assert.Contains("serviceWorker", pwa["App.Client/wwwroot/index.html"], StringComparison.Ordinal);
        Assert.Contains("UsePwa", pwa["App.Client/Program.cs"], StringComparison.Ordinal);

        var noPwa = GenerateWasmHosted(pwa: false);
        Assert.DoesNotContain("serviceWorker", noPwa["App.Client/wwwroot/index.html"], StringComparison.Ordinal);

        var docker = GenerateWasmHosted(docker: true);
        Assert.True(docker.ContainsKey("Dockerfile"));
        Assert.True(docker.ContainsKey(".dockerignore"));
        // The Dockerfile publishes the Server (which bakes the client bundle), not a static host.
        Assert.Contains("App.Server.dll", docker["Dockerfile"], StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(WasmFlagCombinations))]
    public void Every_wasm_hosted_flag_combination_holds_the_invariants(bool auth, bool pwa, bool docker)
    {
        var files = GenerateWasmHosted(auth, pwa, docker);

        foreach (var expected in WasmHostedAlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"[{auth},{pwa},{docker}] missing {expected}");
        }

        Assert.Equal(auth, files.ContainsKey("App.Client/Features/Auth/Auth.cs"));
        Assert.Equal(pwa, files.ContainsKey("App.Client/wwwroot/icon.svg"));
        Assert.Equal(docker, files.ContainsKey("Dockerfile"));

        // The placeholder namespace is rewritten in every file's content and path.
        foreach (var (path, content) in files)
        {
            Assert.DoesNotContain("Company.RaskServer", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Company.RaskServer", path, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The wasm <c>--auth</c> scaffold pins Microsoft.JSInterop / Microsoft.AspNetCore.Authorization itself.
    /// Rask.Wasm references the same two packages, so its nuspec demands <c>&gt;=</c> whatever
    /// Directory.Packages.props pins — scaffolding anything lower puts the generated project below its own
    /// dependency, which NuGet reports as a downgrade (NU1605) and <c>-warnaserror</c> turns into a failed
    /// build. The two drifted apart once already (10.0.9 vs 10.0.10); this keeps them married.
    /// </summary>
    [Theory]
    [InlineData("Microsoft.JSInterop")]
    [InlineData("Microsoft.AspNetCore.Authorization")]
    public void Wasm_auth_framework_version_matches_the_repo_pin(string package)
    {
        var props = File.ReadAllText(Path.Combine(FindRepoRoot(), "Directory.Packages.props"));
        var match = Regex.Match(props, $"""<PackageVersion\s+Include="{Regex.Escape(package)}"\s+Version="([^"]+)"\s*/>""");

        Assert.True(match.Success, $"{package} is not pinned in Directory.Packages.props.");
        Assert.Equal(ProjectGenerator.AspNetCoreFrameworkVersion, match.Groups[1].Value);
    }

    private static string FindRepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}

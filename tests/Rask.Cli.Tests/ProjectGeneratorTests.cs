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
        // For the editor, not the build: scoped TypeScript is compiled by tsgo with explicit flags,
        // but without a tsconfig an author gets no checking and no completion while writing it.
        "Styles/app.css",
        "tsconfig.json",
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
    /// .gitignore, the .editorconfig and the solution (see ProjectGenerator.ProjectHygiene).
    /// </summary>
    private static string[] WithHygiene(IEnumerable<string> files) =>
        [.. files, ".gitignore", ".editorconfig", "App.slnx"];

    [Fact]
    public void Base_project_emits_the_core_files_and_packages_with_no_flags()
    {
        var (files, result) = Generate();

        Assert.Equal(WithHygiene(AlwaysPresent).Order(), files.Keys.Order());

        // Just the framework -- and the framework is all a styled app needs. Tailwind is built INTO
        // Rask.Server (RaskTailwindBuildPack), so a scaffolded csproj names no styling package at all:
        // naming one would import the same targets a second time and run the compiler twice.
        Assert.Equal(["Rask.Server"], result.Packages);
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

        // The welcome page is its own Features/Home slice (no scoped .css to pair with).
        var home = files["Features/Home/HomePage.cs"];
        Assert.Contains("[Route(\"/\")]", home, StringComparison.Ordinal);
        Assert.Contains("public sealed partial class HomePage : Component", home, StringComparison.Ordinal);

        // The welcome copy points at the file it actually lives in.
        Assert.Contains("HomePage.cs", home, StringComparison.Ordinal);

        // The page is written in Tailwind, which is what every project gets: the classes here are the
        // ones its own build scans this file for.
        Assert.Contains("rounded-xl", Generate().Files["Features/Home/HomePage.cs"], StringComparison.Ordinal);
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
        // With --cqrs, so the pin is checked on an opt-in package rather than only on the framework —
        // the version has to be stamped on every reference the template emits, not most.
        var (files, _) = Generate(cqrs: true);
        var csproj = files["App.csproj"];

        Assert.Contains("<PackageReference Include=\"Rask.Server\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.Cqrs\" Version=\"9.9.9\"/>", csproj, StringComparison.Ordinal);

        // And nothing pins a Tailwind package, because there is no Tailwind package to pin.
        Assert.DoesNotContain("Rask.Tailwind", Generate().Files["App.csproj"], StringComparison.Ordinal);
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
        Assert.Equal(cqrs, result.Packages.Contains("Rask.Cqrs"));

        // Tailwind is built in, so no combination REFERENCES it and every combination still gets it --
        // it rides inside Rask.Server. This assertion has now been all four things in turn: Bootstrap
        // always present, then neither present, then Tailwind always referenced, now Tailwind never
        // referenced because it is not a reference any more. Which is exactly why it is asserted on
        // every combination rather than assumed.
        Assert.DoesNotContain("Rask.Tailwind", result.Packages);
        Assert.DoesNotContain("Rask.Bootstrap", result.Packages);

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
        // For the editor, not the build — see AlwaysPresent.
        "Styles/app.css",
        "tsconfig.json",
    ];

    [Fact]
    public void Wasm_base_emits_core_files_and_the_wasm_packages()
    {
        var result = ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: false, Version);
        var files = Index(result);

        Assert.Equal(WithHygiene(WasmAlwaysPresent).Order(), files.Keys.Order());

        Assert.Equal(["Rask.Wasm"], result.Packages);
        Assert.Contains("Microsoft.NET.Sdk.WebAssembly", files["App.csproj"], StringComparison.Ordinal);
        // A standalone SPA never carries the auth/pwa/docker opt-ins by default.
        Assert.DoesNotContain("Features/Auth/Auth.cs", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
    }

    /// <summary>The styling axis reaches the browser-WASM template, all three answers.</summary>
    /// <remarks>
    ///     It did not until #838: this generator took a <c>bool bootstrap</c> beside a ServerBatteries that
    ///     already carried Styling, so <c>--tailwind</c> scaffolded a plain project and reported success.
    ///     One parameter now, read off the batteries — two sources for one decision is the bug.
    /// </remarks>
    /// <summary>The query cache arrives with the dispatcher, not behind a flag of its own.</summary>
    /// <remarks>
    ///     <para>
    ///         A dispatcher without a cache means every render refetches, so the first thing anyone
    ///         building a page over <c>IDispatcher</c> needs is the thing that stops that. Shipping it as
    ///         an opt-in made it discoverable only by reading the docs — which is how a package ends up
    ///         written, tested, documented and unused.
    ///     </para>
    ///     <para>
    ///         Tied to <c>--cqrs</c> rather than always-on because it wraps a dispatcher: with no messages
    ///         to send there is nothing for it to cache.
    ///     </para>
    /// </remarks>
    [Fact]
    public void The_query_cache_rides_along_with_cqrs()
    {
        var with = ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries { Cqrs = true }, Version);
        var files = Index(with);

        Assert.Contains("Rask.Query", with.Packages);
        Assert.Contains("<PackageReference Include=\"Rask.Query\"", files["App.csproj"], StringComparison.Ordinal);

        // Registered as well as referenced: a package reference with no AddRaskQuery() is a dependency
        // the app carries and cannot use.
        Assert.Contains("builder.Services.AddRaskQuery();", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("using Rask.Query;", files["Program.cs"], StringComparison.Ordinal);

        // And not otherwise — an app with no dispatcher has nothing to wrap.
        var without = ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), Version);

        Assert.DoesNotContain("Rask.Query", without.Packages);
        Assert.DoesNotContain(
            "AddRaskQuery", Index(without)["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_hosted_client_gets_the_query_cache_with_its_remote_dispatcher()
    {
        var result = ProjectGenerator.GenerateWasmHosted(
            Root, "App", new ServerBatteries { Cqrs = true }, Version);
        var files = Index(result);

        // Asserted on the csproj rather than on ScaffoldResult.Packages: that list is the summary the
        // CLI prints, and for this template it names the three framework packages only — Rask.Cqrs.Client
        // is absent from it too. The csproj is what restore reads.
        //
        // The client half, where every dispatch is a network round trip — so a component that refetches
        // on each render pays for it over the wire.
        Assert.Contains(
            "<PackageReference Include=\"Rask.Query\"", files["App.Client/App.Client.csproj"], StringComparison.Ordinal);
        Assert.Contains("host.Services.AddRaskQuery();", files["App.Client/Program.cs"], StringComparison.Ordinal);
    }

    // The browser template compiles its own stylesheet like every other host. It was the one worth
    // spelling out while styling was a choice, because the WASM SDK publishes wwwroot as it finds it and a
    // stylesheet written after the publish would simply not be there.
    [Fact]
    public void Wasm_compiles_its_own_stylesheet()
    {
        var result = ProjectGenerator.GenerateWasm(
            Root, "App", auth: false, pwa: false, docker: false, Version, new ServerBatteries());
        var files = Index(result);

        Assert.Equal(["Rask.Wasm"], result.Packages);
        Assert.Contains("@import \"tailwindcss\";", files["Styles/app.css"], StringComparison.Ordinal);

        // The csproj names Rask.Wasm and nothing else for styling: the Tailwind build ships inside it.
        Assert.DoesNotContain("Rask.Tailwind", files["App.csproj"], StringComparison.Ordinal);

        // The head has to point at what the build writes, or the stylesheet is compiled and never served.
        Assert.Contains("/css/app.css", files["Features/Shared/App.cs"], StringComparison.Ordinal);
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

        // Plain is what you get by not choosing, here as everywhere else — so the base package set is
        // Rask.Wasm alone. Bootstrap and Tailwind are covered by their own case below.
        Assert.Equal(["Rask.Wasm"], result.Packages);
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
}

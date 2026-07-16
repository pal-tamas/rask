using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class ProjectGeneratorTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    // Files the server template always emits, whatever the flags. A new project is deliberately minimal:
    // the shell + welcome page (both in App.cs), the entry point, the csproj and the launch profile.
    private static readonly string[] AlwaysPresent =
    [
        "App.csproj", "Program.cs", "App.cs", "Properties/launchSettings.json",
    ];

    // Demo content `rask new` used to scaffold and deliberately no longer does — a new project is one file
    // of components, not a folder of samples to delete. Guards against any of it creeping back.
    private static readonly string[] NeverPresent =
    [
        "HomePage.cs", "HomePage.css", "Counter.cs", "Weather.cs", "WeatherForecast.cs",
        "LocalWeatherForecastService.cs", "README.md", "AGENTS.md",
    ];

    [Fact]
    public void Base_project_emits_the_core_files_and_packages_with_no_flags()
    {
        var (files, result) = Generate();

        Assert.Equal(AlwaysPresent.Order(), files.Keys.Order());

        Assert.Equal(["Rask.Server", "Rask.Bootstrap"], result.Packages);
        // No opt-in artifacts leak in.
        Assert.DoesNotContain("Auth/CredentialStore.cs", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
    }

    [Fact]
    public void The_welcome_page_lives_in_App_cs_and_no_demo_files_are_scaffolded()
    {
        var (files, _) = Generate(auth: true, pwa: true, cqrs: true, docker: true);

        foreach (var gone in NeverPresent)
        {
            Assert.DoesNotContain(gone, files.Keys);
        }

        // App.cs carries BOTH the shell and the routed welcome page.
        var app = files["App.cs"];
        Assert.Contains("public sealed class App : Component", app, StringComparison.Ordinal);
        Assert.Contains("[Route(\"/\")]", app, StringComparison.Ordinal);
        Assert.Contains("public sealed class HomePage : Component", app, StringComparison.Ordinal);
        Assert.Contains("Router()", app, StringComparison.Ordinal);
        // Styled by Bootstrap — there is no scoped .css companion to pair with.
        Assert.Contains("BsCard", app, StringComparison.Ordinal);
        // The welcome copy points at the file it actually lives in.
        Assert.Contains("Code()[\"App.cs\"]", app, StringComparison.Ordinal);
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

        // Program.cs uses top-level statements (no namespace) but references the app namespace.
        Assert.Contains("using App;", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("namespace App;", files["App.cs"], StringComparison.Ordinal);
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
    public void Auth_flag_toggles_the_auth_files_and_wiring()
    {
        var (on, _) = Generate(auth: true);
        Assert.True(on.ContainsKey("Auth/CredentialStore.cs"));
        Assert.True(on.ContainsKey("Auth/LoginPage.cs"));
        Assert.True(on.ContainsKey("Auth/MembersPage.cs"));
        Assert.Contains("AddAuthentication", on["Program.cs"], StringComparison.Ordinal);

        var (off, _) = Generate(auth: false);
        Assert.DoesNotContain("Auth/CredentialStore.cs", off.Keys);
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

        Assert.Equal(auth, files.ContainsKey("Auth/CredentialStore.cs"));
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
        bool auth = false, bool pwa = false, bool cqrs = false, bool docker = false)
    {
        var result = ProjectGenerator.GenerateServer(Root, "App", auth, pwa, cqrs, docker, Version);
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
        "App.csproj", "Program.cs", "App.cs", "wwwroot/index.html", "runtimeconfig.template.json",
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
        Assert.DoesNotContain("Auth/Auth.cs", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
    }

    [Fact]
    public void Wasm_auth_adds_the_jwt_files_and_the_framework_package_refs()
    {
        var on = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: true, pwa: false, docker: false, Version));
        Assert.True(on.ContainsKey("Auth/Auth.cs"));
        Assert.True(on.ContainsKey("Auth/LoginPage.cs"));
        Assert.True(on.ContainsKey("Auth/MembersPage.cs"));
        // WASM has no Microsoft.AspNetCore.App framework ref, so the JWT scaffold pins these directly.
        Assert.Contains("<PackageReference Include=\"Microsoft.JSInterop\"", on["App.csproj"], StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Microsoft.AspNetCore.Authorization\"", on["App.csproj"], StringComparison.Ordinal);

        var off = Index(ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: false, Version));
        Assert.DoesNotContain("Auth/Auth.cs", off.Keys);
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

        Assert.Contains("public sealed class HomePage : Component", files["App.cs"], StringComparison.Ordinal);

        Assert.Equal(["Rask.Wasm", "Rask.Bootstrap"], result.Packages);
        Assert.Equal(auth, files.ContainsKey("Auth/Auth.cs"));
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
        "App.cs", "Platforms/iOS/AppDelegate.cs", "Platforms/Android/MainActivity.cs",
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
    public void Native_local_puts_the_welcome_page_in_App_cs_and_scaffolds_no_demo_pages()
    {
        var local = GenerateNative("local");

        Assert.DoesNotContain("HomePage.cs", local.Keys);
        Assert.DoesNotContain("Counter.cs", local.Keys);
        var app = local["App.cs"];
        Assert.Contains("[Route(\"/\")]", app, StringComparison.Ordinal);
        Assert.Contains("public sealed class HomePage : Component", app, StringComparison.Ordinal);

        // The tab bar linked Counter, which is gone. It may survive as a commented-out suggestion, but
        // nothing may still render it — an unresolved Counter() reference would not compile.
        Assert.DoesNotContain("Counter()", app, StringComparison.Ordinal);
        foreach (var line in app.Split('\n').Where(l => l.Contains("NativeTab", StringComparison.Ordinal)))
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
        "App.Client/App.Client.csproj", "App.Client/Program.cs", "App.Client/App.cs",
        "App.Client/wwwroot/index.html", "App.Client/runtimeconfig.template.json",
        "App.Server/App.Server.csproj", "App.Server/Program.cs", "App.Server/Properties/launchSettings.json",
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
        Assert.DoesNotContain("App.Client/Auth/Auth.cs", files.Keys);
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

        // Each project owns its Client/Server/Shared namespace; the welcome shell is the shared one, re-homed.
        Assert.Contains("namespace App.Client;", files["App.Client/App.cs"], StringComparison.Ordinal);
        Assert.Contains("public sealed class HomePage : Component", files["App.Client/App.cs"], StringComparison.Ordinal);
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
        Assert.True(on.ContainsKey("App.Client/Auth/Auth.cs"));
        Assert.True(on.ContainsKey("App.Client/Auth/LoginPage.cs"));
        Assert.True(on.ContainsKey("App.Client/Auth/MembersPage.cs"));
        Assert.True(on.ContainsKey("App.Server/Auth/CredentialStore.cs"));

        // The dedup win: LoginRequest/MeDto live in Shared, referenced by both sides (not redefined).
        var contracts = on["App.Shared/Contracts.cs"];
        Assert.Contains("record LoginRequest", contracts, StringComparison.Ordinal);
        Assert.Contains("record MeDto", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("record LoginRequest", on["App.Server/Auth/CredentialStore.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("record LoginRequest", on["App.Client/Auth/Auth.cs"], StringComparison.Ordinal);
        Assert.Contains("using App.Shared;", on["App.Server/Program.cs"], StringComparison.Ordinal);
        Assert.Contains("AddAuthentication", on["App.Server/Program.cs"], StringComparison.Ordinal);

        var off = GenerateWasmHosted(auth: false);
        Assert.DoesNotContain("App.Client/Auth/Auth.cs", off.Keys);
        Assert.DoesNotContain("record LoginRequest", off["App.Shared/Contracts.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("AddAuthentication", off["App.Server/Program.cs"], StringComparison.Ordinal);
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

        Assert.Equal(auth, files.ContainsKey("App.Client/Auth/Auth.cs"));
        Assert.Equal(pwa, files.ContainsKey("App.Client/wwwroot/icon.svg"));
        Assert.Equal(docker, files.ContainsKey("Dockerfile"));

        // The placeholder namespace is rewritten in every file's content and path.
        foreach (var (path, content) in files)
        {
            Assert.DoesNotContain("Company.RaskServer", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Company.RaskServer", path, StringComparison.Ordinal);
        }
    }
}

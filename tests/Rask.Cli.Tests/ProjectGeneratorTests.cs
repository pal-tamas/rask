using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class ProjectGeneratorTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    // Files the server template always emits, whatever the flags.
    private static readonly string[] AlwaysPresent =
    [
        "App.csproj", "Program.cs", "App.cs", "HomePage.cs", "HomePage.css", "Counter.cs",
        "Weather.cs", "WeatherForecast.cs", "LocalWeatherForecastService.cs",
        "Properties/launchSettings.json", "README.md", "AGENTS.md",
    ];

    [Fact]
    public void Base_project_emits_the_core_files_and_packages_with_no_flags()
    {
        var (files, result) = Generate();

        foreach (var expected in AlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"expected {expected} to be generated");
        }

        Assert.Equal(["Rask.Server", "Rask.Bootstrap"], result.Packages);
        // No opt-in artifacts leak in.
        Assert.DoesNotContain("Auth/CredentialStore.cs", files.Keys);
        Assert.DoesNotContain("Cqrs/GreetingQuery.cs", files.Keys);
        Assert.DoesNotContain("Dockerfile", files.Keys);
        Assert.DoesNotContain("wwwroot/icon.svg", files.Keys);
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
        Assert.Contains("namespace App;", files["HomePage.cs"], StringComparison.Ordinal);
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

    [Fact]
    public void Cqrs_flag_toggles_files_wiring_package_and_nav()
    {
        var (on, onResult) = Generate(cqrs: true);
        Assert.True(on.ContainsKey("Cqrs/GreetingQuery.cs"));
        Assert.True(on.ContainsKey("Cqrs/GreetingPage.cs"));
        Assert.Contains("AddRaskCqrs", on["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("Rask.Cqrs", onResult.Packages);
        Assert.Contains("GreetingPage()", on["App.cs"], StringComparison.Ordinal); // nav link

        var (off, offResult) = Generate(cqrs: false);
        Assert.DoesNotContain("Cqrs/GreetingQuery.cs", off.Keys);
        Assert.DoesNotContain("AddRaskCqrs", off["Program.cs"], StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.Cqrs", offResult.Packages);
        Assert.DoesNotContain("GreetingPage()", off["App.cs"], StringComparison.Ordinal);
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
        Assert.Equal(cqrs, files.ContainsKey("Cqrs/GreetingQuery.cs"));
        Assert.Equal(docker, files.ContainsKey("Dockerfile"));

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
        "App.csproj", "Program.cs", "App.cs", "HomePage.cs", "HomePage.css", "Counter.cs",
        "Weather.cs", "WeatherForecast.cs", "LocalWeatherForecastService.cs",
        "wwwroot/index.html", "runtimeconfig.template.json", "README.md", "AGENTS.md",
    ];

    [Fact]
    public void Wasm_base_emits_core_files_and_the_wasm_packages()
    {
        var result = ProjectGenerator.GenerateWasm(Root, "App", auth: false, pwa: false, docker: false, Version);
        var files = Index(result);

        foreach (var expected in WasmAlwaysPresent)
        {
            Assert.True(files.ContainsKey(expected), $"expected {expected} to be generated");
        }

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

        Assert.Equal(["Rask.Wasm", "Rask.Bootstrap"], result.Packages);
        Assert.Equal(auth, files.ContainsKey("Auth/Auth.cs"));
        Assert.Equal(pwa, files.ContainsKey("wwwroot/icon.svg"));
        Assert.Equal(docker, files.ContainsKey("Dockerfile"));

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
        "Platforms/iOS/Main.cs", "README.md", "AGENTS.md",
    ];

    // The local-only shared component code + in-process platform heads.
    private static readonly string[] NativeLocalOnly =
    [
        "App.cs", "HomePage.cs", "Counter.cs",
        "Platforms/iOS/AppDelegate.cs", "Platforms/iOS/NativeGeolocation.cs",
        "Platforms/Android/MainActivity.cs", "Platforms/Android/NativeGeolocation.cs",
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

    [Fact]
    public void Native_info_plist_carries_the_location_key_only_for_local()
    {
        var local = GenerateNative("local");
        var server = GenerateNative("server");

        Assert.Contains("NSLocationWhenInUseUsageDescription", local["Platforms/iOS/Info.plist"], StringComparison.Ordinal);
        Assert.DoesNotContain("NSLocationWhenInUseUsageDescription", server["Platforms/iOS/Info.plist"], StringComparison.Ordinal);
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
}

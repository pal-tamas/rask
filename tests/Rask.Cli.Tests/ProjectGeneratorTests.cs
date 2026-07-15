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
        var files = result.Files.ToDictionary(
            f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
            f => f.Content,
            StringComparer.Ordinal);
        return (files, result);
    }
}

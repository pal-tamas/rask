using System.Text.Json;

namespace Rask.Generators.Tests;

// Guards the `dotnet new` template configs (src/Rask.Templates/content/*). These aren't compiled,
// so a renamed symbol, a broken template.json, or a dropped Auth exclusion would otherwise only be
// caught by a human running `dotnet new`. These checks are fast and need no pack/restore — they
// validate the metadata, not a generated build.
public class TemplateConfigTests
{
    private static readonly string TemplatesRoot =
        Path.Combine(RepoRoot(), "src", "Rask.Templates", "content");

    public static IEnumerable<object[]> Templates =>
    [
        ["rask-server", "Company.RaskServer"],
        ["rask-wasm", "Company.RaskWasm"],
        ["rask-wasm-hosted", "Company.RaskWasmHosted"]
    ];

    // `--pwa` exists on all three templates, but the scaffolding differs: the WASM templates register
    // the service worker from index.html and exclude wwwroot/icon.svg when off; the Server template
    // calls AddRaskPwa (which serves + registers the SW) and excludes its whole wwwroot/** when off (a
    // Server PWA is installable + push-capable, not an offline app).
    public static IEnumerable<object[]> AllPwaTemplates =>
    [
        ["rask-server"],
        ["rask-wasm"],
        ["rask-wasm-hosted"]
    ];

    // The WASM-specific PWA scaffolding (index.html SW registration + icon.svg exclusion).
    public static IEnumerable<object[]> WasmPwaTemplates =>
    [
        ["rask-wasm"],
        ["rask-wasm-hosted"]
    ];

    [Theory]
    [MemberData(nameof(Templates))]
    public void TemplateJson_IsValid_WithExpectedIdentity(string shortName, string sourceName)
    {
        var dir = Path.Combine(TemplatesRoot, shortName);
        Assert.True(Directory.Exists(dir), $"template directory missing: {dir}");

        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(dir, ".template.config", "template.json")));
        var root = doc.RootElement;

        var shortNames = root.GetProperty("shortName").EnumerateArray().Select(e => e.GetString());
        Assert.Contains(shortName, shortNames);
        Assert.Equal(sourceName, root.GetProperty("sourceName").GetString());
        Assert.Equal("project", root.GetProperty("tags").GetProperty("type").GetString());
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void AuthSymbol_IsBoolean_DefaultsFalse(string shortName, string _)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, shortName, ".template.config", "template.json")));

        var auth = doc.RootElement.GetProperty("symbols").GetProperty("auth");
        Assert.Equal("parameter", auth.GetProperty("type").GetString());
        Assert.Equal("bool", auth.GetProperty("datatype").GetString());
        Assert.Equal("false", auth.GetProperty("defaultValue").GetString());
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Sources_ExcludeAuthFolder_WhenAuthOff(string shortName, string _)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, shortName, ".template.config", "template.json")));

        var modifiers = doc.RootElement.GetProperty("sources")
            .EnumerateArray()
            .SelectMany(s => s.GetProperty("modifiers").EnumerateArray());

        var authExclusion = modifiers.FirstOrDefault(m =>
            m.TryGetProperty("condition", out var c) && c.GetString() == "(!auth)");

        Assert.True(authExclusion.ValueKind == JsonValueKind.Object,
            "expected a (!auth) source modifier");
        var excludes = authExclusion.GetProperty("exclude").EnumerateArray().Select(e => e.GetString());
        Assert.Contains(excludes, e => e!.Contains("Auth", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void AuthScaffolding_Exists_AndProgramHasAuthConditionals(string shortName, string _)
    {
        var dir = Path.Combine(TemplatesRoot, shortName);

        // The Auth/** sources excluded by (!auth) must actually be present.
        Assert.NotEmpty(Directory.GetDirectories(dir, "Auth", SearchOption.AllDirectories));

        // Program.cs wires auth behind //#if (auth) blocks that the engine strips when auth is off.
        var hasAuthConditional = Directory
            .GetFiles(dir, "Program.cs", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("#if (auth)", StringComparison.Ordinal));
        Assert.True(hasAuthConditional, $"{shortName}: no Program.cs with a //#if (auth) block");
    }

    [Theory]
    [MemberData(nameof(AllPwaTemplates))]
    public void PwaSymbol_IsBoolean_DefaultsFalse(string shortName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, shortName, ".template.config", "template.json")));

        var pwa = doc.RootElement.GetProperty("symbols").GetProperty("pwa");
        Assert.Equal("parameter", pwa.GetProperty("type").GetString());
        Assert.Equal("bool", pwa.GetProperty("datatype").GetString());
        Assert.Equal("false", pwa.GetProperty("defaultValue").GetString());
    }

    [Fact]
    public void ServerPwa_ExcludesWwwroot_WhenPwaOff_AndProgramHasPwaConditional()
    {
        var dir = Path.Combine(TemplatesRoot, "rask-server");

        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(dir, ".template.config", "template.json")));
        var pwaExclusion = doc.RootElement.GetProperty("sources").EnumerateArray()
            .SelectMany(s => s.GetProperty("modifiers").EnumerateArray())
            .FirstOrDefault(m => m.TryGetProperty("condition", out var c) && c.GetString() == "(!pwa)");
        Assert.True(pwaExclusion.ValueKind == JsonValueKind.Object, "expected a (!pwa) source modifier");
        var excludes = pwaExclusion.GetProperty("exclude").EnumerateArray().Select(e => e.GetString());
        Assert.Contains(excludes, e => e!.Contains("wwwroot", StringComparison.Ordinal));

        // The PWA assets excluded by (!pwa) must actually be present to ship.
        Assert.NotEmpty(Directory.GetFiles(dir, "icon.svg", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(dir, "offline.html", SearchOption.AllDirectories));

        // Program.cs wires AddRaskPwa behind a //#if (pwa) block the engine strips when pwa is off.
        Assert.Contains("#if (pwa)",
            File.ReadAllText(Path.Combine(dir, "Program.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void CqrsSymbol_IsBoolean_DefaultsFalse()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, "rask-server", ".template.config", "template.json")));

        var cqrs = doc.RootElement.GetProperty("symbols").GetProperty("cqrs");
        Assert.Equal("parameter", cqrs.GetProperty("type").GetString());
        Assert.Equal("bool", cqrs.GetProperty("datatype").GetString());
        Assert.Equal("false", cqrs.GetProperty("defaultValue").GetString());
    }

    [Fact]
    public void ServerCqrs_ExcludesCqrsFolder_WhenCqrsOff_AndProgramAndCsprojHaveConditionals()
    {
        var dir = Path.Combine(TemplatesRoot, "rask-server");

        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(dir, ".template.config", "template.json")));
        var cqrsExclusion = doc.RootElement.GetProperty("sources").EnumerateArray()
            .SelectMany(s => s.GetProperty("modifiers").EnumerateArray())
            .FirstOrDefault(m => m.TryGetProperty("condition", out var c) && c.GetString() == "(!cqrs)");
        Assert.True(cqrsExclusion.ValueKind == JsonValueKind.Object, "expected a (!cqrs) source modifier");
        var excludes = cqrsExclusion.GetProperty("exclude").EnumerateArray().Select(e => e.GetString());
        Assert.Contains(excludes, e => e!.Contains("Cqrs", StringComparison.Ordinal));

        // The Cqrs/** sources excluded by (!cqrs) — the query+handler and the demo page — must ship.
        Assert.NotEmpty(Directory.GetFiles(dir, "GreetingQuery.cs", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(dir, "GreetingPage.cs", SearchOption.AllDirectories));

        // Program.cs wires AddRaskCqrs behind //#if (cqrs); the csproj adds the package ref behind
        // <!--#if (cqrs)-->. Both are stripped when cqrs is off.
        Assert.Contains("#if (cqrs)",
            File.ReadAllText(Path.Combine(dir, "Program.cs")), StringComparison.Ordinal);
        var csproj = File.ReadAllText(Path.Combine(dir, "Company.RaskServer.csproj"));
        Assert.Contains("#if (cqrs)", csproj, StringComparison.Ordinal);
        Assert.Contains("Rask.Cqrs", csproj, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(WasmPwaTemplates))]
    public void Sources_ExcludeIcon_WhenPwaOff(string shortName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, shortName, ".template.config", "template.json")));

        var modifiers = doc.RootElement.GetProperty("sources")
            .EnumerateArray()
            .SelectMany(s => s.GetProperty("modifiers").EnumerateArray());

        var pwaExclusion = modifiers.FirstOrDefault(m =>
            m.TryGetProperty("condition", out var c) && c.GetString() == "(!pwa)");

        Assert.True(pwaExclusion.ValueKind == JsonValueKind.Object,
            "expected a (!pwa) source modifier");
        var excludes = pwaExclusion.GetProperty("exclude").EnumerateArray().Select(e => e.GetString());
        Assert.Contains(excludes, e => e!.Contains("icon.svg", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(WasmPwaTemplates))]
    public void PwaScaffolding_Exists_AndProgramAndIndexHavePwaConditionals(string shortName)
    {
        var dir = Path.Combine(TemplatesRoot, shortName);

        // The icon.svg excluded by (!pwa) must actually be present to ship.
        Assert.NotEmpty(Directory.GetFiles(dir, "icon.svg", SearchOption.AllDirectories));

        // Program.cs wires UsePwa behind //#if (pwa) blocks the engine strips when pwa is off.
        var hasProgramConditional = Directory
            .GetFiles(dir, "Program.cs", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("#if (pwa)", StringComparison.Ordinal));
        Assert.True(hasProgramConditional, $"{shortName}: no Program.cs with a //#if (pwa) block");

        // index.html registers the service worker behind a <!--#if (pwa)--> block.
        var hasIndexConditional = Directory
            .GetFiles(dir, "index.html", SearchOption.AllDirectories)
            .Any(f => File.ReadAllText(f).Contains("#if (pwa)", StringComparison.Ordinal));
        Assert.True(hasIndexConditional, $"{shortName}: no index.html with a <!--#if (pwa)--> block");
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void DockerSymbol_IsBoolean_DefaultsFalse(string shortName, string _)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, shortName, ".template.config", "template.json")));

        var docker = doc.RootElement.GetProperty("symbols").GetProperty("docker");
        Assert.Equal("parameter", docker.GetProperty("type").GetString());
        Assert.Equal("bool", docker.GetProperty("datatype").GetString());
        Assert.Equal("false", docker.GetProperty("defaultValue").GetString());
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Sources_ExcludeDockerfile_WhenDockerOff(string shortName, string _)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(TemplatesRoot, shortName, ".template.config", "template.json")));

        var dockerExclusion = doc.RootElement.GetProperty("sources").EnumerateArray()
            .SelectMany(s => s.GetProperty("modifiers").EnumerateArray())
            .FirstOrDefault(m => m.TryGetProperty("condition", out var c) && c.GetString() == "(!docker)");

        Assert.True(dockerExclusion.ValueKind == JsonValueKind.Object,
            "expected a (!docker) source modifier");
        var excludes = dockerExclusion.GetProperty("exclude").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Dockerfile", excludes);
        Assert.Contains(".dockerignore", excludes);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void DockerScaffolding_Exists(string shortName, string _)
    {
        var dir = Path.Combine(TemplatesRoot, shortName);

        // The Dockerfile + .dockerignore excluded by (!docker) must actually be present to ship.
        Assert.True(File.Exists(Path.Combine(dir, "Dockerfile")), $"{shortName}: missing Dockerfile");
        Assert.True(File.Exists(Path.Combine(dir, ".dockerignore")), $"{shortName}: missing .dockerignore");

        // The standalone WASM template serves its static bundle from nginx, so its nginx.conf ships too.
        if (shortName == "rask-wasm")
        {
            Assert.True(File.Exists(Path.Combine(dir, "nginx.conf")), "rask-wasm: missing nginx.conf");
        }
    }

    // Walks up from the test assembly to the repo root (the directory holding Rask.slnx) so the
    // tests find the template sources regardless of the bin/ depth they run from.
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate repo root (Rask.slnx) from " + AppContext.BaseDirectory);
    }
}

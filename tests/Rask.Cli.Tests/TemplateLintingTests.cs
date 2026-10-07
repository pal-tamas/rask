using System.Text.Json;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     An island project lints and formats itself, and a Blazor-only one asks for nothing it cannot run.
/// </summary>
/// <remarks>
///     What this CANNOT check is that the config loads — a plugin's exported config name differs per
///     plugin and per major, and a wrong one throws at ESLint startup rather than at parse time. This
///     holds the parts that can be checked without an install, which keeps them from silently going missing.
/// </remarks>
public sealed class TemplateLintingTests
{
    public static TheoryData<string> Clients() => [.. SpaFramework.All.Select(framework => framework.Key)];

    private static Dictionary<string, string> Client(string key) =>
        ProjectGenerator
            .GenerateSpa("/proj/Shop", "Shop", SpaFramework.All.Single(f => f.Key == key), new ServerBatteries(), "9.9.9")
            .Files
            .Select(f => (Path: f.Path.Replace('\\', '/'), f.Content))
            .Where(f => f.Path.StartsWith("/proj/Shop/client/", StringComparison.Ordinal))
            .ToDictionary(f => f.Path["/proj/Shop/client/".Length..], f => f.Content, StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Clients))]
    public void Every_client_can_lint_and_format_itself(string key)
    {
        var client = Client(key);

        using var manifest = JsonDocument.Parse(client["package.json"]);

        Assert.Contains("eslint.config.mjs", client.Keys);
        Assert.Contains(".prettierrc", client.Keys);
        var scripts = manifest.RootElement.GetProperty("scripts");
        Assert.All(new[] { "lint", "format", "format:check" }, script => Assert.True(scripts.TryGetProperty(script, out _), script));
        var deps = manifest.RootElement.GetProperty("devDependencies");
        Assert.All(
            new[] { "eslint", "prettier", "eslint-config-prettier", "typescript-eslint" },
            package => Assert.True(deps.TryGetProperty(package, out _), package));
    }

    // The typed client under src/rask is rewritten on every build; formatting it is a diff the next build undoes.
    [Theory]
    [MemberData(nameof(Clients))]
    public void No_client_lints_or_formats_what_the_build_generates(string key)
    {
        var client = Client(key);

        var ignored = client[".prettierignore"];

        Assert.Contains("src/rask/", ignored, StringComparison.Ordinal);
        Assert.Contains("package-lock.json", ignored, StringComparison.Ordinal);
        Assert.Contains("'src/rask/**'", client["eslint.config.mjs"], StringComparison.Ordinal);
    }

    [Fact]
    public void An_island_project_lints_the_same_way_its_framework_s_template_does()
    {
        // A React island project ships its own ESLint and Prettier setup, so `npm run lint` works in it.
        var files = TemplateMaterializer.Files(
            "/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9",
            ["react"]);

        Assert.Contains(files, f => Path.GetFileName(f.Path) == "eslint.config.mjs");
        Assert.Contains(files, f => Path.GetFileName(f.Path) == ".prettierrc");
        Assert.Contains(files, f => Path.GetFileName(f.Path) == ".prettierignore");

        var manifest = files.Single(f => Path.GetFileName(f.Path) == "package.json");
        using var document = JsonDocument.Parse(manifest.Content);

        var deps = document.RootElement.GetProperty("devDependencies");
        foreach (var package in new[]
        {
            "eslint", "prettier", "eslint-config-prettier", "typescript-eslint",
            "eslint-plugin-react-hooks",
        })
        {
            Assert.True(deps.TryGetProperty(package, out _), $"an island project declares no {package}.");
        }

        var scripts = document.RootElement.GetProperty("scripts");
        Assert.True(scripts.TryGetProperty("lint", out _));
        Assert.True(scripts.TryGetProperty("format", out _));
    }

    [Fact]
    public void A_blazor_only_island_project_asks_for_no_linting_it_cannot_run()
    {
        // Blazor has no npm side, so there is no package.json — and therefore nothing that would tell
        // `dotnet build` to probe for node and install a linter for a project with no JavaScript.
        var files = TemplateMaterializer.Files(
            "/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9",
            ["blazor"]);

        Assert.DoesNotContain(files, f => Path.GetFileName(f.Path) == "package.json");
        Assert.DoesNotContain(files, f => Path.GetFileName(f.Path) == "eslint.config.mjs");
    }
}

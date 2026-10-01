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
    [Fact]
    public void An_island_project_lints_the_same_way_its_framework_s_template_does()
    {
        // A React island project ships its own ESLint and Prettier setup, so `npm run lint` works in it.
        var files = TemplateMaterializer.Files(
            "/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9", DotnetTarget.Default,
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
            "/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9", DotnetTarget.Default,
            ["blazor"]);

        Assert.DoesNotContain(files, f => Path.GetFileName(f.Path) == "package.json");
        Assert.DoesNotContain(files, f => Path.GetFileName(f.Path) == "eslint.config.mjs");
    }
}

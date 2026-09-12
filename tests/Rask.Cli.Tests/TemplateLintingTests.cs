using System.Text.Json;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Every front-end template, and every island project, lints and formats the same way.
/// </summary>
/// <remarks>
///     <para>
///         Before the trees were committed this was whatever each creator happened to ship that week:
///         create-vite now writes an oxlint config, the Angular CLI writes only a .prettierrc, and three
///         of the meta creators write nothing at all. A scaffolded app's answer to "how do I lint this"
///         depended on which template it came from.
///     </para>
///     <para>
///         What this CANNOT check is that the config loads — a plugin's exported config name differs per
///         plugin and per major, and a wrong one throws at ESLint startup rather than at parse time. That
///         is what `npm run lint` in the template gate is for; this holds the parts that can be checked
///         without an install, which is what keeps them from silently going missing.
///     </para>
/// </remarks>
public sealed class TemplateLintingTests
{
    private static readonly string Root =
        Path.Combine(CliBuildE2E.FindRepoRoot(), "src", "Rask.Templates");

    public static TheoryData<string> Clients()
    {
        var data = new TheoryData<string>();
        foreach (var manifest in Directory.EnumerateFiles(Root, "package.json", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(Root, Path.GetDirectoryName(manifest)!)
                .Replace(Path.DirectorySeparatorChar, '/'));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void Every_client_can_lint_and_format_itself(string client)
    {
        var directory = Path.Combine(Root, client.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(Path.Combine(directory, "eslint.config.mjs")),
            $"{client} has no eslint.config.mjs, so `npm run lint` would fail on a fresh scaffold.");
        Assert.True(
            File.Exists(Path.Combine(directory, ".prettierrc")),
            $"{client} has no .prettierrc, so two developers format the same file differently.");
        Assert.True(
            File.Exists(Path.Combine(directory, ".prettierignore")),
            $"{client} has no .prettierignore, so `npm run format` rewrites generated code and lockfiles.");

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "package.json")));
        var scripts = document.RootElement.GetProperty("scripts");

        foreach (var script in new[] { "lint", "format", "format:check" })
        {
            Assert.True(
                scripts.TryGetProperty(script, out _),
                $"{client}'s package.json declares no '{script}' script.");
        }

        var deps = document.RootElement.GetProperty("devDependencies");
        foreach (var package in new[] { "eslint", "prettier", "eslint-config-prettier", "typescript-eslint" })
        {
            Assert.True(
                deps.TryGetProperty(package, out _),
                $"{client} declares no {package}, so its lint scripts cannot run.");
        }
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public void No_client_formats_what_the_build_generates(string client)
    {
        // The contracts under src/rask (app/rask on two of them) are rewritten from the C# on every
        // build. Formatting them produces a diff that the next build undoes, forever.
        var ignore = File.ReadAllText(
            Path.Combine(Root, client.Replace('/', Path.DirectorySeparatorChar), ".prettierignore"));

        Assert.Contains("rask/", ignore, StringComparison.Ordinal);
        Assert.Contains("package-lock.json", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void An_island_project_lints_the_same_way_its_framework_s_template_does()
    {
        // An island written in React and a React client linting under different rules is a framework
        // arguing with itself. The island assembly and the templates declare the same base set.
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

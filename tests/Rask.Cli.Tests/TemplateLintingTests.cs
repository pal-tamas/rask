using System.Text.Json;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Every Vite front end and island project checks, lints, formats and tests itself through Vite+, and a
///     Blazor-only one asks for nothing it cannot run.
/// </summary>
/// <remarks>
///     What this CANNOT check is that the config loads — a JS plugin's rule names differ per plugin and per
///     major, and a wrong one fails at Oxlint startup rather than at parse time; the front-end template gate
///     installs and runs each one for that. This holds the parts that can be checked without an install, which
///     keeps them from silently going missing.
/// </remarks>
public sealed class TemplateLintingTests
{
    /// <summary>The front ends built on Vite. Angular builds with its own CLI and keeps angular-eslint.</summary>
    public static TheoryData<string> ViteClients() =>
        [.. SpaFramework.All.Where(framework => framework.Key != "angular").Select(framework => framework.Key)];

    private static readonly string[] Scripts = ["check", "lint", "format", "format:check", "test", "prepare"];

    private static readonly string[] Retired =
    [
        "eslint", "@eslint/js", "typescript-eslint", "globals", "prettier", "eslint-config-prettier",
        "eslint-plugin-react-hooks", "eslint-plugin-vue", "eslint-plugin-svelte", "oxlint", "vitest",
    ];

    private static Dictionary<string, string> Client(string key, ServerBatteries? batteries = null) =>
        ProjectGenerator
            .GenerateSpa("/proj/Shop", "Shop", SpaFramework.All.Single(f => f.Key == key), batteries ?? new ServerBatteries(), "9.9.9")
            .Files
            .Select(f => (Path: f.Path.Replace('\\', '/'), f.Content))
            .Where(f => f.Path.StartsWith("/proj/Shop/client/", StringComparison.Ordinal))
            .ToDictionary(f => f.Path["/proj/Shop/client/".Length..], f => f.Content, StringComparer.Ordinal);

    private static Dictionary<string, string> Islands(params string[] runtimes) =>
        TemplateMaterializer.Files("/proj/Shop", "server", "Shop", new ServerBatteries(), "9.9.9", runtimes)
            .Select(f => (Path: f.Path.Replace('\\', '/'), f.Content))
            .Where(f => f.Path.StartsWith("/proj/Shop/", StringComparison.Ordinal))
            .ToDictionary(f => f.Path["/proj/Shop/".Length..], f => f.Content, StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(ViteClients))]
    public void Every_vite_client_checks_lints_formats_and_tests_through_vite_plus(string key)
    {
        var client = Client(key);

        using var manifest = JsonDocument.Parse(client["package.json"]);
        var scripts = manifest.RootElement.GetProperty("scripts");
        Assert.All(Scripts, script => Assert.True(scripts.TryGetProperty(script, out _), script));
        Assert.All(new[] { "dev", "build", "lint", "format", "test" },
            script => Assert.Contains("vp ", scripts.GetProperty(script).GetString(), StringComparison.Ordinal));
        AssertToolchain(manifest.RootElement);

        var config = client["vite.config.ts"];
        Assert.Contains($"from '{VitePlusToolchain.Package}'", config, StringComparison.Ordinal);
        Assert.All(new[] { "fmt: {", "lint: {", "test: {", "staged: {" },
            block => Assert.Contains(block, config, StringComparison.Ordinal));
        Assert.Contains("vite-plus/prefer-vite-plus-imports", config, StringComparison.Ordinal);
        Assert.Contains(".vite-hooks/pre-commit", client.Keys);
    }

    [Theory]
    [MemberData(nameof(ViteClients))]
    public void No_vite_client_ships_the_eslint_or_prettier_setup_vite_plus_replaced(string key)
    {
        var client = Client(key);

        using var manifest = JsonDocument.Parse(client["package.json"]);
        var deps = manifest.RootElement.GetProperty("devDependencies");
        Assert.All(Retired, package => Assert.False(deps.TryGetProperty(package, out _), package));
        Assert.DoesNotContain(client.Keys, name =>
            name.StartsWith("eslint.config.", StringComparison.Ordinal) || name.StartsWith(".prettier", StringComparison.Ordinal)
            || name == ".oxlintrc.json");
    }

    // The typed client under src/rask is rewritten on every build; formatting it is a diff the next build undoes.
    [Theory]
    [MemberData(nameof(ViteClients))]
    public void No_client_lints_or_formats_what_the_build_generates(string key)
    {
        var config = Client(key)["vite.config.ts"];

        var fmt = Block(config, "fmt");
        var lint = Block(config, "lint");
        Assert.Contains("'src/rask/**'", fmt, StringComparison.Ordinal);
        Assert.Contains("'package-lock.json'", fmt, StringComparison.Ordinal);
        Assert.Contains("'src/rask/**'", lint, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ViteClients))]
    public void The_starter_test_ships_exactly_when_the_module_it_tests_does(string key)
    {
        var withPush = Client(key, new ServerBatteries { Pwa = true, Push = true });
        var withoutPush = Client(key);

        Assert.Contains("src/push.ts", withPush.Keys);
        Assert.Contains("from './push'", withPush["src/push.test.ts"], StringComparison.Ordinal);
        Assert.DoesNotContain("src/push.ts", withoutPush.Keys);
        Assert.DoesNotContain("src/push.test.ts", withoutPush.Keys);
        // With no test at all, `vp test` still passes: a starter without push is not a failing one.
        Assert.Contains("passWithNoTests: true", withoutPush["vite.config.ts"], StringComparison.Ordinal);
    }

    [Fact]
    public void An_island_project_lints_the_same_way_its_framework_s_template_does()
    {
        // A React island project ships its own Vite+ setup, so `npm run check` works in it.
        var files = Islands("react");

        Assert.DoesNotContain(files.Keys, name => name.StartsWith("eslint.config.", StringComparison.Ordinal)
            || name.StartsWith(".prettier", StringComparison.Ordinal));
        Assert.Contains(".vite-hooks/pre-commit", files.Keys);

        using var manifest = JsonDocument.Parse(files["package.json"]);
        // Vite itself for the island build, which runs `npx vite` (the alias's core ships no `vite` command),
        // and vite-plus beside it for the checks.
        var deps = manifest.RootElement.GetProperty("devDependencies");
        Assert.Equal(FrontEndVersions.Latest, deps.GetProperty(VitePlusToolchain.Vite).GetString());
        Assert.Equal(FrontEndVersions.Latest, deps.GetProperty(VitePlusToolchain.Package).GetString());
        Assert.False(manifest.RootElement.TryGetProperty("overrides", out _));
        var scripts = manifest.RootElement.GetProperty("scripts");
        Assert.All(new[] { "check", "lint", "format", "format:check", "prepare" },
            script => Assert.True(scripts.TryGetProperty(script, out _), script));

        var islandRules = Block(files[IslandAssembly.ToolchainConfigFile], "rules");
        var clientRules = Block(Client("react")["vite.config.ts"], "rules");
        Assert.All(new[] { "'react/rules-of-hooks'", "'react/exhaustive-deps'" }, rule =>
        {
            Assert.Contains(rule, islandRules, StringComparison.Ordinal);
            Assert.Contains(rule, clientRules, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("solid", "eslint-plugin-solid")]
    [InlineData("lit", "eslint-plugin-lit")]
    public void A_runtime_oxlint_has_no_plugin_for_brings_its_eslint_plugin_as_a_js_plugin(string runtime, string plugin)
    {
        var islands = Islands(runtime);
        var client = Client(runtime);

        foreach (var (manifest, config) in new[]
        {
            (islands["package.json"], islands[IslandAssembly.ToolchainConfigFile]),
            (client["package.json"], client["vite.config.ts"]),
        })
        {
            using var document = JsonDocument.Parse(manifest);
            Assert.True(document.RootElement.GetProperty("devDependencies").TryGetProperty(plugin, out _), plugin);
            Assert.Contains($"specifier: '{plugin}'", config, StringComparison.Ordinal);
        }

        // Every rule the island project turns on, the template does too.
        var clientRules = Block(client["vite.config.ts"], "rules");
        foreach (var rule in Block(islands[IslandAssembly.ToolchainConfigFile], "rules")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith($"'{runtime}/", StringComparison.Ordinal)))
        {
            Assert.Contains(rule, clientRules, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Solid_s_rules_stay_on_solid_islands_when_react_shares_the_project()
    {
        // Both are .tsx: unscoped, `solid/no-destructure` fails the React counter on its first `vp check`.
        var shared = Block(Islands("react", "solid")[IslandAssembly.ToolchainConfigFile], "lint");
        var alone = Block(Islands("solid")[IslandAssembly.ToolchainConfigFile], "lint");

        Assert.DoesNotContain("'solid/", Block(shared, "rules"), StringComparison.Ordinal);
        Assert.Contains($"files: ['{IslandAssembly.SolidFolder}']", shared, StringComparison.Ordinal);
        Assert.Contains("'solid/no-destructure': 'error'", shared[shared.IndexOf("overrides: [", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("'solid/no-destructure': 'error'", Block(alone, "rules"), StringComparison.Ordinal);
        Assert.DoesNotContain("overrides", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void A_svelte_island_project_names_the_svelte_plugin_svelte_check_reads()
    {
        // svelte-check reads the Svelte config from the nearest vite.config; without the plugin there, the
        // island build's type check fails on "No Svelte configuration found".
        Assert.Contains("lazyPlugins(() => [svelte()])", Islands("svelte")[IslandAssembly.ToolchainConfigFile], StringComparison.Ordinal);
        Assert.DoesNotContain("plugins: lazyPlugins", Islands("react")[IslandAssembly.ToolchainConfigFile], StringComparison.Ordinal);
    }

    [Fact]
    public void Vue_islands_hold_typescript_where_the_vue_template_does()
    {
        // vue-tsc cannot drive TypeScript 7, so a Vue island project floating onto it fails its island build.
        using var vue = JsonDocument.Parse(Islands("vue")["package.json"]);
        using var react = JsonDocument.Parse(Islands("react")["package.json"]);
        using var template = JsonDocument.Parse(Client("vue")["package.json"]);

        Assert.Equal(IslandAssembly.VueTypeScript, vue.RootElement.GetProperty("devDependencies").GetProperty("typescript").GetString());
        Assert.Equal(IslandAssembly.VueTypeScript, template.RootElement.GetProperty("devDependencies").GetProperty("typescript").GetString());
        Assert.Equal(FrontEndVersions.Latest, react.RootElement.GetProperty("devDependencies").GetProperty("typescript").GetString());
    }

    [Fact]
    public void An_island_config_reaches_features_and_nothing_else()
    {
        // appsettings.json, the vendored wwwroot/js bundles and the rest are the C# side's to format.
        var config = Islands("react")[IslandAssembly.ToolchainConfigFile];

        Assert.Contains("ignorePatterns: ['/*', '!/Features/']", Block(config, "fmt"), StringComparison.Ordinal);
        Assert.Contains("ignorePatterns: ['/*', '!/Features/']", Block(config, "lint"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_island_config_is_not_a_typescript_file_scoped_typescript_would_pair()
    {
        // Every .ts in a host project is a component's scoped TypeScript; a vite.config.ts beside the
        // csproj would be an orphan (RASK015) on the first build.
        var files = Islands("react", "vue");

        Assert.Contains(IslandAssembly.ToolchainConfigFile, files.Keys);
        Assert.EndsWith(".mjs", IslandAssembly.ToolchainConfigFile, StringComparison.Ordinal);
        Assert.DoesNotContain("vite.config.ts", files.Keys);
    }

    [Fact]
    public void A_blazor_only_island_project_asks_for_no_linting_it_cannot_run()
    {
        // Blazor has no npm side, so there is no package.json — and therefore nothing that would tell
        // `dotnet build` to probe for node and install a linter for a project with no JavaScript.
        var files = Islands("blazor");

        Assert.DoesNotContain("package.json", files.Keys);
        Assert.DoesNotContain(IslandAssembly.ToolchainConfigFile, files.Keys);
    }

    // vite-plus, and vite aliased to its core in devDependencies and overrides alike, all floating together.
    private static void AssertToolchain(JsonElement manifest)
    {
        var deps = manifest.GetProperty("devDependencies");
        Assert.Equal(FrontEndVersions.Latest, deps.GetProperty(VitePlusToolchain.Package).GetString());
        Assert.Equal(VitePlusToolchain.FloatingAlias, deps.GetProperty(VitePlusToolchain.Vite).GetString());
        Assert.Equal(VitePlusToolchain.FloatingAlias, manifest.GetProperty("overrides").GetProperty(VitePlusToolchain.Vite).GetString());
    }

    // The text of a config's `name: { … }` block, up to the brace that closes it.
    private static string Block(string config, string name)
    {
        var start = config.IndexOf($"{name}: {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {name} block in:\n{config}");

        var depth = 0;
        for (var i = config.IndexOf('{', start); i < config.Length; i++)
        {
            depth += config[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return config[start..(i + 1)];
            }
        }

        return config[start..];
    }
}

using System.Reflection;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Tests;

/// <summary>
///     What <c>rask new --template react</c> writes, and what it deliberately does not.
/// </summary>
/// <remarks>
///     The build gate in <see cref="ProjectGeneratorBuildE2ETests" /> proves the host compiles and that the
///     contracts reach the client's sources. These cover the decisions a compile cannot see: which files are
///     an overlay rather than a copy, and what the two patches do to somebody else's output.
/// </remarks>
public sealed class SpaTemplateTests
{
    private const string Root = "/tmp/spa";

    private static ScaffoldResult Generate(ServerBatteries? batteries = null) =>
        ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.React, batteries ?? new ServerBatteries(), "1.2.3");

    private static string Content(ScaffoldResult result, string endsWith) =>
        result.Files.Single(f => f.Path.Replace('\\', '/').EndsWith(endsWith, StringComparison.Ordinal)).Content;

    private static bool Has(ScaffoldResult result, string endsWith) =>
        result.Files.Any(f => f.Path.Replace('\\', '/').EndsWith(endsWith, StringComparison.Ordinal));

    [Fact]
    public void The_client_skeleton_comes_from_the_framework_s_own_scaffolder()
    {
        var external = Assert.Single(Generate().ExternalScaffolds);

        Assert.Equal("npx", external.Command);
        Assert.Equal(["--yes", "create-vite@latest", "Shop.Client", "--template", "react-ts"], external.Arguments);
    }

    /// <summary>
    ///     Every framework the scaffolder knows asks <c>create-vite</c> for its <b>TypeScript</b> template.
    /// </summary>
    /// <remarks>
    ///     Reflection over the declared frameworks rather than an assertion about React, because this is the
    ///     invariant a second framework is most likely to break: create-vite ships each of them as a pair
    ///     (<c>vue</c>/<c>vue-ts</c>, <c>angular</c> is TypeScript already), and picking the wrong half
    ///     scaffolds a client the host then refuses to build with RASKSPA004. Rask supports TypeScript
    ///     single-page app clients — a JavaScript one would import the generated contracts and have nothing
    ///     check them, which is the failure the whole generated-TypeScript pipeline exists to prevent.
    /// </remarks>
    [Fact]
    public void Every_scaffolded_client_is_typescript()
    {
        var frameworks = typeof(SpaFramework)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SpaFramework))
            .Select(field => (SpaFramework)field.GetValue(null)!)
            .ToArray();

        Assert.NotEmpty(frameworks);
        foreach (var framework in frameworks)
        {
            Assert.True(
                framework.ViteTemplate.EndsWith("-ts", StringComparison.Ordinal)
                || framework.ViteTemplate == "angular",
                $"{framework.DisplayName} scaffolds '{framework.ViteTemplate}', which is not a TypeScript template.");
        }
    }

    [Fact]
    public void Only_four_client_files_are_ours()
    {
        var result = Generate();

        // Everything else in the client is create-vite's, and stays create-vite's. A React skeleton Rask
        // maintained by hand would be a worse one within a release or two — and the overlay growing is the
        // signal that the split has stopped working.
        var ours = result.Files
            .Select(f => f.Path.Replace('\\', '/'))
            .Where(p => p.Contains("/Shop.Client/", StringComparison.Ordinal))
            .Select(p => p[(p.IndexOf("/Shop.Client/", StringComparison.Ordinal) + 13)..])
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["src/App.tsx", "src/main.tsx", "vite.config.ts"], ours);
    }

    [Fact]
    public void The_dev_server_proxies_the_wire_to_the_host()
    {
        var config = Content(Generate(), "/Shop.Client/vite.config.ts");

        // The browser talks to Vite and Vite forwards /_rask, so HMR stays native and the browser only ever
        // sees one origin — which is what means there is no CORS to configure in development.
        Assert.Contains("'/_rask'", config, StringComparison.Ordinal);
        Assert.Contains("target: 'http://localhost:5000'", config, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_listens_where_the_proxy_points()
    {
        // These two numbers are one decision written in two files, and nothing else checks they agree. A
        // mismatch is a dev session where every call 502s with no clue why.
        Assert.Contains(
            "\"applicationUrl\": \"http://localhost:5000\"",
            Content(Generate(), "/Properties/launchSettings.json"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_is_mapped_before_the_spa_fallback()
    {
        var program = Content(Generate(), "/Shop.Server/Program.cs");

        // UseRaskSpa ends the pipeline with a fallback to index.html. An endpoint mapped after it is
        // shadowed by that fallback rather than reached — and the symptom is an API call answered with
        // HTML, which the browser reports as a JSON parse error.
        var map = program.IndexOf("app.MapRaskCqrs();", StringComparison.Ordinal);
        var spa = program.IndexOf("app.UseRaskSpa();", StringComparison.Ordinal);

        Assert.True(map >= 0, "the CQRS endpoints are never mapped.");
        Assert.True(spa >= 0, "the SPA is never served.");
        Assert.True(map < spa, "MapRaskCqrs must come before UseRaskSpa or the fallback shadows it.");
    }

    [Fact]
    public void The_greeting_carries_an_offset_rather_than_a_bare_DateTime()
    {
        // A DateTime with an unspecified Kind writes an ISO string with no suffix, and modern JS parses
        // that as LOCAL time — so the same payload would mean a different instant on every machine that
        // read it. This is the template teaching the right default by using it.
        var messages = Content(Generate(), "/Features/Hello/Messages.cs");

        Assert.Contains("DateTimeOffset SeenAt", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime SeenAt", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void Cqrs_is_on_whether_or_not_it_was_asked_for()
    {
        // The wire IS this template — a client that cannot dispatch has nothing to be.
        Assert.Contains(
            "AddRaskCqrsServer",
            Content(Generate(), "/Shop.Server/Program.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_advertises_no_flag_it_cannot_honour()
    {
        Assert.True(TemplateCatalog.TryGet("react", out var template));

        // --auth and --pwa both need work on the CLIENT — a login flow in React, a service worker through
        // vite-plugin-pwa — that this template does not write. Accepting them and scaffolding half of one
        // is worse than saying no.
        Assert.DoesNotContain("auth", template.SupportedFlags);
        Assert.DoesNotContain("pwa", template.SupportedFlags);
        Assert.DoesNotContain("push", template.SupportedFlags);
        Assert.Contains("data", template.SupportedFlags);
        Assert.Contains("docker", template.SupportedFlags);
    }

    [Fact]
    public void A_database_lands_in_the_host_and_is_reachable_from_its_Program()
    {
        var result = Generate(new ServerBatteries { Data = true });

        Assert.True(Has(result, "/Shop.Server/Features/Shared/AppDbContext.cs"));

        // The context is declared in the .Server namespace, so Program.cs has to import it — and the
        // failure when it does not is a compile error nothing but a real build catches.
        Assert.Contains(
            "using Shop.Server.Features.Shared;",
            Content(result, "/Shop.Server/Program.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_contracts_are_not_committed()
    {
        var result = Generate();
        var patch = result.Patches.Single(p => p.Path.EndsWith(".gitignore", StringComparison.Ordinal));

        Assert.Contains("src/rask/", patch.Transform("node_modules\ndist\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignoring_the_contracts_twice_adds_one_entry()
    {
        // rask new --force over an existing client re-applies every patch.
        var once = ProjectGenerator.IgnoreGeneratedContracts("dist\n");
        var twice = ProjectGenerator.IgnoreGeneratedContracts(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Adding_the_query_dependency_leaves_the_rest_of_package_json_alone()
    {
        const string Original = """
            {
              "name": "shop-client",
              "private": true,
              "scripts": { "build": "tsc -b && vite build" },
              "dependencies": { "react": "^19.2.8" },
              "devDependencies": { "vite": "^8.2.2" }
            }
            """;

        var patched = ProjectGenerator.AddQueryDependency(Original);

        Assert.Contains("\"@tanstack/react-query\"", patched, StringComparison.Ordinal);
        Assert.Contains("\"react\": \"^19.2.8\"", patched, StringComparison.Ordinal);
        Assert.Contains("\"vite\": \"^8.2.2\"", patched, StringComparison.Ordinal);
        Assert.Contains("tsc -b \\u0026\\u0026 vite build", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_the_query_dependency_twice_is_the_same_as_once()
    {
        var once = ProjectGenerator.AddQueryDependency("""{ "dependencies": { "react": "^19.2.8" } }""");

        Assert.Equal(once, ProjectGenerator.AddQueryDependency(once));
    }

    [Fact]
    public void A_package_json_with_no_dependencies_still_gets_one()
    {
        // create-vite's output is not ours, and its shape is free to change.
        var patched = ProjectGenerator.AddQueryDependency("""{ "name": "shop-client" }""");

        Assert.Contains("\"@tanstack/react-query\"", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_json_that_is_not_an_object_is_reported_rather_than_swallowed()
    {
        // The command turns this into a line of advice instead of a failed scaffold with a half-written
        // project on disk — but it has to be told, not silently handed unchanged content.
        Assert.Throws<InvalidOperationException>(() => ProjectGenerator.AddQueryDependency("[]"));
    }

    [Fact]
    public void Restore_targets_the_solution_because_there_is_no_root_project()
    {
        Assert.Equal("Shop.slnx", Generate().RestoreTarget);
    }
}

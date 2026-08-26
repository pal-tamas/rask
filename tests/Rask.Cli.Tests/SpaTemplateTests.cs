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
    ///     Reflection over the declared fields rather than over <see cref="SpaFramework.All" />, because
    ///     this catches the framework somebody adds and forgets to put in that list — which would otherwise
    ///     be invisible here and merely absent from the CLI.
    ///     <para>
    ///         create-vite ships each framework as a pair, and picking the JavaScript half scaffolds a
    ///         client the host then refuses to build with RASKSPA004. Rask supports TypeScript single-page
    ///         app clients: a JavaScript one would import the generated contracts and have nothing check
    ///         them, which is the failure the whole pipeline exists to prevent.
    ///     </para>
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
            // Angular has no create-vite template and no ViteTemplate to check; its own CLI only ever
            // produces TypeScript, so there is no JavaScript half to pick by mistake.
            if (framework.WritesViteConfig)
            {
                Assert.True(
                    framework.ViteTemplate.EndsWith("-ts", StringComparison.Ordinal),
                    $"{framework.DisplayName} scaffolds '{framework.ViteTemplate}', which is not a TypeScript template.");
            }

            Assert.Contains(framework, SpaFramework.All);
        }
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void The_overlay_stays_small(string key)
    {
        var result = ProjectGenerator.GenerateSpa(Root, "Shop", Framework(key), new ServerBatteries(), "1.2.3");

        // Everything else in the client is create-vite's, and stays create-vite's. A React skeleton Rask
        // maintained by hand would be a worse one within a release or two — so the overlay growing is the
        // signal that the split has stopped working, and this is where that shows up.
        //
        // Four is the ceiling, not a target: a Vite config for the dev proxy, an entry that installs the
        // QueryClient, the component that dispatches, and — where TanStack ships a router — its routes.
        var ours = result.Files
            .Select(f => f.Path.Replace('\\', '/'))
            .Where(p => p.Contains("/Shop.Client/", StringComparison.Ordinal))
            .Select(p => p[(p.IndexOf("/Shop.Client/", StringComparison.Ordinal) + 13)..])
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        // Angular declares its dev proxy in angular.json and gets proxy.conf.json instead — there is no
        // vite.config.ts to write, because the Vite config Angular's build runs on is Angular's own.
        Assert.Contains(Framework(key).WritesViteConfig ? "vite.config.ts" : "proxy.conf.json", ours);
        Assert.InRange(ours.Length, 2, 4);
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

        var patched = ProjectGenerator.AddClientDependencies(Original, SpaFramework.React);

        Assert.Contains("\"@tanstack/react-query\"", patched, StringComparison.Ordinal);
        Assert.Contains("\"react\": \"^19.2.8\"", patched, StringComparison.Ordinal);
        Assert.Contains("\"vite\": \"^8.2.2\"", patched, StringComparison.Ordinal);
        Assert.Contains("tsc -b \\u0026\\u0026 vite build", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_the_query_dependency_twice_is_the_same_as_once()
    {
        var once = ProjectGenerator.AddClientDependencies("""{ "dependencies": { "react": "^19.2.8" } }""", SpaFramework.React);

        Assert.Equal(once, ProjectGenerator.AddClientDependencies(once, SpaFramework.React));
    }

    [Fact]
    public void A_package_json_with_no_dependencies_still_gets_one()
    {
        // create-vite's output is not ours, and its shape is free to change.
        var patched = ProjectGenerator.AddClientDependencies("""{ "name": "shop-client" }""", SpaFramework.React);

        Assert.Contains("\"@tanstack/react-query\"", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_json_that_is_not_an_object_is_reported_rather_than_swallowed()
    {
        // The command turns this into a line of advice instead of a failed scaffold with a half-written
        // project on disk — but it has to be told, not silently handed unchanged content.
        Assert.Throws<InvalidOperationException>(() => ProjectGenerator.AddClientDependencies("[]", SpaFramework.React));
    }

    public static IEnumerable<object[]> Frameworks() =>
        SpaFramework.All.Select(framework => new object[] { framework.Key });

    private static SpaFramework Framework(string key)
    {
        Assert.True(SpaFramework.TryGet(key, out var framework));
        return framework;
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_is_offered_as_a_template(string key)
    {
        // The catalog is DERIVED from this list rather than repeating it. Two hand-maintained lists of
        // the same frameworks is how `--template native` came to be accepted by the parser and then
        // generate a server app.
        Assert.True(TemplateCatalog.TryGet(key, out var template));
        Assert.Equal(key, template.Key);
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_scaffolds_something_that_can_dispatch(string key)
    {
        var framework = Framework(key);
        var result = ProjectGenerator.GenerateSpa(Root, "Shop", framework, new ServerBatteries(), "1.2.3");

        // Whatever the framework, the client has to reach the generated messages and the bridge — those
        // two imports are what the whole template exists to make possible.
        var client = string.Join(
            "\n",
            result.Files
                .Where(f => f.Path.Replace('\\', '/').Contains("/Shop.Client/", StringComparison.Ordinal))
                .Select(f => f.Content));

        Assert.Contains("rask/messages", client, StringComparison.Ordinal);
        Assert.Contains("rask/query", client, StringComparison.Ordinal);
        Assert.Contains("getGreeting", client, StringComparison.Ordinal);
        Assert.Contains("recordVisit", client, StringComparison.Ordinal);

        // Invalidation by the factory's own wire name, never a string literal — renaming the C# record
        // has to move the cache key with it.
        Assert.Contains("getGreeting.messageName", client, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_overlays_onto_the_entry_its_scaffolder_actually_wrote(string key)
    {
        var framework = Framework(key);

        // create-vite does not name these the same way — Solid boots from src/index.tsx, Preact's app is
        // src/app.tsx in lower case, Svelte has no App.tsx at all, and Lit has no entry module beside its
        // element. An overlay written for the wrong name does not fail: it lands beside the real file and
        // is never imported, so the app builds and shows the scaffolder's placeholder instead.
        var expected = key switch
        {
            "react" => "src/main.tsx",
            "preact" => "src/main.tsx",
            "vue" => "src/App.vue",
            "angular" => "src/app/app.ts",
            "solid" => "src/index.tsx",
            "svelte" => "src/App.svelte",
            "lit" => "src/my-element.ts",
            _ => throw new InvalidOperationException($"'{key}' has no expected entry point in this test."),
        };

        Assert.Contains(framework.ClientFiles, file => file.Path == expected);
    }

    [SkippableTheory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_asks_its_scaffolder_for_a_TypeScript_template(string key)
    {
        // Rask supports TypeScript clients only. create-vite ships each framework as a pair, and asking
        // for the JavaScript half would scaffold a client the host then refuses to build (RASKSPA004).
        var framework = Framework(key);
        Skip.IfNot(framework.WritesViteConfig, "Angular is scaffolded by its own CLI, which is TypeScript-only.");

        Assert.EndsWith("-ts", framework.ViteTemplate, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Frameworks))]
    public void Every_framework_pins_the_TanStack_Query_adapter_its_own_code_imports(string key)
    {
        var framework = Framework(key);
        var patched = ProjectGenerator.AddClientDependencies("""{ "name": "c" }""", framework);

        Assert.Contains($"\"{framework.QueryPackage}\"", patched, StringComparison.Ordinal);

        // The adapter the package.json pins and the one the client code imports are one decision written
        // in two places, and nothing else checks they agree. A mismatch is an unresolved import at build.
        var client = string.Join("\n", framework.ClientFiles.Select(f => f.Content));
        Assert.Contains($"from '{framework.QueryPackage}'", client, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("react", "@tanstack/react-router")]
    [InlineData("solid", "@tanstack/solid-router")]
    public void React_and_Solid_get_TanStack_Router(string key, string package)
    {
        var framework = Framework(key);

        Assert.Equal(package, framework.RouterPackage);
        Assert.Contains(framework.ClientFiles, file => file.Path == "src/router.tsx");
        Assert.Contains(
            $"\"{package}\"",
            ProjectGenerator.AddClientDependencies("""{ "name": "c" }""", framework),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preact")]
    [InlineData("svelte")]
    [InlineData("lit")]
    public void Everything_else_gets_no_router(string key)
    {
        // TanStack Router ships React and Solid adapters only. Scaffolding a different router for the
        // others would be Rask picking one on their behalf, in a template whose whole argument is that
        // the framework's own conventions win.
        var framework = Framework(key);

        Assert.Null(framework.RouterPackage);
        Assert.DoesNotContain(framework.ClientFiles, file => file.Path.Contains("router", StringComparison.Ordinal));
    }

    [Fact]
    public void Svelte_type_checks_in_its_build_script()
    {
        // create-vite gives Svelte a bare `vite build`; tsc cannot read a .svelte file, so type checking
        // lives in a separate `check` script nothing runs. Left alone, renaming a C# property would break
        // NOTHING at build time and surface on the wire — the exact failure the generated contracts exist
        // to prevent. Every other framework's template already type-checks in build.
        var patched = ProjectGenerator.AddClientDependencies(
            """{ "scripts": { "build": "vite build", "check": "svelte-check" } }""",
            Framework("svelte"));

        Assert.Contains("svelte-check", patched, StringComparison.Ordinal);
        Assert.Contains("vite build", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_Svelte_has_its_build_script_rewritten()
    {
        // Somebody else's scripts are not ours to edit. Svelte is the one exception and it is argued for
        // above; a second one appearing silently is the thing this pins.
        foreach (var framework in SpaFramework.All.Where(f => f.Key != "svelte"))
        {
            var patched = ProjectGenerator.AddClientDependencies(
                """{ "scripts": { "build": "tsc -b && vite build" } }""", framework);

            Assert.Contains("tsc -b", patched, StringComparison.Ordinal);
            Assert.DoesNotContain("svelte-check", patched, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Lit_needs_no_vite_plugin()
    {
        // Its components are standard custom elements and its decorators are TypeScript's — which is why
        // create-vite ships that template with no vite.config.ts at all. The generated one exists purely
        // to carry the dev proxy, and a dangling `import  from` would not parse.
        var config = Content(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("lit"), new ServerBatteries(), "1.2.3"),
            "/Shop.Client/vite.config.ts");

        Assert.DoesNotContain("import  from", config, StringComparison.Ordinal);
        Assert.Contains("plugins: []", config, StringComparison.Ordinal);
        Assert.Contains("'/_rask'", config, StringComparison.Ordinal);
    }


    [Fact]
    public void Angular_is_scaffolded_by_its_own_CLI()
    {
        var external = Assert.Single(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("angular"), new ServerBatteries(), "1.2.3")
                .ExternalScaffolds);

        // ng new, not create-vite: Angular has no create-vite template, and its own CLI is where its
        // conventions come from. The project name has to be kebab-case — Angular rejects "Shop.Client"
        // outright — so the CLI is given shop-client with --directory Shop.Client.
        Assert.Contains("@angular/cli@latest", external.Arguments);
        Assert.Contains("shop-client", external.Arguments);
        Assert.Contains("--directory", external.Arguments);
        Assert.Contains("Shop.Client", external.Arguments);

        // The install is the build's job, and rask new initialises one repository at the solution root.
        Assert.Contains("--skip-install", external.Arguments);
        Assert.Contains("--skip-git", external.Arguments);
    }

    [Fact]
    public void The_host_is_told_where_Angular_nests_its_bundle()
    {
        var csproj = Content(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("angular"), new ServerBatteries(), "1.2.3"),
            "/Shop.Server/Shop.Server.csproj");

        // Angular's default output is dist/<project>/browser. A host left pointing at dist/ serves the
        // "nothing built yet" page after a build that succeeded — which reads as a broken scaffold.
        Assert.Contains("<RaskSpaDistDir>dist/shop-client/browser</RaskSpaDistDir>", csproj, StringComparison.Ordinal);
        Assert.Contains("<RaskSpaDevServerUrl>http://localhost:4200</RaskSpaDevServerUrl>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_Angular_carries_a_dist_override()
    {
        // Every other framework writes to dist/, and a property restating the default is one more thing
        // that can drift from it.
        foreach (var framework in SpaFramework.All.Where(f => f.Key != "angular"))
        {
            var csproj = Content(
                ProjectGenerator.GenerateSpa(Root, "Shop", framework, new ServerBatteries(), "1.2.3"),
                "/Shop.Server/Shop.Server.csproj");

            Assert.DoesNotContain("<RaskSpaDistDir>", csproj, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Angulars_dev_server_is_pointed_at_the_proxy_file()
    {
        var patched = ProjectGenerator.UseProxyConfig("""
            { "projects": { "shop-client": { "architect": { "serve": { "builder": "@angular/build:dev-server" } } } } }
            """);

        // Written into angular.json rather than onto the start script, so `ng serve` picks it up however
        // it is launched — an IDE does not run the npm script.
        Assert.Contains("\"proxyConfig\": \"proxy.conf.json\"", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void An_angular_json_this_does_not_recognise_is_left_alone()
    {
        // Failing a scaffold over a proxy line would be worse than saying it did not happen — the CLI
        // reports the skip, and everything else on disk is still correct.
        const string Unfamiliar = """{ "version": 1 }""";

        Assert.Equal(Unfamiliar, ProjectGenerator.UseProxyConfig(Unfamiliar));
    }

    [Fact]
    public void Restore_targets_the_solution_because_there_is_no_root_project()
    {
        Assert.Equal("Shop.slnx", Generate().RestoreTarget);
    }
}

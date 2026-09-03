using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
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

    /// <summary>Runs the package.json patch over a minimal stand-in for what the scaffolder writes.</summary>
    /// <remarks>
    ///     package.json is not ours — create-vite (or ng new) writes it and the generator patches it — so
    ///     asking the result for the file finds nothing. The patch is the thing under test.
    /// </remarks>
    private static string PackageJson(ScaffoldResult result)
    {
        var patch = result.Patches.Single(p => p.Path.EndsWith("package.json", StringComparison.Ordinal));

        return patch.Transform("""{ "dependencies": {}, "devDependencies": {}, "scripts": {} }""");
    }

    [Fact]
    public void The_client_skeleton_comes_from_the_framework_s_own_scaffolder()
    {
        var external = Assert.Single(Generate().ExternalScaffolds);

        Assert.Equal("npx", external.Command);
        Assert.Equal(["--yes", "create-vite@latest", "Shop/Client", "--template", "react-ts"], external.Arguments);
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
        // Six is the ceiling, not a target: a Vite config for the dev proxy, an entry that installs the
        // QueryClient, the component that dispatches, — where TanStack ships a router — its routes, the
        // Tailwind stylesheet that REPLACES create-vite's demo one, and on Angular a .postcssrc.json,
        // because Angular's Vite config belongs to @angular/build and has no plugin slot to use.
        //
        // It was four while styling was a choice and Tailwind was one answer. Raising a ceiling is exactly
        // the move this test exists to make someone justify, so: the two new files are the ones Tailwind
        // needs to compile at all, and neither is a hand-maintained copy of a framework's own skeleton.
        var ours = result.Files
            .Select(f => f.Path.Replace('\\', '/'))
            .Where(p => p.Contains("/Shop/Client/", StringComparison.Ordinal))
            .Select(p => p[(p.IndexOf("/Shop/Client/", StringComparison.Ordinal) + 13)..])
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        // Angular declares its dev proxy in angular.json and gets proxy.conf.json instead — there is no
        // vite.config.ts to write, because the Vite config Angular's build runs on is Angular's own.
        Assert.Contains(Framework(key).WritesViteConfig ? "vite.config.ts" : "proxy.conf.json", ours);
        Assert.InRange(ours.Length, 2, 6);
    }

    [Fact]
    public void The_dev_server_proxies_the_wire_to_the_host()
    {
        var config = Content(Generate(), "/Shop/Client/vite.config.ts");

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
        var program = Content(Generate(), "/Shop/Program.cs");

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
            Content(Generate(), "/Shop/Program.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_template_advertises_no_flag_it_cannot_honour()
    {
        Assert.True(TemplateCatalog.TryGet("react", out var template));

        // --auth still needs work on the CLIENT this template does not write — a sign-in flow in the
        // framework's own idiom — and accepting it to scaffold half of one is worse than saying no.
        Assert.DoesNotContain("auth", template.SupportedFlags);

        // --pwa and --push are honoured: the manifest, the service worker and the subscription call are
        // the client's own files, and none of them needs a login.
        Assert.Contains("pwa", template.SupportedFlags);
        Assert.Contains("push", template.SupportedFlags);
        Assert.Contains("data", template.SupportedFlags);
        Assert.Contains("docker", template.SupportedFlags);
    }

    [Fact]
    public void A_database_lands_in_the_host_and_is_reachable_from_its_Program()
    {
        var result = Generate(new ServerBatteries { Data = true });

        Assert.True(Has(result, "/Shop/Features/Shared/AppDbContext.cs"));

        // The context is declared in the .Server namespace, so Program.cs has to import it — and the
        // failure when it does not is a compile error nothing but a real build catches.
        Assert.Contains(
            "using Shop.Features.Shared;",
            Content(result, "/Shop/Program.cs"),
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
                .Where(f => f.Path.Replace('\\', '/').Contains("/Shop/Client/", StringComparison.Ordinal))
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
        // create-vite ships that template with no vite.config.ts at all. The generated one exists to carry
        // the dev proxy and Tailwind, and a dangling `import  from` would not parse.
        var config = Content(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("lit"), new ServerBatteries(), "1.2.3"),
            "/Shop/Client/vite.config.ts");

        Assert.DoesNotContain("import  from", config, StringComparison.Ordinal);

        // Tailwind's plugin and NOTHING else: the empty-list assertion this replaced was checking that Lit
        // contributes no framework plugin of its own, and that is still the thing worth pinning.
        Assert.Contains("plugins: [tailwindcss()]", config, StringComparison.Ordinal);
        Assert.Contains("'/_rask'", config, StringComparison.Ordinal);
    }


    /// <summary>
    ///     Every framework gets Tailwind through the adapter its own build can actually read.
    /// </summary>
    /// <remarks>
    ///     The failure this pins is silent: install the wrong adapter and the packages are there, the
    ///     stylesheet is there, the build succeeds — and every utility class is missing from the output.
    ///     Nothing reports it, so only a test that looks at the wiring catches it.
    /// </remarks>
    [Fact]
    public void Tailwind_uses_the_vite_plugin_where_there_is_a_vite_config_and_postcss_where_there_is_not()
    {
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "Shop", framework, new ServerBatteries(), "1.2.3");

            var packageJson = PackageJson(result);
            Assert.Contains("\"tailwindcss\"", packageJson, StringComparison.Ordinal);

            if (framework.WritesViteConfig)
            {
                Assert.Contains("\"@tailwindcss/vite\"", packageJson, StringComparison.Ordinal);
                Assert.DoesNotContain("@tailwindcss/postcss", packageJson, StringComparison.Ordinal);
                Assert.False(Has(result, "/Shop/Client/.postcssrc.json"));

                var config = Content(result, "/Shop/Client/vite.config.ts");
                Assert.Contains("import tailwindcss from '@tailwindcss/vite'", config, StringComparison.Ordinal);
                Assert.Contains("tailwindcss()", config, StringComparison.Ordinal);
            }
            else
            {
                // Angular: its Vite config belongs to @angular/build, so there is nowhere to register a
                // plugin. The builder reads .postcssrc.json from the project root on its own.
                Assert.Contains("\"@tailwindcss/postcss\"", packageJson, StringComparison.Ordinal);
                Assert.DoesNotContain("@tailwindcss/vite", packageJson, StringComparison.Ordinal);
                Assert.Contains(
                    "@tailwindcss/postcss", Content(result, "/Shop/Client/.postcssrc.json"), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    ///     Tailwind replaces the scaffolder's own global stylesheet, at whatever name that one uses.
    /// </summary>
    /// <remarks>
    ///     Not the same file in any two of them — <c>index.css</c>, <c>style.css</c>, <c>app.css</c>,
    ///     <c>styles.css</c>. Overlaying the wrong name does not fail: the file lands beside the real one,
    ///     nothing imports it, and the app builds with no Tailwind in it at all.
    /// </remarks>
    [Fact]
    public void Tailwind_overwrites_the_stylesheet_the_entry_point_already_imports()
    {
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "Shop", framework, new ServerBatteries(), "1.2.3");

            var sheet = Content(result, $"/Shop/Client/{framework.GlobalStylesheet}");
            Assert.Contains("@import \"tailwindcss\";", sheet, StringComparison.Ordinal);

            // v4 needs no config file and no content array: it detects the sources itself.
            Assert.DoesNotContain("content:", sheet, StringComparison.Ordinal);
            Assert.False(Has(result, "/Shop/Client/tailwind.config.js"));
        }
    }

    /// <summary>
    ///     The Tailwind stylesheet styles every element the starter actually renders.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Overwriting the scaffolder's stylesheet is only half a job. That file was styling
    ///         <c>body</c>, <c>h1</c> and the rest BY TAG, and the overlay still renders those tags — so a
    ///         replacement that is just <c>@import "tailwindcss";</c> takes the page's styling away and
    ///         lets preflight reset what the browser had left. <c>--tailwind</c> then produced a visibly
    ///         worse page than no flag at all, and every check stayed green
    ///         (<see href="https://github.com/pal-tamas/rask/issues/859" />).
    ///     </para>
    ///     <para>
    ///         Driven off the markup rather than a hand-kept list: an element the starters render is read
    ///         out of their own source here, so adding one to the markup and forgetting to style it fails
    ///         this test instead of shipping. The classless markup is asserted too — that is the premise
    ///         the base layer rests on, and if it ever stops holding, styling by element is the wrong
    ///         answer and this should be reconsidered rather than quietly extended.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Tailwind_styles_the_elements_the_starter_renders()
    {
        string[] elements = ["main", "h1", "label", "input", "button"];

        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "Shop", framework, new ServerBatteries(), "1.2.3");

            var sheet = Content(result, $"/Shop/Client/{framework.GlobalStylesheet}");
            var markup = string.Join("\n", framework.ClientFiles.Select(file => file.Content));

            // Matched on the trimmed line rather than on indentation, so re-indenting the stylesheet is
            // not a test failure. A selector opening a block is what is being looked for.
            var rules = sheet.Split('\n').Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);

            // The premise. Utilities reach this markup by element or not at all.
            Assert.DoesNotContain("class=", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("className=", markup, StringComparison.Ordinal);

            foreach (var element in elements)
            {
                Assert.Contains($"<{element}", markup, StringComparison.Ordinal);

                Assert.True(
                    rules.Contains($"{element} {{"),
                    $"[{framework.Key}] the starter renders <{element}> and the Tailwind stylesheet has no "
                    + $"rule for it, so that element ships unstyled — preflight having removed whatever the "
                    + "browser gave it.");
            }

            // Rules alone would pass with an empty body; the utilities are the point.
            Assert.Contains("@apply", sheet, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_client_carries_the_wiring_Tailwind_needs_to_compile()
    {
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(Root, "Shop", framework, new ServerBatteries(), "1.2.3");

            Assert.Contains("tailwindcss", PackageJson(result), StringComparison.Ordinal);

            // Angular's Vite config belongs to @angular/build and has no plugin slot, so it takes Tailwind
            // through PostCSS instead. Without the file the packages install and NOTHING compiles the
            // stylesheet: the app builds, and every utility class is silently missing.
            Assert.Equal(
                !framework.WritesViteConfig,
                Has(result, "/Shop/Client/.postcssrc.json"));
        }
    }

    // What create-vite's react-ts template actually writes, indentation and all. The patch is applied to
    // somebody else's file, so the fixture has to be their file rather than a tidy stand-in.
    private const string ViteIndexHtml =
        """
        <!doctype html>
        <html lang="en">
          <head>
            <meta charset="UTF-8" />
            <link rel="icon" type="image/svg+xml" href="/favicon.svg" />
            <meta name="viewport" content="width=device-width, initial-scale=1.0" />
            <title>shop-client</title>
          </head>
          <body>
            <div id="root"></div>
            <script type="module" src="/src/main.tsx"></script>
          </body>
        </html>
        """;

    /// <summary>
    ///     Both URLs the patch writes are root-absolute, and that is the whole point of them.
    /// </summary>
    /// <remarks>
    ///     A SPA serves one index.html at every route. Relative URLs would resolve against the current
    ///     path, so the manifest 404s on any deep link and the service worker takes its scope from
    ///     <c>/orders/</c> instead of <c>/</c> — registering fine, controlling one sub-tree, and never
    ///     seeing a push. Nothing reports either failure.
    /// </remarks>
    [Fact]
    public void The_manifest_and_service_worker_are_referenced_from_the_origin_root()
    {
        var patched = ProjectGenerator.LinkManifestAndServiceWorker(ViteIndexHtml);

        Assert.Contains("""<link href="/manifest.webmanifest" rel="manifest"/>""", patched, StringComparison.Ordinal);
        Assert.Contains("""register("/rask-sw.js")""", patched, StringComparison.Ordinal);

        // The negative half: no bare relative form survived anywhere in the document.
        Assert.DoesNotContain("href=\"manifest.webmanifest", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("""register("rask-sw.js")""", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void The_patch_lands_inside_the_head_aligned_with_its_siblings()
    {
        var patched = ProjectGenerator.LinkManifestAndServiceWorker(ViteIndexHtml);

        // A manifest link outside <head> is ignored by every browser, silently.
        var link = patched.IndexOf("rel=\"manifest\"", StringComparison.Ordinal);
        var headEnd = patched.IndexOf("</head>", StringComparison.Ordinal);
        Assert.InRange(link, 0, headEnd);

        // Aligned with the <title> above it (4 spaces), not with the </head> below it (2).
        Assert.Contains("\n    <link href=\"/manifest.webmanifest\"", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Patching_a_document_twice_changes_nothing_the_second_time()
    {
        var once = ProjectGenerator.LinkManifestAndServiceWorker(ViteIndexHtml);

        Assert.Equal(once, ProjectGenerator.LinkManifestAndServiceWorker(once), StringComparer.Ordinal);
    }

    /// <summary>A document this does not understand is left exactly as it was found.</summary>
    /// <remarks>
    ///     The head-less case is not hypothetical: it is what a scaffolder's template looks like the day it
    ///     changes shape. Appending blindly would move the failure from "no PWA" to "broken document".
    ///     The empty and newline-only cases are the ones that threw while this was being written — the
    ///     backward scan started ON the newline it was looking for and took an empty slice.
    /// </remarks>
    [Theory]
    [InlineData("<html><body>nothing to patch</body></html>")]
    [InlineData("")]
    [InlineData("\n")]
    public void A_document_with_no_head_to_patch_is_returned_unchanged(string html)
    {
        Assert.Equal(html, ProjectGenerator.LinkManifestAndServiceWorker(html), StringComparer.Ordinal);
    }

    /// <summary>A document whose head closes with nothing above it still patches rather than throwing.</summary>
    /// <remarks>
    ///     These are the inputs that threw while this was being written: the backward search for a sibling
    ///     to copy the indentation from started ON the newline that ended the line it was looking for, took
    ///     an empty slice, and asked for a negative length. It surfaced as an unhandled exception out of
    ///     `rask new`, after the project had already been written to disk.
    /// </remarks>
    [Theory]
    [InlineData("</head>")]
    [InlineData("\n</head>")]
    [InlineData("<head>\n\n</head>")]
    [InlineData("<head>\r\n  <title>x</title>\r\n</head>")]
    public void A_head_with_nothing_to_align_against_is_patched_without_throwing(string html)
    {
        var patched = ProjectGenerator.LinkManifestAndServiceWorker(html);

        Assert.Contains("rel=\"manifest\"", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Pwa_writes_the_manifest_the_icon_and_the_service_worker_into_the_bundle_root()
    {
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "Shop", framework, new ServerBatteries { Pwa = true }, "1.2.3");

            // public/ because every bundler copies it verbatim to the bundle root — so these are reachable
            // at / in a build AND under the dev server, where only /_rask is proxied to the host.
            Assert.True(Has(result, "/Shop/Client/public/manifest.webmanifest"));
            Assert.True(Has(result, "/Shop/Client/public/icon.svg"));
            Assert.True(Has(result, "/Shop/Client/public/rask-sw.js"));

            var worker = Content(result, "/Shop/Client/public/rask-sw.js");
            Assert.Contains("addEventListener(\"push\"", worker, StringComparison.Ordinal);
            Assert.Contains("notificationclick", worker, StringComparison.Ordinal);

            // Deliberately no app-shell cache: the bundler fingerprints its assets and rewrites index.html
            // every build, so a cached shell would point at hashed files that no longer exist.
            Assert.DoesNotContain("caches.open", worker, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     <c>--push</c> reaches both halves, and the client half is typed against what the host binds.
    /// </summary>
    [Fact]
    public void Push_scaffolds_the_client_helper_and_the_hosts_endpoints()
    {
        var result = ProjectGenerator.GenerateSpa(
            Root, "Shop", SpaFramework.React, new ServerBatteries { Push = true }.Normalized(), "1.2.3");

        // src/push.ts, NOT src/rask/. What this file decides — which endpoints, and when to ask for
        // permission — is the developer's, so it is a committed source file they can edit. src/rask/ is
        // build output that .gitignore excludes, which is where this used to be scaffolded: hand-owned,
        // regenerated by nothing, and gone after a fresh clone.
        var client = Content(result, "/Shop/Client/src/push.ts");
        Assert.Contains("/_push/key", client, StringComparison.Ordinal);
        Assert.Contains("/_push/subscribe", client, StringComparison.Ordinal);
        Assert.Contains("/_push/unsubscribe", client, StringComparison.Ordinal);

        // The flattening that makes this work at all now comes from the shared browser layer, which is
        // refreshed from the package on every build and is the same code Rask's own clients run. The
        // browser nests { endpoint, keys: { p256dh, auth } } while the host binds a flat record; posting
        // the nested shape still answers 204, with both keys null, and every send afterwards fails to
        // encrypt for a subscription that looked like it registered.
        Assert.Contains("from './rask/browser/webPush'", client, StringComparison.Ordinal);

        var store = Content(result, "/Shop/Features/Push/PushSubscriptions.cs");
        // Re-namespaced into the .Server project, which is the half with the endpoints on it.
        Assert.Contains("namespace Shop.Features.Push;", store, StringComparison.Ordinal);
        Assert.Contains("MapPushSubscriptions", store, StringComparison.Ordinal);

        // Mapped before UseRaskSpa, which ends the pipeline with a fallback to index.html — an endpoint
        // added after it would answer HTML instead of JSON.
        var program = Content(result, "/Shop/Program.cs");
        Assert.InRange(
            program.IndexOf("app.MapPushSubscriptions();", StringComparison.Ordinal),
            0,
            program.IndexOf("app.UseRaskSpa();", StringComparison.Ordinal));
    }

    [Fact]
    public void No_scaffolded_file_lands_in_a_directory_the_scaffold_gitignores()
    {
        // The general form of the defect that put push.ts in src/rask/: a file written ONCE by the
        // scaffolder, into a directory the same scaffolder tells git to ignore, and regenerated by
        // nothing. It survives exactly until someone clones the repository, and then it is gone with no
        // error that names it.
        //
        // Asserted over every scaffolded file rather than over push.ts, because the next one to land
        // there would be just as invisible.
        var result = ProjectGenerator.GenerateSpa(
            Root,
            "Shop",
            SpaFramework.React,
            new ServerBatteries { Push = true, Pwa = true }.Normalized(),
            "1.2.3");

        var gitignore = result.Patches
            .Single(p => p.Path.EndsWith(".gitignore", StringComparison.Ordinal));

        var ignored = gitignore.Transform("node_modules\ndist\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.EndsWith('/'))
            .ToList();

        Assert.NotEmpty(ignored);

        var buried = result.Files
            .Select(f => f.Path.Replace('\\', '/'))
            .Where(path => ignored.Any(dir => path.Contains("/" + dir, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            buried.Count == 0,
            "These files are scaffolded into a directory the scaffold adds to .gitignore, so a fresh "
            + "clone will not have them and nothing regenerates them: " + string.Join(", ", buried));
    }

    [Fact]
    public void Without_pwa_no_client_carries_a_service_worker_or_a_manifest_link()
    {
        var result = ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.React, new ServerBatteries(), "1.2.3");

        Assert.False(Has(result, "/Shop/Client/public/rask-sw.js"));
        Assert.False(Has(result, "/Shop/Client/public/manifest.webmanifest"));
        Assert.False(Has(result, "/Shop/Client/src/push.ts"));
        Assert.DoesNotContain(result.Patches, patch => patch.Path.EndsWith("index.html", StringComparison.Ordinal));
    }

    [Fact]
    public void Angular_is_scaffolded_by_its_own_CLI()
    {
        var external = Assert.Single(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("angular"), new ServerBatteries(), "1.2.3")
                .ExternalScaffolds);

        // ng new, not create-vite: Angular has no create-vite template, and its own CLI is where its
        // conventions come from. The project name has to be kebab-case — Angular rejects "Shop/Client"
        // outright — so the CLI is given shop-client with --directory Shop/Client.
        Assert.Contains("@angular/cli@latest", external.Arguments);
        Assert.Contains("shop-client", external.Arguments);
        Assert.Contains("--directory", external.Arguments);
        Assert.Contains("Shop/Client", external.Arguments);

        // The install is the build's job, and rask new initialises one repository at the solution root.
        Assert.Contains("--skip-install", external.Arguments);
        Assert.Contains("--skip-git", external.Arguments);
    }

    [Fact]
    public void The_host_is_told_where_Angular_nests_its_bundle()
    {
        var csproj = Content(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("angular"), new ServerBatteries(), "1.2.3"),
            "/Shop/Shop.csproj");

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
                "/Shop/Shop.csproj");

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

    /// <summary>
    ///     The scaffolded image installs a Node the host package's own build will accept, and installs the
    ///     current LTS rather than merely a version that clears the floor.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two different numbers, and the difference is the point. <c>RaskSpaMinimumNode</c> is a FLOOR
    ///         — the oldest Node the build tolerates — while the image is a RECOMMENDATION every scaffolded
    ///         project inherits and rebuilds on for years. An image pinned below the floor would install a
    ///         Node its own <c>dotnet publish</c> then refuses, which is a broken template rather than a
    ///         slow upgrade, so that half is checked against the shipped props rather than a literal.
    ///     </para>
    ///     <para>
    ///         The second half used to be a literal, because nothing in the repo knew which line Node calls
    ///         Active LTS. Something does now: <c>NodeRequirement.ScaffoldLine</c> states it once, the
    ///         installers and docs are held to it by <c>NodeRequirementTests</c>, and
    ///         <c>.github/workflows/lts-watch.yml</c> opens an issue when nodejs.org moves past it. So this
    ///         reads that one number instead of restating it — going backwards is still the regression
    ///         worth failing over, but "which line" is no longer this test's to know.
    ///     </para>
    /// </remarks>
    [Fact]
    public void The_docker_image_installs_the_current_node_lts()
    {
        var dockerfile = Content(Generate(new ServerBatteries { Docker = true }), "Dockerfile");

        var installed = Regex.Match(dockerfile, @"deb\.nodesource\.com/setup_(\d+)\.x");
        Assert.True(installed.Success, $"the SPA Dockerfile no longer installs Node from NodeSource:\n{dockerfile}");
        var major = int.Parse(installed.Groups[1].Value, CultureInfo.InvariantCulture);

        var props = File.ReadAllText(Path.Combine(
            CliBuildE2E.FindRepoRoot(), "src", "Rask.Spa.Hosting", "build", "Rask.Spa.Hosting.props"));
        var floor = Version.Parse(Regex.Match(props, @"<RaskSpaMinimumNode[^>]*>([0-9.]+)</RaskSpaMinimumNode>").Groups[1].Value);

        Assert.True(
            major >= floor.Major,
            $"the image installs Node {major}.x, which the build's own floor ({floor}) refuses.");

        // Against NodeRequirement.ScaffoldLine rather than a literal. The repo now DOES know which line it
        // calls Active LTS — it is stated once, in NodeRequirement, and .github/workflows/lts-watch.yml
        // opens an issue when nodejs.org moves past it. A literal here was a third copy of that number.
        Assert.True(
            major >= NodeRequirement.ScaffoldLine.Major,
            $"the image installs Node {major}.x, below the scaffold line "
            + $"({NodeRequirement.ScaffoldLine}) this repo states in src/Rask.Cli/NodeRequirement.cs.");
    }
}

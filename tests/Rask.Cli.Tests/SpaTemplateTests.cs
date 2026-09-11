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

    /// <summary>
    ///     The next-steps text names directories the scaffold actually produced.
    /// </summary>
    /// <remarks>
    ///     Nothing else asserts on this string, which is how it went on pointing at
    ///     <c>Shop.Client/src/rask/</c> after the layout moved to <c>Shop/client/</c> — user-facing
    ///     output naming a directory that does not exist, straight through a rename sweep and a full
    ///     gate. It is the last thing `rask new` prints, so it is the first thing anyone follows.
    /// </remarks>
    [Fact]
    public void Next_steps_name_directories_the_scaffold_produced()
    {
        var result = Generate();
        var notes = result.Notes ?? string.Empty;

        Assert.DoesNotContain("Shop.Client", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("Shop.Server", notes, StringComparison.Ordinal);

        // And the directory it does name is one the scaffold writes into.
        //
        // The notes are written for someone standing OUTSIDE the new project, so they carry the project
        // name; the scaffold's own paths are relative to the directory it creates and so do not. Those
        // two stopped matching literally when the target directory became the project directory (it used
        // to be nested, and `rask new Shop` wrote Shop/Shop) — so the check is that the notes name the
        // project plus a directory the scaffold really writes, rather than that one string contains the
        // other.
        Assert.Contains("Shop/client/src/rask/", notes, StringComparison.Ordinal);
        Assert.True(
            result.Files.Any(f => f.Path.Replace('\\', '/').Contains("/client/", StringComparison.Ordinal)),
            "the scaffold produced no client/ files for the next steps to point at");
    }

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

    [Fact]
    public void The_dev_server_proxies_the_wire_to_the_host()
    {
        var config = Content(Generate(), "/client/vite.config.ts");

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
        var program = Content(Generate(), "/Program.cs");

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
            Content(Generate(), "/Program.cs"),
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

        Assert.True(Has(result, "/Features/Shared/AppDbContext.cs"));

        // The context is declared in the .Server namespace, so Program.cs has to import it — and the
        // failure when it does not is a compile error nothing but a real build catches.
        Assert.Contains(
            "using Shop.Features.Shared;",
            Content(result, "/Program.cs"),
            StringComparison.Ordinal);
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

        // Whatever the framework, the client has to reach the generated messages and the dispatcher —
        // those two imports are what the whole template exists to make possible.
        var client = string.Join(
            "\n",
            result.Files
                .Where(f => f.Path.Replace('\\', '/').Contains("/client/", StringComparison.Ordinal))
                .Select(f => f.Content));

        Assert.Contains("rask/messages", client, StringComparison.Ordinal);
        Assert.Contains("rask/client", client, StringComparison.Ordinal);
        Assert.Contains("getGreeting", client, StringComparison.Ordinal);
        Assert.Contains("recordVisit", client, StringComparison.Ordinal);

        // Both directions, not just the read: a template that only fetches would not show that a command
        // goes back the same way.
        Assert.Contains("rask.dispatch(", client, StringComparison.Ordinal);
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

    [Fact]
    public void Lit_needs_no_vite_plugin()
    {
        // Its components are standard custom elements and its decorators are TypeScript's — which is why
        // create-vite ships that template with no vite.config.ts at all. The generated one exists to carry
        // the dev proxy and Tailwind, and a dangling `import  from` would not parse.
        var config = Content(
            ProjectGenerator.GenerateSpa(Root, "Shop", Framework("lit"), new ServerBatteries(), "1.2.3"),
            "/client/vite.config.ts");

        Assert.DoesNotContain("import  from", config, StringComparison.Ordinal);

        // Tailwind's plugin and NOTHING else: the empty-list assertion this replaced was checking that Lit
        // contributes no framework plugin of its own, and that is still the thing worth pinning.
        Assert.Contains("plugins: [tailwindcss()]", config, StringComparison.Ordinal);
        Assert.Contains("'/_rask'", config, StringComparison.Ordinal);
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

            var sheet = Content(result, $"/client/{framework.GlobalStylesheet}");
            Assert.Matches("@import ['\"]tailwindcss['\"];", sheet);

            // v4 needs no config file and no content array: it detects the sources itself.
            Assert.DoesNotContain("content:", sheet, StringComparison.Ordinal);
            Assert.False(Has(result, "/client/tailwind.config.js"));
        }
    }

    [Fact]
    public void Every_starter_stylesheet_compiles_daisyui()
    {
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "Shop", framework, new ServerBatteries(), "1.2.3");

            var sheet = Content(result, $"/client/{framework.GlobalStylesheet}");

            // Loaded from node_modules here, unlike the C# hosts: this lane has a package tree, so the
            // plugin resolves by name the way Node does.
            Assert.Contains("@plugin \"daisyui\";", sheet, StringComparison.Ordinal);

            // daisyUI emits into a layer Tailwind's own import does not rank, so without this statement
            // it outranks the utilities beside it and `class="btn px-8"` ignores the px-8.
            Assert.Contains(
                "@layer properties, theme, base, components, daisyui, utilities;",
                sheet,
                StringComparison.Ordinal);

            // The one element rule that survives: daisyUI paints base-100 on :root, and something has
            // to put the page's own background behind it.
            Assert.Contains("@apply bg-base-200", sheet, StringComparison.Ordinal);
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

    [Fact]
    public void Pwa_writes_the_manifest_the_icon_and_the_service_worker_into_the_bundle_root()
    {
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "Shop", framework, new ServerBatteries { Pwa = true }, "1.2.3");

            // public/ because every bundler copies it verbatim to the bundle root — so these are reachable
            // at / in a build AND under the dev server, where only /_rask is proxied to the host.
            Assert.True(Has(result, "/client/public/manifest.webmanifest"));
            Assert.True(Has(result, "/client/public/icon.svg"));
            Assert.True(Has(result, "/client/public/rask-sw.js"));

            var worker = Content(result, "/client/public/rask-sw.js");
            // Quote-agnostic: the templates are Prettier-formatted with singleQuote, and pinning a
            // quote style here makes a formatting pass look like a behaviour change.
            Assert.Matches("addEventListener\\(['\"]push['\"]", worker);
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
        var client = Content(result, "/client/src/push.ts");
        Assert.Contains("/_push/key", client, StringComparison.Ordinal);
        Assert.Contains("/_push/subscribe", client, StringComparison.Ordinal);
        Assert.Contains("/_push/unsubscribe", client, StringComparison.Ordinal);

        // The flattening that makes this work at all now comes from the shared browser layer, which is
        // refreshed from the package on every build and is the same code Rask's own clients run. The
        // browser nests { endpoint, keys: { p256dh, auth } } while the host binds a flat record; posting
        // the nested shape still answers 204, with both keys null, and every send afterwards fails to
        // encrypt for a subscription that looked like it registered.
        Assert.Contains("from './rask/browser/webPush'", client, StringComparison.Ordinal);

        var store = Content(result, "/Features/Push/PushSubscriptions.cs");
        // Re-namespaced into the .Server project, which is the half with the endpoints on it.
        Assert.Contains("namespace Shop.Features.Push;", store, StringComparison.Ordinal);
        Assert.Contains("MapPushSubscriptions", store, StringComparison.Ordinal);

        // Mapped before UseRaskSpa, which ends the pipeline with a fallback to index.html — an endpoint
        // added after it would answer HTML instead of JSON.
        var program = Content(result, "/Program.cs");
        Assert.InRange(
            program.IndexOf("app.MapPushSubscriptions();", StringComparison.Ordinal),
            0,
            program.IndexOf("app.UseRaskSpa();", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_pwa_no_client_carries_a_service_worker_or_a_manifest_link()
    {
        var result = ProjectGenerator.GenerateSpa(Root, "Shop", SpaFramework.React, new ServerBatteries(), "1.2.3");

        Assert.False(Has(result, "/client/public/rask-sw.js"));
        Assert.False(Has(result, "/client/public/manifest.webmanifest"));
        Assert.False(Has(result, "/client/src/push.ts"));
        Assert.DoesNotContain(result.Patches, patch => patch.Path.EndsWith("index.html", StringComparison.Ordinal));
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
                "/Shop.csproj");

            Assert.DoesNotContain("<RaskSpaDistDir>", csproj, StringComparison.Ordinal);
        }
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

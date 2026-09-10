using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The accounts endpoints are actually reachable from a scaffolded front end.
/// </summary>
/// <remarks>
///     <para>
///         They were not. <c>AddRaskAuth</c> came with the database and registered the services, and
///         <c>docs/spa.md</c> said "the endpoints it maps are already there" — but nothing scaffolded
///         ever called <c>MapRaskAuth()</c>, so a React app calling the <c>auth.login</c> that ships in
///         its own <c>rask/browser/</c> folder got a 404. In development it got one anyway, because the
///         dev proxies forwarded <c>/_rask</c> and nothing else.
///     </para>
///     <para>
///         Both halves are asserted here because either alone still leaves it broken, and both fail the
///         same silent way: a front end that compiles, runs, and cannot sign anyone in.
///     </para>
/// </remarks>
public sealed class JsLaneAuthWiringTests
{
    private const string Root = "/proj/App";

    [Fact]
    public void Every_spa_template_maps_the_auth_endpoints()
    {
        foreach (var framework in SpaFramework.All)
        {
            var program = Program(ProjectGenerator.GenerateSpa(
                Root, "App", framework, new ServerBatteries { Data = true }, "1.2.3"));

            Assert.Contains("app.MapRaskAuth();", program, StringComparison.Ordinal);
            Assert.Contains("app.UseAuthentication();", program, StringComparison.Ordinal);
            Assert.Contains("app.UseAuthorization();", program, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_meta_template_maps_the_auth_endpoints()
    {
        foreach (var template in MetaTemplate.All)
        {
            var program = Program(ProjectGenerator.GenerateMeta(
                Root, "App", template, new ServerBatteries { Data = true }, "1.2.3"));

            Assert.Contains("app.MapRaskAuth();", program, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Auth_is_mapped_before_the_fallback_that_would_swallow_it()
    {
        // UseRaskSpa ends the pipeline with a fallback to index.html and UseRaskMeta forwards everything
        // else to the node process, so an endpoint added after either answers HTML instead of JSON —
        // which reads as a front-end bug rather than a wiring one.
        var spa = Program(ProjectGenerator.GenerateSpa(
            Root, "App", SpaFramework.React, new ServerBatteries { Data = true }, "1.2.3"));

        Assert.True(
            spa.IndexOf("app.MapRaskAuth();", StringComparison.Ordinal)
            < spa.IndexOf("app.UseRaskSpa();", StringComparison.Ordinal),
            "MapRaskAuth must come before UseRaskSpa's fallback.");

        var meta = Program(ProjectGenerator.GenerateMeta(
            Root, "App", MetaTemplate.Nuxt, new ServerBatteries { Data = true }, "1.2.3"));

        Assert.True(
            meta.IndexOf("app.MapRaskAuth();", StringComparison.Ordinal)
            < meta.IndexOf("app.UseRaskMeta();", StringComparison.Ordinal),
            "MapRaskAuth must come before UseRaskMeta's forward.");
    }

    [Fact]
    public void An_app_with_no_database_maps_nothing_it_cannot_answer()
    {
        // Accounts arrive with the database, because they are rows. Mapping the endpoints without one
        // would answer every sign-in with an exception instead of a 404, which is worse.
        var program = Program(ProjectGenerator.GenerateSpa(
            Root, "App", SpaFramework.React, new ServerBatteries { Data = false }, "1.2.3"));

        Assert.DoesNotContain("app.MapRaskAuth();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_spa_dev_server_forwards_the_auth_endpoints()
    {
        // In a `rask dev` session the browser talks to the framework's dev server, not to Kestrel — so a
        // path it does not forward is a 404 from the bundler, with the host running perfectly beside it.
        foreach (var framework in SpaFramework.All)
        {
            var result = ProjectGenerator.GenerateSpa(
                Root, "App", framework, new ServerBatteries { Data = true }, "1.2.3");

            // Angular declares its proxy in proxy.conf.json; the Vite frameworks in vite.config.ts.
            var config = result.Files
                .Select(f => f.Content)
                .Where(c => c.Contains("/_rask", StringComparison.Ordinal))
                .ToArray();

            Assert.True(config.Length > 0, $"[{framework.Key}] no dev proxy was scaffolded at all.");

            Assert.True(
                config.Any(c => c.Contains("/api/auth", StringComparison.Ordinal)),
                $"[{framework.Key}] the dev server forwards /_rask but not /api/auth, so every sign-in "
                + "in development 404s against the bundler.");
        }
    }

    [Fact]
    public void Every_meta_template_scaffolds_both_screens()
    {
        // Route paths that are not guessable and were read off real scaffolds: a file at the wrong path
        // does not fail, the framework simply never routes it and the page 404s on a green build.
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["nuxt"] = ["app/pages/login.vue", "app/pages/register.vue"],
            ["nextjs"] = ["app/login/page.tsx", "app/register/page.tsx"],
            ["sveltekit"] = ["src/routes/login/+page.svelte", "src/routes/register/+page.svelte"],
            ["solidstart"] = ["src/routes/login.tsx", "src/routes/register.tsx"],
            ["tanstack-start"] = ["src/routes/login.tsx", "src/routes/register.tsx"],
            ["analog"] = ["src/app/pages/login.page.ts", "src/app/pages/register.page.ts"],
        };

        foreach (var template in MetaTemplate.All)
        {
            var paths = ProjectGenerator
                .GenerateMeta(Root, "App", template, new ServerBatteries { Data = true }, "1.2.3")
                .Files
                .Select(f => f.Path.Replace('\\', '/'))
                .ToArray();

            foreach (var route in expected[template.Key])
            {
                Assert.Contains(
                    paths,
                    p => p.EndsWith($"/{template.AppDir}/{route}", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Nextjs_marks_its_interactive_screen_as_a_client_component()
    {
        // App Router components are SERVER components by default, where useState does not exist. The
        // failure is a build error in a file Rask wrote, which is the worst place for one.
        var form = ProjectGenerator
            .GenerateMeta(Root, "App", MetaTemplate.Next, new ServerBatteries { Data = true }, "1.2.3")
            .Files
            .Single(f => f.Path.Replace('\\', '/').EndsWith("/auth-form.tsx", StringComparison.Ordinal))
            .Content;

        Assert.StartsWith("'use client'", form, StringComparison.Ordinal);
    }

    [Fact]
    public void Nuxt_gets_the_pages_router_its_screens_need()
    {
        // The minimal template writes no pages/ directory: app.vue renders <NuxtWelcome /> and that is
        // the whole app. Adding pages/ is what turns vue-router on, and app.vue then has to render
        // <NuxtPage /> or none of them are reachable — including an index, or `/` starts 404ing.
        var files = ProjectGenerator
            .GenerateMeta(Root, "App", MetaTemplate.Nuxt, new ServerBatteries { Data = true }, "1.2.3")
            .Files
            .ToDictionary(f => f.Path.Replace('\\', '/'), f => f.Content, StringComparer.Ordinal);

        var appVue = files.Single(f => f.Key.EndsWith("/app/app.vue", StringComparison.Ordinal)).Value;

        Assert.Contains("<NuxtPage />", appVue, StringComparison.Ordinal);
        Assert.DoesNotContain("<NuxtWelcome />", appVue, StringComparison.Ordinal);
        Assert.Contains(files, f => f.Key.EndsWith("/app/pages/index.vue", StringComparison.Ordinal));
    }

    [Fact]
    public void An_app_with_no_database_gets_no_screens_it_cannot_serve()
    {
        foreach (var template in MetaTemplate.All)
        {
            var paths = ProjectGenerator
                .GenerateMeta(Root, "App", template, new ServerBatteries { Data = false }, "1.2.3")
                .Files
                .Select(f => f.Path.Replace('\\', '/'))
                .ToArray();

            Assert.DoesNotContain(paths, p => p.Contains("login", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_meta_screen_draws_the_same_card_as_the_others()
    {
        foreach (var template in MetaTemplate.All)
        {
            var markup = string.Join("\n", template.AuthPages.Select(page => page.Content));

            foreach (var name in (string[])["hero min-h-screen", "card bg-base-100", "card-body", "btn btn-primary btn-block", "alert alert-error"])
            {
                Assert.Contains(name, markup, StringComparison.Ordinal);
            }

            // The generated client, not a hand-rolled fetch: typed, and it carries the CSRF header
            // these endpoints require.
            Assert.Contains("@rask/browser/auth", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("localStorage.", markup, StringComparison.Ordinal);
        }
    }

    private static string Program(ScaffoldResult result) =>
        result.Files
            .Single(f => f.Path.Replace('\\', '/').EndsWith("/Program.cs", StringComparison.Ordinal))
            .Content;
}
